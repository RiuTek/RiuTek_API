using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RiuTek.API.Contracts;
using RiuTek.API.IntegrationTest.Infrastructure;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Orders.Commands;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Data;
using Xunit;

namespace RiuTek.API.IntegrationTest.Http;

[Collection(IntegrationTestCollection.Name)]
public class ProductBuyNowIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ProductBuyNowIntegrationTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync() => await CleanupDataAsync();

    public async Task DisposeAsync() => await CleanupDataAsync();

    private async Task CleanupDataAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            await db.StockMovements.ExecuteDeleteAsync();
            await db.PaymentAttempts.ExecuteDeleteAsync();
            await db.Reviews.ExecuteDeleteAsync();
            await db.OrderItems.ExecuteDeleteAsync();
            await db.Orders.ExecuteDeleteAsync();
            await db.CartItems.ExecuteDeleteAsync();
            await db.Carts.ExecuteDeleteAsync();
            await db.UserAddresses.ExecuteDeleteAsync();
            await db.Products.ExecuteDeleteAsync();
            await db.Categories.ExecuteDeleteAsync();
            await db.Users.ExecuteDeleteAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private (HttpClient Client, FakeStripePaymentGateway FakeGateway) CreateClientWithFakeGateway(
        Action<FakeStripePaymentGateway>? configure = null)
    {
        var fakeGateway = new FakeStripePaymentGateway();
        configure?.Invoke(fakeGateway);

        var customFactory = _fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IStripePaymentGateway));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }
                services.AddSingleton<IStripePaymentGateway>(fakeGateway);
            });
        });

        return (customFactory.CreateClient(), fakeGateway);
    }

    private async Task<(HttpClient Client, User User, UserAddress Address, Cart? Cart)> CreateUserWithAddressAndOptionalCartAsync(
        Guid? cartProductId = null,
        int cartQuantity = 0,
        bool isUserActive = true)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var jwtGenerator = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(
            email: $"buynow_user_{suffix}@riutek.test",
            passwordHash: "dummy_hashed_password",
            fullName: $"BuyNow User {suffix}",
            role: UserRole.Customer,
            phoneNumber: "0901234567"
        )
        {
            IsActive = isUserActive
        };
        db.Users.Add(user);

        var address = new UserAddress(
            userId: user.Id,
            receiverName: $"Receiver {suffix}",
            phoneNumber: "0909999999",
            addressLine: "123 Nguyen Hue",
            ward: "Ben Nghe",
            district: "Quan 1",
            city: "TP Ho Chi Minh",
            isDefault: true
        );
        db.UserAddresses.Add(address);

        Cart? cart = null;
        if (cartProductId.HasValue && cartQuantity > 0)
        {
            cart = new Cart(user.Id);
            cart.AddItem(cartProductId.Value, cartQuantity);
            db.Carts.Add(cart);
        }

        await db.SaveChangesAsync();

        var token = jwtGenerator.GenerateAccessToken(user);
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return (client, user, address, cart);
    }

    private async Task<(Category Category, Product Product)> SeedProductAsync(
        decimal price = 250_000m,
        int stockQuantity = 50,
        bool isActive = true)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = new Category(
            name: $"Category {suffix}",
            slug: $"category-{suffix}",
            componentType: ComponentType.Accessory
        );
        db.Categories.Add(category);

        var product = new Product(
            categoryId: category.Id,
            name: $"Product {suffix}",
            slug: $"product-{suffix}",
            sku: $"SKU-{suffix}",
            brand: "RiuTek",
            price: price,
            stockQuantity: stockQuantity,
            imageUrl: "https://riutek.test/product.png",
            componentType: ComponentType.Accessory,
            specifications: new AccessorySpecification { Details = "Test Specs" }
        )
        {
            IsActive = isActive
        };
        db.Products.Add(product);

        await db.SaveChangesAsync();
        return (category, product);
    }

    // 1. COD complete persistence + 201 response
    [Fact]
    public async Task BuyNowProduct_Cod_PersistsOrder_DecrementsStock_Returns201Created()
    {
        var initialStock = 10;
        var buyQty = 2;
        var unitPrice = 200_000m;
        var (_, product) = await SeedProductAsync(price: unitPrice, stockQuantity: initialStock);
        var (client, user, address, _) = await CreateUserWithAddressAndOptionalCartAsync();

        var idempotencyKey = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/buy-now/product/{product.Id}")
        {
            Content = JsonContent.Create(new BuyNowProductRequest(
                AddressId: address.Id,
                Quantity: buyQty,
                PaymentMethod: PaymentMethod.COD,
                Notes: "COD Buy Now order"
            ))
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var orderDto = await response.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions);
        orderDto.Should().NotBeNull();
        orderDto!.Status.Should().Be(OrderStatus.Confirmed);
        orderDto.PaymentMethod.Should().Be(PaymentMethod.COD);
        orderDto.PaymentStatus.Should().Be(PaymentStatus.Pending);
        orderDto.TotalAmount.Should().Be(unitPrice * buyQty);
        orderDto.FinalAmount.Should().Be(unitPrice * buyQty);
        orderDto.DiscountAmount.Should().Be(0m);
        orderDto.Notes.Should().Be("COD Buy Now order");
        orderDto.ShippingAddress.Should().Be("123 Nguyen Hue, Ben Nghe, Quan 1, TP Ho Chi Minh");
        orderDto.PaymentAction.Should().BeNull();
        orderDto.Items.Should().ContainSingle(i =>
            i.ProductId == product.Id &&
            i.Quantity == buyQty &&
            i.UnitPrice == unitPrice &&
            i.TotalPrice == unitPrice * buyQty);

        // Verify DB persistence
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var dbProduct = await db.Products.FindAsync(product.Id);
        dbProduct!.StockQuantity.Should().Be(initialStock - buyQty);

        var dbOrder = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderDto.Id);
        dbOrder.Should().NotBeNull();
        dbOrder!.UserId.Should().Be(user.Id);
        dbOrder.Items.Should().ContainSingle();
        dbOrder.Items.First().PCBuildId.Should().BeNull();

        var movement = await db.StockMovements.FirstOrDefaultAsync(m => m.OrderId == orderDto.Id && m.ProductId == product.Id);
        movement.Should().NotBeNull();
        movement!.QuantityDelta.Should().Be(-buyQty);
        movement.Reason.Should().Be(StockMovementReason.Checkout);
    }

    // 2. Stripe redirect + PaymentAttempt
    [Fact]
    public async Task BuyNowProduct_Stripe_CreatesPendingOrderAndSession_Returns201Created()
    {
        var initialStock = 5;
        var buyQty = 1;
        var unitPrice = 300_000m;
        var (_, product) = await SeedProductAsync(price: unitPrice, stockQuantity: initialStock);
        var (_, user, address, _) = await CreateUserWithAddressAndOptionalCartAsync();

        var (client, _) = CreateClientWithFakeGateway();
        using var scopeAuth = _fixture.Factory.Services.CreateScope();
        var jwtGenerator = scopeAuth.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();
        var token = jwtGenerator.GenerateAccessToken(user);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var idempotencyKey = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/buy-now/product/{product.Id}")
        {
            Content = JsonContent.Create(new BuyNowProductRequest(
                AddressId: address.Id,
                Quantity: buyQty,
                PaymentMethod: PaymentMethod.Stripe,
                Notes: "Stripe Buy Now order"
            ))
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var orderDto = await response.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions);
        orderDto.Should().NotBeNull();
        orderDto!.Status.Should().Be(OrderStatus.PendingPayment);
        orderDto.PaymentMethod.Should().Be(PaymentMethod.Stripe);
        orderDto.PaymentStatus.Should().Be(PaymentStatus.Pending);
        orderDto.PaymentAction.Should().NotBeNull();
        orderDto.PaymentAction!.Url.Should().NotBeNullOrWhiteSpace();
        orderDto.PaymentAction.Url.Should().Contain("checkout.stripe.com");

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var dbProduct = await db.Products.FindAsync(product.Id);
        dbProduct!.StockQuantity.Should().Be(initialStock - buyQty);

        var dbOrder = await db.Orders.Include(o => o.PaymentAttempts).FirstOrDefaultAsync(o => o.Id == orderDto.Id);
        dbOrder.Should().NotBeNull();
        dbOrder!.PaymentAttempts.Should().ContainSingle();
        var attempt = dbOrder.PaymentAttempts.First();
        attempt.Status.Should().Be(PaymentAttemptStatus.Pending);
        attempt.ProviderReference.Should().NotBeNullOrWhiteSpace();
        attempt.IdempotencyKey.Should().Be($"stripe-{dbOrder.Id:N}");
        dbOrder.CheckoutIdempotencyKey.Should().Be(idempotencyKey);
    }

    // 3. Pre-existing Cart unchanged after COD
    [Fact]
    public async Task BuyNowProduct_WhenUserHasPreExistingCart_CodCheckoutLeavesCartUntouched()
    {
        var (_, productBuyNow) = await SeedProductAsync(price: 100_000m, stockQuantity: 10);
        var (_, productInCart) = await SeedProductAsync(price: 50_000m, stockQuantity: 15);

        var (client, user, address, initialCart) = await CreateUserWithAddressAndOptionalCartAsync(
            cartProductId: productInCart.Id,
            cartQuantity: 3);

        initialCart.Should().NotBeNull();
        var initialVersion = initialCart!.Version;

        var idempotencyKey = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/buy-now/product/{productBuyNow.Id}")
        {
            Content = JsonContent.Create(new BuyNowProductRequest(
                AddressId: address.Id,
                Quantity: 2,
                PaymentMethod: PaymentMethod.COD,
                Notes: null
            ))
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        // Verify cart in DB is completely untouched
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var dbCart = await db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.UserId == user.Id);
        dbCart.Should().NotBeNull();
        dbCart!.Version.Should().Be(initialVersion);
        dbCart.Items.Should().ContainSingle();
        var cartItem = dbCart.Items.First();
        cartItem.ProductId.Should().Be(productInCart.Id);
        cartItem.Quantity.Should().Be(3);
    }

    // 4. Pre-existing Cart unchanged after Stripe
    [Fact]
    public async Task BuyNowProduct_WhenUserHasPreExistingCart_StripeCheckoutLeavesCartUntouched()
    {
        var (_, productBuyNow) = await SeedProductAsync(price: 150_000m, stockQuantity: 10);
        var (_, productInCart) = await SeedProductAsync(price: 70_000m, stockQuantity: 20);

        var (_, user, address, initialCart) = await CreateUserWithAddressAndOptionalCartAsync(
            cartProductId: productInCart.Id,
            cartQuantity: 4);

        var initialVersion = initialCart!.Version;

        var (client, _) = CreateClientWithFakeGateway();
        using var scopeAuth = _fixture.Factory.Services.CreateScope();
        var jwtGenerator = scopeAuth.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();
        var token = jwtGenerator.GenerateAccessToken(user);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var idempotencyKey = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/buy-now/product/{productBuyNow.Id}")
        {
            Content = JsonContent.Create(new BuyNowProductRequest(
                AddressId: address.Id,
                Quantity: 1,
                PaymentMethod: PaymentMethod.Stripe,
                Notes: null
            ))
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var dbCart = await db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.UserId == user.Id);
        dbCart.Should().NotBeNull();
        dbCart!.Version.Should().Be(initialVersion);
        dbCart.Items.Should().ContainSingle();
        dbCart.Items.First().ProductId.Should().Be(productInCart.Id);
        dbCart.Items.First().Quantity.Should().Be(4);
    }

    // 5. Insufficient stock atomic failure (cart and stock unchanged)
    [Fact]
    public async Task BuyNowProduct_WhenInsufficientStock_ReturnsConflict_NoOrderOrStockChange_CartUntouched()
    {
        var initialStock = 2;
        var (_, productBuyNow) = await SeedProductAsync(price: 100_000m, stockQuantity: initialStock);
        var (_, productInCart) = await SeedProductAsync(price: 50_000m, stockQuantity: 10);

        var (client, user, address, initialCart) = await CreateUserWithAddressAndOptionalCartAsync(
            cartProductId: productInCart.Id,
            cartQuantity: 2);

        var initialVersion = initialCart!.Version;

        var idempotencyKey = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/buy-now/product/{productBuyNow.Id}")
        {
            Content = JsonContent.Create(new BuyNowProductRequest(
                AddressId: address.Id,
                Quantity: 5, // exceeds available 2
                PaymentMethod: PaymentMethod.COD,
                Notes: null
            ))
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var dbProduct = await db.Products.FindAsync(productBuyNow.Id);
        dbProduct!.StockQuantity.Should().Be(initialStock);

        var ordersCount = await db.Orders.CountAsync(o => o.UserId == user.Id);
        ordersCount.Should().Be(0);

        var dbCart = await db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.UserId == user.Id);
        dbCart!.Version.Should().Be(initialVersion);
        dbCart.Items.Should().ContainSingle();
    }

    // 6. Cross-user address isolation (returns 404)
    [Fact]
    public async Task BuyNowProduct_WhenUsingAnotherUsersAddress_ReturnsNotFound()
    {
        var (_, product) = await SeedProductAsync(stockQuantity: 10);
        var (client1, _, _, _) = await CreateUserWithAddressAndOptionalCartAsync();
        var (_, _, address2, _) = await CreateUserWithAddressAndOptionalCartAsync();

        var idempotencyKey = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/buy-now/product/{product.Id}")
        {
            Content = JsonContent.Create(new BuyNowProductRequest(
                AddressId: address2.Id, // user 2's address
                Quantity: 1,
                PaymentMethod: PaymentMethod.COD,
                Notes: null
            ))
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        var response = await client1.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var dbProduct = await db.Products.FindAsync(product.Id);
        dbProduct!.StockQuantity.Should().Be(10);
        (await db.Orders.CountAsync()).Should().Be(0);
    }

    // 7. Same-key replay returns same order, stock deducted only once
    [Fact]
    public async Task BuyNowProduct_SameIdempotencyKeyReplay_ReturnsSameOrder_StockDeductedOnce()
    {
        var initialStock = 10;
        var buyQty = 2;
        var (_, product) = await SeedProductAsync(stockQuantity: initialStock);
        var (client, user, address, _) = await CreateUserWithAddressAndOptionalCartAsync();

        var idempotencyKey = Guid.NewGuid().ToString("N");

        HttpRequestMessage CreateRequest() => new(HttpMethod.Post, $"/api/v1/orders/buy-now/product/{product.Id}")
        {
            Content = JsonContent.Create(new BuyNowProductRequest(
                AddressId: address.Id,
                Quantity: buyQty,
                PaymentMethod: PaymentMethod.COD,
                Notes: null
            )),
            Headers = { { "Idempotency-Key", idempotencyKey } }
        };

        // First attempt
        using var request1 = CreateRequest();
        var response1 = await client.SendAsync(request1);
        response1.StatusCode.Should().Be(HttpStatusCode.Created);
        var order1 = await response1.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions);
        order1.Should().NotBeNull();

        // Second attempt with exact same key
        using var request2 = CreateRequest();
        var response2 = await client.SendAsync(request2);
        response2.StatusCode.Should().Be(HttpStatusCode.Created);
        var order2 = await response2.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions);
        order2.Should().NotBeNull();

        order2!.Id.Should().Be(order1!.Id);
        order2.OrderNumber.Should().Be(order1.OrderNumber);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var dbProduct = await db.Products.FindAsync(product.Id);
        dbProduct!.StockQuantity.Should().Be(initialStock - buyQty);

        var orders = await db.Orders.Where(o => o.UserId == user.Id).ToListAsync();
        orders.Should().ContainSingle();

        var movements = await db.StockMovements.Where(m => m.ProductId == product.Id).ToListAsync();
        movements.Should().ContainSingle();
    }

    // 8. Concurrent same-key requests create exactly one order and no oversell
    [Fact]
    public async Task BuyNowProduct_ConcurrentRequests_SameIdempotencyKey_CreatesExactlyOneOrder()
    {
        var initialStock = 10;
        var buyQty = 2;
        var (_, product) = await SeedProductAsync(stockQuantity: initialStock);
        var (client, user, address, _) = await CreateUserWithAddressAndOptionalCartAsync();

        var idempotencyKey = Guid.NewGuid().ToString("N");

        Func<Task<HttpResponseMessage>> sendFunc = () =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/buy-now/product/{product.Id}")
            {
                Content = JsonContent.Create(new BuyNowProductRequest(
                    AddressId: address.Id,
                    Quantity: buyQty,
                    PaymentMethod: PaymentMethod.COD,
                    Notes: null
                )),
                Headers = { { "Idempotency-Key", idempotencyKey } }
            };
            return client.SendAsync(req);
        };

        var task1 = sendFunc();
        var task2 = sendFunc();
        var responses = await Task.WhenAll(task1, task2);

        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.Created));

        var order1 = await responses[0].Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions);
        var order2 = await responses[1].Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions);

        order1.Should().NotBeNull();
        order2.Should().NotBeNull();
        order1!.Id.Should().Be(order2!.Id);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var dbProduct = await db.Products.FindAsync(product.Id);
        dbProduct!.StockQuantity.Should().Be(initialStock - buyQty);

        var orders = await db.Orders.Where(o => o.UserId == user.Id).ToListAsync();
        orders.Should().ContainSingle();

        var movements = await db.StockMovements.Where(m => m.ProductId == product.Id).ToListAsync();
        movements.Should().ContainSingle();
    }

    // 9. Two different keys competing for stock: no negative stock
    [Fact]
    public async Task BuyNowProduct_ConcurrentRequests_DifferentKeys_CompeteForStock_NoNegativeStock()
    {
        var initialStock = 1;
        var (_, product) = await SeedProductAsync(stockQuantity: initialStock);
        var (client1, _, address1, _) = await CreateUserWithAddressAndOptionalCartAsync();
        var (client2, _, address2, _) = await CreateUserWithAddressAndOptionalCartAsync();

        var key1 = Guid.NewGuid().ToString("N");
        var key2 = Guid.NewGuid().ToString("N");

        var req1 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/buy-now/product/{product.Id}")
        {
            Content = JsonContent.Create(new BuyNowProductRequest(
                AddressId: address1.Id,
                Quantity: 1,
                PaymentMethod: PaymentMethod.COD,
                Notes: null
            )),
            Headers = { { "Idempotency-Key", key1 } }
        };

        var req2 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/buy-now/product/{product.Id}")
        {
            Content = JsonContent.Create(new BuyNowProductRequest(
                AddressId: address2.Id,
                Quantity: 1,
                PaymentMethod: PaymentMethod.COD,
                Notes: null
            )),
            Headers = { { "Idempotency-Key", key2 } }
        };

        var task1 = client1.SendAsync(req1);
        var task2 = client2.SendAsync(req2);
        var responses = await Task.WhenAll(task1, task2);

        var statusCodes = responses.Select(r => r.StatusCode).ToList();
        statusCodes.Should().Contain(HttpStatusCode.Created);
        statusCodes.Should().Contain(HttpStatusCode.Conflict);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var dbProduct = await db.Products.FindAsync(product.Id);
        dbProduct!.StockQuantity.Should().Be(0);

        var orders = await db.Orders.ToListAsync();
        orders.Should().ContainSingle();
    }

    // 10. Product Buy Now OrderItem.PCBuildId == null
    [Fact]
    public async Task BuyNowProduct_OrderItem_PCBuildIdAlwaysNull()
    {
        var (_, product) = await SeedProductAsync(stockQuantity: 5);
        var (client, _, address, _) = await CreateUserWithAddressAndOptionalCartAsync();

        var idempotencyKey = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/buy-now/product/{product.Id}")
        {
            Content = JsonContent.Create(new BuyNowProductRequest(
                AddressId: address.Id,
                Quantity: 1,
                PaymentMethod: PaymentMethod.COD,
                Notes: null
            ))
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var orderDto = await response.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions);
        orderDto.Should().NotBeNull();
        orderDto!.Items.Should().ContainSingle();

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var dbOrder = await db.Orders.Include(o => o.Items).FirstAsync(o => o.Id == orderDto.Id);
        dbOrder.Items.Should().AllSatisfy(i => i.PCBuildId.Should().BeNull());
    }

    // 11. Existing cart checkout regression test
    [Fact]
    public async Task CartCheckout_Regression_ContinuesToFunctionNormally()
    {
        var initialStock = 10;
        var (_, product) = await SeedProductAsync(stockQuantity: initialStock);
        var (client, user, address, cart) = await CreateUserWithAddressAndOptionalCartAsync(
            cartProductId: product.Id,
            cartQuantity: 2);

        cart.Should().NotBeNull();

        var idempotencyKey = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(
                AddressId: address.Id,
                ExpectedCartVersion: cart!.Version,
                PaymentMethod: PaymentMethod.COD,
                Notes: "Cart regression checkout"
            ))
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var dbCart = await db.Carts.Include(c => c.Items).FirstAsync(c => c.UserId == user.Id);
        dbCart.Items.Should().BeEmpty();
        dbCart.Version.Should().Be(cart.Version + 1);

        var dbProduct = await db.Products.FindAsync(product.Id);
        dbProduct!.StockQuantity.Should().Be(initialStock - 2);
    }
}
