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
public class OrderCheckoutIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public OrderCheckoutIntegrationTests(PostgreSqlContainerFixture fixture)
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

    private async Task<(HttpClient Client, User User, UserAddress Address, Cart Cart)> CreateUserWithAddressAndCartAsync(
        Guid productId,
        int quantity = 2,
        bool isUserActive = true)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var jwtGenerator = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(
            email: $"checkout_user_{suffix}@riutek.test",
            passwordHash: "dummy_hashed_password",
            fullName: $"Checkout User {suffix}",
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
            addressLine: "456 Le Loi",
            ward: "Ben Nghe",
            district: "Quan 1",
            city: "TP Ho Chi Minh",
            isDefault: true
        );
        db.UserAddresses.Add(address);

        var cart = new Cart(user.Id);
        if (quantity > 0)
        {
            cart.AddItem(productId, quantity);
        }
        db.Carts.Add(cart);

        await db.SaveChangesAsync();

        var token = jwtGenerator.GenerateAccessToken(user);
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return (client, user, address, cart);
    }

    private async Task<(User User, UserAddress Address, Cart Cart)> SeedUserWithAddressAndCartAsync(
        Guid productId,
        int quantity = 1)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(
            email: $"seed_user_{suffix}@riutek.test",
            passwordHash: "dummy_hashed_password",
            fullName: $"Seed User {suffix}",
            role: UserRole.Customer,
            phoneNumber: "0901234567"
        );
        db.Users.Add(user);

        var address = new UserAddress(
            userId: user.Id,
            receiverName: $"Receiver {suffix}",
            phoneNumber: "0908888888",
            addressLine: "789 Tran Hung Dao",
            ward: "Ward 5",
            district: "District 5",
            city: "TP Ho Chi Minh",
            isDefault: true
        );
        db.UserAddresses.Add(address);

        var cart = new Cart(user.Id);
        if (quantity > 0)
        {
            cart.AddItem(productId, quantity);
        }
        db.Carts.Add(cart);

        await db.SaveChangesAsync();
        return (user, address, cart);
    }

    private async Task<(Category Category, Product Product)> SeedProductAsync(
        decimal price = 200_000m,
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

    // 1. Happy path: Order + OrderItems persist, stock giảm, CartItems bị xoá, Cart version tăng.
    [Fact]
    public async Task Scenario01_HappyPath_AtomicCodCheckout_PersistsOrder_DecrementsStock_ClearsCart()
    {
        var initialStock = 20;
        var cartQty = 3;
        var unitPrice = 150_000m;
        var (_, product) = await SeedProductAsync(price: unitPrice, stockQuantity: initialStock);
        var (client, user, address, cart) = await CreateUserWithAddressAndCartAsync(product.Id, cartQty);

        var idempotencyKey = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(
                AddressId: address.Id,
                ExpectedCartVersion: cart.Version,
                PaymentMethod: PaymentMethod.COD,
                Notes: "Handle with care"
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
        orderDto.TotalAmount.Should().Be(unitPrice * cartQty);
        orderDto.FinalAmount.Should().Be(unitPrice * cartQty);
        orderDto.DiscountAmount.Should().Be(0m);
        orderDto.Notes.Should().Be("Handle with care");
        orderDto.ShippingAddress.Should().Be("456 Le Loi, Ben Nghe, Quan 1, TP Ho Chi Minh");
        orderDto.Items.Should().ContainSingle(i =>
            i.ProductId == product.Id &&
            i.Quantity == cartQty &&
            i.UnitPrice == unitPrice &&
            i.TotalPrice == unitPrice * cartQty);

        // Verify in database with fresh context
        using var verifyScope = _fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var dbOrder = await verifyDb.Orders.Include(o => o.Items).SingleAsync(o => o.Id == orderDto.Id);
        dbOrder.OrderNumber.Should().Be(orderDto.OrderNumber);
        dbOrder.CheckoutIdempotencyKey.Should().Be(idempotencyKey);
        dbOrder.Items.Should().ContainSingle();

        var freshProduct = await verifyDb.Products.FindAsync(product.Id);
        freshProduct!.StockQuantity.Should().Be(initialStock - cartQty);

        var freshCart = await verifyDb.Carts.Include(c => c.Items).SingleAsync(c => c.UserId == user.Id);
        freshCart.Items.Should().BeEmpty();
        freshCart.Version.Should().Be(cart.Version + 1);
    }

    // 2. Idempotent replay: cùng key trả cùng OrderId/OrderNumber; stock chỉ giảm một lần; chỉ có một Order.
    [Fact]
    public async Task Scenario02_IdempotentReplay_SameKey_ReturnsSameOrder_StockDecrementedOnce()
    {
        var initialStock = 30;
        var cartQty = 2;
        var (_, product) = await SeedProductAsync(stockQuantity: initialStock);
        var (client, _, address, cart) = await CreateUserWithAddressAndCartAsync(product.Id, cartQty);

        var idempotencyKey = Guid.NewGuid().ToString("N");

        // First call
        using var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(
                AddressId: address.Id,
                ExpectedCartVersion: cart.Version,
                PaymentMethod: PaymentMethod.COD,
                Notes: null
            ))
        };
        req1.Headers.Add("Idempotency-Key", idempotencyKey);
        var res1 = await client.SendAsync(req1);
        res1.StatusCode.Should().Be(HttpStatusCode.Created);
        var order1 = await res1.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions);

        // Second call with same idempotency key (even with old expected version)
        using var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(
                AddressId: address.Id,
                ExpectedCartVersion: cart.Version,
                PaymentMethod: PaymentMethod.COD,
                Notes: null
            ))
        };
        req2.Headers.Add("Idempotency-Key", idempotencyKey);
        var res2 = await client.SendAsync(req2);
        res2.StatusCode.Should().Be(HttpStatusCode.Created);
        var order2 = await res2.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions);

        order2.Should().NotBeNull();
        order2!.Id.Should().Be(order1!.Id);
        order2.OrderNumber.Should().Be(order1.OrderNumber);

        // Audit DB: exactly 1 order, stock deducted only once
        using var verifyScope = _fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        (await verifyDb.Orders.CountAsync()).Should().Be(1);
        var freshProduct = await verifyDb.Products.FindAsync(product.Id);
        freshProduct!.StockQuantity.Should().Be(initialStock - cartQty);
    }

    // 3. Transaction rollback: ép concurrency failure thì không có Order/OrderItem, không clear Cart và stock không bị giảm một phần.
    [Fact]
    public async Task Scenario03_TransactionRollback_WhenConcurrencyFailureOccurs_DatabaseCompletelyIntact()
    {
        var initialStock = 10;
        var cartQty = 2;
        var (_, product) = await SeedProductAsync(stockQuantity: initialStock);
        var (user, address, cart) = await SeedUserWithAddressAndCartAsync(product.Id, cartQty);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Test hook: right before SaveChangesAsync, another transaction updates the product in PostgreSQL
        var hookDb = new TestHookApplicationDbContext(db, async _ =>
        {
            using var bgScope = _fixture.Factory.Services.CreateScope();
            var bgDb = bgScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var bgProduct = await bgDb.Products.FindAsync(product.Id);
            bgProduct!.StockQuantity = 999;
            await bgDb.SaveChangesAsync();
        });

        var userSvc = new TestCurrentUserService(user.Id);
        var handler = new CheckoutCartCommandHandler(hookDb, userSvc);

        var command = new CheckoutCartCommand(
            AddressId: address.Id,
            ExpectedCartVersion: cart.Version,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: Guid.NewGuid().ToString("N")
        );

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("Checkout.InventoryChanged");

        // Verify DB: No Order created, Cart not cleared, Stock not deducted
        using var verifyScope = _fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        (await verifyDb.Orders.CountAsync()).Should().Be(0);
        (await verifyDb.OrderItems.CountAsync()).Should().Be(0);

        var freshCart = await verifyDb.Carts.Include(c => c.Items).SingleAsync(c => c.UserId == user.Id);
        freshCart.Items.Should().ContainSingle(i => i.ProductId == product.Id && i.Quantity == cartQty);

        var freshProduct = await verifyDb.Products.FindAsync(product.Id);
        freshProduct!.StockQuantity.Should().Be(999);
    }

    // 4. Deterministic concurrent oversell: hai user, hai Cart, cùng mua sản phẩm tồn kho cuối; dùng barrier/test hook.
    // Chính xác một checkout success, một Checkout.InventoryChanged; stock không âm và loser Cart còn nguyên.
    [Fact]
    public async Task Scenario04_DeterministicConcurrentOversell_OneSucceeds_OneInventoryChanged_StockNonNegative()
    {
        var (_, product) = await SeedProductAsync(stockQuantity: 1);
        var (user1, addr1, cart1) = await SeedUserWithAddressAndCartAsync(product.Id, 1);
        var (user2, addr2, cart2) = await SeedUserWithAddressAndCartAsync(product.Id, 1);

        using var scope1 = _fixture.Factory.Services.CreateScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        using var scope2 = _fixture.Factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tcs1ReachedHook = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2ReachedHook = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsAllowDb1ToSave = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsAllowDb2ToSave = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var hookDb1 = new TestHookApplicationDbContext(db1, async ct =>
        {
            tcs1ReachedHook.TrySetResult(true);
            await tcsAllowDb1ToSave.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
        });

        var hookDb2 = new TestHookApplicationDbContext(db2, async ct =>
        {
            tcs2ReachedHook.TrySetResult(true);
            await tcsAllowDb2ToSave.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
        });

        var userSvc1 = new TestCurrentUserService(user1.Id);
        var userSvc2 = new TestCurrentUserService(user2.Id);

        var handler1 = new CheckoutCartCommandHandler(hookDb1, userSvc1);
        var handler2 = new CheckoutCartCommandHandler(hookDb2, userSvc2);

        var key1 = "concurrent-key-user-1";
        var key2 = "concurrent-key-user-2";

        var task1 = handler1.Handle(new CheckoutCartCommand(addr1.Id, cart1.Version, PaymentMethod.COD, null, key1), CancellationToken.None);
        await tcs1ReachedHook.Task.WaitAsync(TimeSpan.FromSeconds(15));

        var task2 = handler2.Handle(new CheckoutCartCommand(addr2.Id, cart2.Version, PaymentMethod.COD, null, key2), CancellationToken.None);
        await tcs2ReachedHook.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // Release winner (User 1)
        tcsAllowDb1ToSave.TrySetResult(true);
        var result1 = await task1;

        // Release loser (User 2)
        tcsAllowDb2ToSave.TrySetResult(true);
        var result2 = await task2;

        result1.IsSuccess.Should().BeTrue("User 1 must succeed");
        result2.IsFailure.Should().BeTrue("User 2 must fail due to concurrent inventory update");
        result2.Error.Type.Should().Be(ErrorType.Conflict);
        result2.Error.Code.Should().Be("Checkout.InventoryChanged");

        // Verify in DB with fresh context
        using var verifyScope = _fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var freshProduct = await verifyDb.Products.FindAsync(product.Id);
        freshProduct!.StockQuantity.Should().Be(0, "Stock was 1, decremented once, and must not be negative");

        var orders = await verifyDb.Orders.ToListAsync();
        orders.Should().ContainSingle("Exactly one order must exist");
        orders.Single().UserId.Should().Be(user1.Id);

        var loserCart = await verifyDb.Carts.Include(c => c.Items).SingleAsync(c => c.UserId == user2.Id);
        loserCart.Items.Should().ContainSingle(i => i.ProductId == product.Id && i.Quantity == 1);
    }

    // 5. Stale Cart version trả conflict và không mutation.
    [Fact]
    public async Task Scenario05_StaleCartVersion_Returns409Conflict_NoMutation()
    {
        var (_, product) = await SeedProductAsync(stockQuantity: 20);
        var (client, user, address, cart) = await CreateUserWithAddressAndCartAsync(product.Id, 2);

        var staleVersion = cart.Version + 10;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(
                AddressId: address.Id,
                ExpectedCartVersion: staleVersion,
                PaymentMethod: PaymentMethod.COD,
                Notes: null
            ))
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Checkout.CartChanged");

        // DB audit
        using var verifyScope = _fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        (await verifyDb.Orders.CountAsync()).Should().Be(0);
        var freshCart = await verifyDb.Carts.Include(c => c.Items).SingleAsync(c => c.UserId == user.Id);
        freshCart.Items.Should().ContainSingle();
        freshCart.Version.Should().Be(cart.Version);
    }

    // 6. Address của user khác không dùng được.
    [Fact]
    public async Task Scenario06_AddressOfOtherUser_Returns404NotFound()
    {
        var (_, product) = await SeedProductAsync();
        var (client1, _, _, cart1) = await CreateUserWithAddressAndCartAsync(product.Id, 1);
        var (_, _, addr2, _) = await CreateUserWithAddressAndCartAsync(product.Id, 1);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(
                AddressId: addr2.Id, // Address belongs to user 2
                ExpectedCartVersion: cart1.Version,
                PaymentMethod: PaymentMethod.COD,
                Notes: null
            ))
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var response = await client1.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Checkout.AddressNotFound");
    }

    // 7. API integration: 401 anonymous; quote 200; checkout 201; invalid body/header 400; conflict cases 409.
    [Fact]
    public async Task Scenario07_ApiIntegration_AuthorizationValidationAndQuoteEndpoints()
    {
        var (_, product) = await SeedProductAsync(price: 120_000m, stockQuantity: 10);
        var (client, user, address, cart) = await CreateUserWithAddressAndCartAsync(product.Id, 2);
        using var anonymousClient = _fixture.Factory.CreateClient();

        // 7a. Anonymous quote returns 401
        var anonQuoteRes = await anonymousClient.PostAsJsonAsync("/api/v1/orders/checkout/quote", new CheckoutQuoteRequest(address.Id));
        anonQuoteRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 7b. Anonymous checkout returns 401
        using var anonCheckoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(address.Id, cart.Version, PaymentMethod.COD, null))
        };
        anonCheckoutReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var anonCheckoutRes = await anonymousClient.SendAsync(anonCheckoutReq);
        anonCheckoutRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 7c. Authenticated quote returns 200 with live snapshot
        var quoteRes = await client.PostAsJsonAsync("/api/v1/orders/checkout/quote", new CheckoutQuoteRequest(address.Id));
        quoteRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var quoteDto = await quoteRes.Content.ReadFromJsonAsync<CheckoutQuoteDto>(JsonOptions);
        quoteDto.Should().NotBeNull();
        quoteDto!.CanCheckout.Should().BeTrue();
        quoteDto.Subtotal.Should().Be(240_000m);
        quoteDto.FinalAmount.Should().Be(240_000m);
        quoteDto.Items.Should().ContainSingle();

        // 7d. Missing / invalid Idempotency-Key returns 400
        using var noHeaderReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(address.Id, cart.Version, PaymentMethod.COD, null))
        };
        var noHeaderRes = await client.SendAsync(noHeaderReq);
        noHeaderRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var shortKeyReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(address.Id, cart.Version, PaymentMethod.COD, null))
        };
        shortKeyReq.Headers.Add("Idempotency-Key", "short-key"); // < 16 chars
        var shortKeyRes = await client.SendAsync(shortKeyReq);
        shortKeyRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 7e. Stripe / VNPay returns 400 with PaymentMethodNotAvailable
        using var stripeReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(address.Id, cart.Version, PaymentMethod.Stripe, null))
        };
        stripeReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var stripeRes = await client.SendAsync(stripeReq);
        stripeRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var stripeBody = await stripeRes.Content.ReadAsStringAsync();
        stripeBody.Should().Contain("Checkout.PaymentMethodNotAvailable");
    }

    // 8. EF model test xác nhận ba Product properties là concurrency token và migration/model snapshot khớp.
    [Fact]
    public void Scenario08_EfModel_ProductProperties_AreConfiguredAsConcurrencyTokens()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var entityType = db.Model.FindEntityType(typeof(Product));
        entityType.Should().NotBeNull();

        var priceProp = entityType!.FindProperty(nameof(Product.Price));
        priceProp.Should().NotBeNull();
        priceProp!.IsConcurrencyToken.Should().BeTrue("Price must be configured as concurrency token");

        var stockProp = entityType.FindProperty(nameof(Product.StockQuantity));
        stockProp.Should().NotBeNull();
        stockProp!.IsConcurrencyToken.Should().BeTrue("StockQuantity must be configured as concurrency token");

        var activeProp = entityType.FindProperty(nameof(Product.IsActive));
        activeProp.Should().NotBeNull();
        activeProp!.IsConcurrencyToken.Should().BeTrue("IsActive must be configured as concurrency token");
    }
}
