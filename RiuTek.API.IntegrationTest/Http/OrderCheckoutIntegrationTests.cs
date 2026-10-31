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
            await db.StockMovements.ExecuteDeleteAsync();
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

    private async Task<(HttpClient Client, User User, UserAddress Address, Cart Cart)> CreateUserWithAddressAndMultiProductCartAsync(
        params (Guid ProductId, int Quantity)[] items)
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
            IsActive = true
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
        foreach (var item in items)
        {
            cart.AddItem(item.ProductId, item.Quantity);
        }
        db.Carts.Add(cart);

        await db.SaveChangesAsync();

        var token = jwtGenerator.GenerateAccessToken(user);
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return (client, user, address, cart);
    }

    private async Task<(HttpClient Client, User User)> CreateStaffUserClientAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var jwtGenerator = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(
            email: $"staff_{suffix}@riutek.test",
            passwordHash: "dummy_hashed_password",
            fullName: $"Staff User {suffix}",
            role: UserRole.Staff
        ) { IsActive = true };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var token = jwtGenerator.GenerateAccessToken(user);
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, user);
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

    private async Task<(User User, UserAddress Address, Cart Cart)> SeedUserWithAddressAndMultiProductCartAsync(
        params (Guid ProductId, int Quantity)[] items)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(
            email: $"seed_multi_{suffix}@riutek.test",
            passwordHash: "dummy_hashed_password",
            fullName: $"Seed Multi User {suffix}",
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
        foreach (var item in items)
        {
            cart.AddItem(item.ProductId, item.Quantity);
        }
        db.Carts.Add(cart);

        await db.SaveChangesAsync();
        return (user, address, cart);
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
        dbOrder.Items.First().PCBuildId.Should().BeNull("Cart checkout must not set PCBuildId on OrderItems");

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

    // 9. Deterministic concurrent same-key race: hai request cùng một user/cart/idempotency-key
    // cả 2 bắt đầu trước khi save; sau khi release barrier, cả 2 đều success, cùng OrderId/OrderNumber;
    // stock chỉ trừ một lần, duy nhất một Order trong database.
    [Fact]
    public async Task Scenario09_DeterministicConcurrentSameKeyRace_BothSucceed_SameOrder_StockDecrementedOnce()
    {
        var initialStock = 10;
        var cartQty = 2;
        var (_, product) = await SeedProductAsync(stockQuantity: initialStock);
        var (user, address, cart) = await SeedUserWithAddressAndCartAsync(product.Id, cartQty);

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

        var userSvc1 = new TestCurrentUserService(user.Id);
        var userSvc2 = new TestCurrentUserService(user.Id);

        var handler1 = new CheckoutCartCommandHandler(hookDb1, userSvc1);
        var handler2 = new CheckoutCartCommandHandler(hookDb2, userSvc2);

        var sharedKey = "deterministic-same-key-" + Guid.NewGuid().ToString("N");

        var cmd1 = new CheckoutCartCommand(address.Id, cart.Version, PaymentMethod.COD, null, sharedKey);
        var cmd2 = new CheckoutCartCommand(address.Id, cart.Version, PaymentMethod.COD, null, sharedKey);

        var task1 = handler1.Handle(cmd1, CancellationToken.None);
        await tcs1ReachedHook.Task.WaitAsync(TimeSpan.FromSeconds(15));

        var task2 = handler2.Handle(cmd2, CancellationToken.None);
        await tcs2ReachedHook.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // Both handlers have deterministically read the initial state and are held before SaveChangesAsync
        tcsAllowDb1ToSave.TrySetResult(true);
        var result1 = await task1;

        tcsAllowDb2ToSave.TrySetResult(true);
        var result2 = await task2;

        // Both requests must succeed and return the exact same Order
        result1.IsSuccess.Should().BeTrue("Winner request must succeed");
        result2.IsSuccess.Should().BeTrue("Concurrent same-key request must recover and succeed");

        result1.Value.Id.Should().Be(result2.Value.Id);
        result1.Value.OrderNumber.Should().Be(result2.Value.OrderNumber);

        // Verify in DB with fresh context
        using var verifyScope = _fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var orders = await verifyDb.Orders.Include(o => o.Items).ToListAsync();
        orders.Should().ContainSingle("Exactly one order row must exist in PostgreSQL");
        orders.Single().Items.Should().ContainSingle();

        var freshProduct = await verifyDb.Products.FindAsync(product.Id);
        freshProduct!.StockQuantity.Should().Be(initialStock - cartQty, "Stock must be deducted exactly once");

        var freshCart = await verifyDb.Carts.Include(c => c.Items).SingleAsync(c => c.UserId == user.Id);
        freshCart.Items.Should().BeEmpty("Cart must be cleared");
    }

    // 10. API status regression: EmptyCart, ProductInactive, InvalidPrice, InsufficientStock trả 409 Conflict.
    [Fact]
    public async Task Scenario10_ApiStatusRegression_StateConflictsReturn409_InvalidRequestsReturn400()
    {
        var (_, activeProduct) = await SeedProductAsync(price: 100_000m, stockQuantity: 10, isActive: true);
        var (_, inactiveProduct) = await SeedProductAsync(price: 100_000m, stockQuantity: 10, isActive: false);
        var (_, zeroPriceProduct) = await SeedProductAsync(price: 0m, stockQuantity: 10, isActive: true);
        var (_, lowStockProduct) = await SeedProductAsync(price: 100_000m, stockQuantity: 1, isActive: true);

        // 10a. Empty Cart -> 409 Conflict with Checkout.EmptyCart
        var (clientEmpty, _, addrEmpty, cartEmpty) = await CreateUserWithAddressAndCartAsync(activeProduct.Id, quantity: 0);
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(addrEmpty.Id, cartEmpty.Version, PaymentMethod.COD, null))
        })
        {
            req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            var res = await clientEmpty.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var body = await res.Content.ReadAsStringAsync();
            body.Should().Contain("Checkout.EmptyCart");
        }

        // 10b. Inactive Product -> 409 Conflict with Checkout.ProductInactive
        var (clientInactive, _, addrInactive, cartInactive) = await CreateUserWithAddressAndCartAsync(inactiveProduct.Id, quantity: 1);
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(addrInactive.Id, cartInactive.Version, PaymentMethod.COD, null))
        })
        {
            req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            var res = await clientInactive.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var body = await res.Content.ReadAsStringAsync();
            body.Should().Contain("Checkout.ProductInactive");
        }

        // 10c. Invalid Price (price <= 0) -> 409 Conflict with Checkout.InvalidPrice
        var (clientZeroPrice, _, addrZeroPrice, cartZeroPrice) = await CreateUserWithAddressAndCartAsync(zeroPriceProduct.Id, quantity: 1);
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(addrZeroPrice.Id, cartZeroPrice.Version, PaymentMethod.COD, null))
        })
        {
            req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            var res = await clientZeroPrice.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var body = await res.Content.ReadAsStringAsync();
            body.Should().Contain("Checkout.InvalidPrice");
        }

        // 10d. Insufficient Stock -> 409 Conflict with Checkout.InsufficientStock
        var (clientLowStock, _, addrLowStock, cartLowStock) = await CreateUserWithAddressAndCartAsync(lowStockProduct.Id, quantity: 5);
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(addrLowStock.Id, cartLowStock.Version, PaymentMethod.COD, null))
        })
        {
            req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            var res = await clientLowStock.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var body = await res.Content.ReadAsStringAsync();
            body.Should().Contain("Checkout.InsufficientStock");
        }
    }

    // 11. Test nhánh exact constraint: unique violation đúng UX_Orders_UserId_CheckoutIdempotencyKey
    // trên PostgreSQL thực tế được catch và recover thành công Order hiện hữu.
    [Fact]
    public async Task Scenario11_PostgreSqlExactUniqueViolation_TriggersFilterAndRecoversOrder()
    {
        var (_, product) = await SeedProductAsync(stockQuantity: 20);
        var (user, address, cart) = await SeedUserWithAddressAndCartAsync(product.Id, 2);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var idempotencyKey = "exact-unique-pg-" + Guid.NewGuid().ToString("N");

        // Trước khi SaveChangesAsync, một scope khác chèn một Order với chính xác cùng (UserId, CheckoutIdempotencyKey)
        // mà KHÔNG chạm vào Product hay Cart. Việc này đảm bảo PostgreSQL ném unique violation trên constraint UX_Orders_UserId_CheckoutIdempotencyKey.
        var hookDb = new TestHookApplicationDbContext(db, async _ =>
        {
            using var bgScope = _fixture.Factory.Services.CreateScope();
            var bgDb = bgScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var bgOrder = new Order(
                orderNumber: $"ORD-BG-{Guid.NewGuid():N}"[..25],
                userId: user.Id,
                checkoutIdempotencyKey: idempotencyKey,
                customerName: "BG User",
                customerEmail: user.Email,
                customerPhone: "0901234567",
                shippingAddress: "123 BG Street",
                paymentMethod: PaymentMethod.COD
            );
            bgOrder.AddItem(product.Id, product.Name, product.Sku, product.Price, 2);
            bgDb.Orders.Add(bgOrder);
            await bgDb.SaveChangesAsync();
        });

        var userSvc = new TestCurrentUserService(user.Id);
        var handler = new CheckoutCartCommandHandler(hookDb, userSvc);

        var command = new CheckoutCartCommand(
            AddressId: address.Id,
            ExpectedCartVersion: cart.Version,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: idempotencyKey
        );

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue("Exact unique violation on UX_Orders_UserId_CheckoutIdempotencyKey must be caught and recovered");
        result.Value.Should().NotBeNull();
        result.Value.CustomerName.Should().Be("BG User");
    }

    // 12. Non-idempotency DbUpdateException trên PostgreSQL (ví dụ foreign key failure)
    // KHÔNG được bắt bởi filter và phải propagate ra ngoài.
    [Fact]
    public async Task Scenario12_PostgreSqlNonIdempotencyDbUpdateException_IsNotCaughtAndPropagates()
    {
        var (_, product) = await SeedProductAsync(stockQuantity: 20);
        var (user, address, cart) = await SeedUserWithAddressAndCartAsync(product.Id, 2);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Trước khi SaveChangesAsync, một scope khác xóa User khỏi PostgreSQL.
        // Khi handler thử INSERT Order, foreign key constraint FK_Orders_Users_UserId sẽ thất bại!
        var hookDb = new TestHookApplicationDbContext(db, async _ =>
        {
            using var bgScope = _fixture.Factory.Services.CreateScope();
            var bgDb = bgScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await bgDb.UserAddresses.Where(a => a.UserId == user.Id).ExecuteDeleteAsync();
            await bgDb.CartItems.Where(ci => ci.CartId == cart.Id).ExecuteDeleteAsync();
            await bgDb.Carts.Where(c => c.UserId == user.Id).ExecuteDeleteAsync();
            await bgDb.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync();
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

        var act = () => handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    // 13. Multi-product checkout: each product has exactly 1 negative Checkout movement with correct deltas,
    // before/after, ActorUserId = customer, OrderId = order.Id. Replay does NOT add duplicate movements.
    [Fact]
    public async Task Scenario13_MultiProductCheckoutAndReplay_PersistsStockMovementsAndIsIdempotent()
    {
        var (_, p1) = await SeedProductAsync(price: 100_000m, stockQuantity: 20);
        var (_, p2) = await SeedProductAsync(price: 200_000m, stockQuantity: 15);

        var (client, user, address, cart) = await CreateUserWithAddressAndMultiProductCartAsync(
            (p1.Id, 2),
            (p2.Id, 3));

        var idempotencyKey = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(
                AddressId: address.Id,
                ExpectedCartVersion: cart.Version,
                PaymentMethod: PaymentMethod.COD,
                Notes: "Multi-product checkout"
            ))
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderDto = (await response.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions))!;

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var p1Db = await db.Products.FindAsync(p1.Id);
            p1Db!.StockQuantity.Should().Be(18);

            var p2Db = await db.Products.FindAsync(p2.Id);
            p2Db!.StockQuantity.Should().Be(12);

            var movements = await db.StockMovements
                .Where(m => m.OrderId == orderDto.Id)
                .OrderBy(m => m.ProductId)
                .ToListAsync();

            movements.Should().HaveCount(2);

            var m1 = movements.Single(m => m.ProductId == p1.Id);
            m1.Reason.Should().Be(StockMovementReason.Checkout);
            m1.QuantityDelta.Should().Be(-2);
            m1.StockBefore.Should().Be(20);
            m1.StockAfter.Should().Be(18);
            m1.ActorUserId.Should().Be(user.Id);
            m1.OrderId.Should().Be(orderDto.Id);

            var m2 = movements.Single(m => m.ProductId == p2.Id);
            m2.Reason.Should().Be(StockMovementReason.Checkout);
            m2.QuantityDelta.Should().Be(-3);
            m2.StockBefore.Should().Be(15);
            m2.StockAfter.Should().Be(12);
            m2.ActorUserId.Should().Be(user.Id);
            m2.OrderId.Should().Be(orderDto.Id);
        }

        // Replay with exact same idempotency key
        using var replayReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(
                AddressId: address.Id,
                ExpectedCartVersion: cart.Version,
                PaymentMethod: PaymentMethod.COD,
                Notes: "Multi-product checkout replay"
            ))
        };
        replayReq.Headers.Add("Idempotency-Key", idempotencyKey);

        var replayResp = await client.SendAsync(replayReq);
        replayResp.StatusCode.Should().Be(HttpStatusCode.Created);

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var p1Db = await db.Products.FindAsync(p1.Id);
            p1Db!.StockQuantity.Should().Be(18);

            var p2Db = await db.Products.FindAsync(p2.Id);
            p2Db!.StockQuantity.Should().Be(12);

            var totalMovements = await db.StockMovements
                .Where(m => m.OrderId == orderDto.Id)
                .CountAsync();
            totalMovements.Should().Be(2, "Replay must NOT add duplicate stock movements");
        }
    }

    // 14. Customer cancellation: creates positive OrderCancellation movements for each product,
    // restores stock, sets ActorUserId = customer, OrderId = order.Id. Replay does NOT create duplicate movements.
    [Fact]
    public async Task Scenario14_CustomerCancellationAfterCod_PersistsPositiveMovementsAndIsIdempotent()
    {
        var (_, p1) = await SeedProductAsync(price: 100_000m, stockQuantity: 20);
        var (_, p2) = await SeedProductAsync(price: 200_000m, stockQuantity: 15);

        var (client, user, address, cart) = await CreateUserWithAddressAndMultiProductCartAsync(
            (p1.Id, 2),
            (p2.Id, 3));

        var idempotencyKey = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(
                AddressId: address.Id,
                ExpectedCartVersion: cart.Version,
                PaymentMethod: PaymentMethod.COD,
                Notes: null
            ))
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderDto = (await response.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions))!;

        // Customer cancels order
        var cancelResp = await client.PostAsync($"/api/v1/orders/{orderDto.Id}/cancel", null);
        cancelResp.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var p1Db = await db.Products.FindAsync(p1.Id);
            p1Db!.StockQuantity.Should().Be(20);

            var p2Db = await db.Products.FindAsync(p2.Id);
            p2Db!.StockQuantity.Should().Be(15);

            var cancelMovements = await db.StockMovements
                .Where(m => m.OrderId == orderDto.Id && m.Reason == StockMovementReason.OrderCancellation)
                .ToListAsync();

            cancelMovements.Should().HaveCount(2);

            var cm1 = cancelMovements.Single(m => m.ProductId == p1.Id);
            cm1.QuantityDelta.Should().Be(2);
            cm1.StockBefore.Should().Be(18);
            cm1.StockAfter.Should().Be(20);
            cm1.ActorUserId.Should().Be(user.Id);

            var cm2 = cancelMovements.Single(m => m.ProductId == p2.Id);
            cm2.QuantityDelta.Should().Be(3);
            cm2.StockBefore.Should().Be(12);
            cm2.StockAfter.Should().Be(15);
            cm2.ActorUserId.Should().Be(user.Id);
        }

        // Replay cancellation
        var replayCancelResp = await client.PostAsync($"/api/v1/orders/{orderDto.Id}/cancel", null);
        replayCancelResp.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var p1Db = await db.Products.FindAsync(p1.Id);
            p1Db!.StockQuantity.Should().Be(20);

            var p2Db = await db.Products.FindAsync(p2.Id);
            p2Db!.StockQuantity.Should().Be(15);

            var totalCancelMovements = await db.StockMovements
                .Where(m => m.OrderId == orderDto.Id && m.Reason == StockMovementReason.OrderCancellation)
                .CountAsync();
            totalCancelMovements.Should().Be(2, "Replay cancellation must NOT create duplicate movements");
        }
    }

    // 15. Admin cancellation: sets ActorUserId to Staff/Admin and creates positive OrderCancellation movement.
    [Fact]
    public async Task Scenario15_AdminCancellation_PersistsMovementsWithStaffAdminActor()
    {
        var (_, product) = await SeedProductAsync(price: 150_000m, stockQuantity: 10);
        var (client, _, address, cart) = await CreateUserWithAddressAndCartAsync(product.Id, 4);

        using var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(
                AddressId: address.Id,
                ExpectedCartVersion: cart.Version,
                PaymentMethod: PaymentMethod.COD,
                Notes: null
            ))
        };
        checkoutReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var checkoutResp = await client.SendAsync(checkoutReq);
        checkoutResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderDto = (await checkoutResp.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions))!;

        // Staff updates status to Cancelled
        var (staffClient, staffUser) = await CreateStaffUserClientAsync();
        var patchReq = new UpdateOrderStatusRequest(OrderStatus.Confirmed, OrderStatus.Cancelled);
        var patchResp = await staffClient.PatchAsJsonAsync($"/api/v1/orders/admin/{orderDto.Id}/status", patchReq);
        patchResp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var pDb = await db.Products.FindAsync(product.Id);
        pDb!.StockQuantity.Should().Be(10);

        var cancelMovements = await db.StockMovements
            .Where(m => m.OrderId == orderDto.Id && m.Reason == StockMovementReason.OrderCancellation)
            .ToListAsync();

        cancelMovements.Should().ContainSingle();
        var cm = cancelMovements.Single();
        cm.ProductId.Should().Be(product.Id);
        cm.QuantityDelta.Should().Be(4);
        cm.StockBefore.Should().Be(6);
        cm.StockAfter.Should().Be(10);
        cm.ActorUserId.Should().Be(staffUser.Id);
        cm.OrderId.Should().Be(orderDto.Id);
    }

    // 16. Failed checkout before SaveChanges (e.g. stock depleted) leaves 0 persisted movements.
    [Fact]
    public async Task Scenario16_FailedCheckoutBeforeSaveChanges_LeavesNoMovementsPersisted()
    {
        var (_, product) = await SeedProductAsync(price: 150_000m, stockQuantity: 2);
        var (client, _, address, cart) = await CreateUserWithAddressAndCartAsync(product.Id, 2);

        // Another actor drops the stock to 0 right before checkout
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var p = await db.Products.FindAsync(product.Id);
            p!.StockQuantity = 0;
            await db.SaveChangesAsync();
        }

        using var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(
                AddressId: address.Id,
                ExpectedCartVersion: cart.Version,
                PaymentMethod: PaymentMethod.COD,
                Notes: null
            ))
        };
        checkoutReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var checkoutResp = await client.SendAsync(checkoutReq);
        checkoutResp.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var verifyScope = _fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var movements = await verifyDb.StockMovements
            .Where(m => m.ProductId == product.Id && m.Reason == StockMovementReason.Checkout)
            .ToListAsync();
        movements.Should().BeEmpty("Failed checkout must leave 0 checkout stock movements");
    }

    // 17. Selected items checkout (A and C selected from A, B, C):
    // Order contains only A and C, cart retains B, version increments, A and C removed from DB.
    [Fact]
    public async Task Scenario17_SelectedItemsCheckout_OnlySelectedItemsOrdered_UnselectedRemainInCart()
    {
        var (_, productA) = await SeedProductAsync(price: 100_000m, stockQuantity: 10);
        var (_, productB) = await SeedProductAsync(price: 200_000m, stockQuantity: 10);
        var (_, productC) = await SeedProductAsync(price: 300_000m, stockQuantity: 10);

        var (client, user, address, cart) = await CreateUserWithAddressAndCartAsync(productA.Id, quantity: 2);

        // Add B and C to cart
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cartDb = await db.Carts.Include(c => c.Items).FirstAsync(c => c.Id == cart.Id);
            cartDb.AddItem(productB.Id, 3);
            cartDb.AddItem(productC.Id, 1);
            await db.SaveChangesAsync();
            cart = cartDb;
        }

        // 1. Quote endpoint with selectedProductIds: [A, C]
        var quoteReq = new CheckoutQuoteRequest(
            AddressId: address.Id,
            SelectedProductIds: [productA.Id, productC.Id]
        );
        var quoteResp = await client.PostAsJsonAsync("/api/v1/orders/checkout/quote", quoteReq);
        quoteResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var quoteDto = (await quoteResp.Content.ReadFromJsonAsync<CheckoutQuoteDto>(JsonOptions))!;
        quoteDto.Items.Should().HaveCount(2);
        quoteDto.Items.Select(i => i.ProductId).Should().BeEquivalentTo([productA.Id, productC.Id]);
        quoteDto.Subtotal.Should().Be(500_000m); // 2*100k + 1*300k
        quoteDto.CanCheckout.Should().BeTrue();

        // 2. Checkout endpoint with selectedProductIds: [A, C]
        using var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(
                AddressId: address.Id,
                ExpectedCartVersion: cart.Version,
                PaymentMethod: PaymentMethod.COD,
                Notes: "Partial checkout",
                SelectedProductIds: [productA.Id, productC.Id]
            ))
        };
        var idempotencyKey = Guid.NewGuid().ToString("N");
        checkoutReq.Headers.Add("Idempotency-Key", idempotencyKey);

        var checkoutResp = await client.SendAsync(checkoutReq);
        checkoutResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderDto = (await checkoutResp.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions))!;
        orderDto.Items.Should().HaveCount(2);
        orderDto.Items.Select(i => i.ProductId).Should().BeEquivalentTo([productA.Id, productC.Id]);

        // 3. Verify Database
        using var verifyScope = _fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Product stock
        var pADb = await verifyDb.Products.FindAsync(productA.Id);
        pADb!.StockQuantity.Should().Be(8); // 10 - 2
        var pBDb = await verifyDb.Products.FindAsync(productB.Id);
        pBDb!.StockQuantity.Should().Be(10); // Untouched
        var pCDb = await verifyDb.Products.FindAsync(productC.Id);
        pCDb!.StockQuantity.Should().Be(9); // 10 - 1

        // Cart items: B is retained, A and C deleted
        var cartInDb = await verifyDb.Carts.Include(c => c.Items).FirstAsync(c => c.Id == cart.Id);
        cartInDb.Items.Should().HaveCount(1);
        cartInDb.Items.Single().ProductId.Should().Be(productB.Id);
        cartInDb.Items.Single().Quantity.Should().Be(3);
        cartInDb.Version.Should().BeGreaterThan(cart.Version);
    }

    // 18. Empty array [] in selectedProductIds returns 400 Bad Request.
    [Fact]
    public async Task Scenario18_EmptySelectedProductIds_Returns400BadRequest()
    {
        var (_, product) = await SeedProductAsync();
        var (client, _, address, cart) = await CreateUserWithAddressAndCartAsync(product.Id, 2);

        // Quote
        var quoteResp = await client.PostAsJsonAsync("/api/v1/orders/checkout/quote", new CheckoutQuoteRequest(
            AddressId: address.Id,
            SelectedProductIds: []
        ));
        quoteResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Checkout
        using var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(
                AddressId: address.Id,
                ExpectedCartVersion: cart.Version,
                PaymentMethod: PaymentMethod.COD,
                Notes: null,
                SelectedProductIds: []
            ))
        };
        checkoutReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var checkoutResp = await client.SendAsync(checkoutReq);
        checkoutResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // 19. Item in selectedProductIds not present in cart returns 409 Conflict (Checkout.SelectionChanged).
    [Fact]
    public async Task Scenario19_SelectedProductIdNotInCart_Returns409SelectionChanged()
    {
        var (_, product) = await SeedProductAsync();
        var (client, _, address, cart) = await CreateUserWithAddressAndCartAsync(product.Id, 2);
        var missingProductId = Guid.NewGuid();

        // Quote
        var quoteResp = await client.PostAsJsonAsync("/api/v1/orders/checkout/quote", new CheckoutQuoteRequest(
            AddressId: address.Id,
            SelectedProductIds: [product.Id, missingProductId]
        ));
        quoteResp.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Checkout
        using var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(
                AddressId: address.Id,
                ExpectedCartVersion: cart.Version,
                PaymentMethod: PaymentMethod.COD,
                Notes: null,
                SelectedProductIds: [product.Id, missingProductId]
            ))
        };
        checkoutReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var checkoutResp = await client.SendAsync(checkoutReq);
        checkoutResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // 20. (C1) Deterministic concurrent partial checkouts on same cart:
    // Worker 1 selects [A], Worker 2 selects [C]. Both start at cart version V.
    // Barrier coordinates both workers reaching pre-save hook before any commit.
    // Worker 1 commits successfully. Worker 2 conflicts with Checkout.InventoryChanged.
    // Transaction of Worker 2 rolls back cleanly. Fresh DB assertions verify exactly 1 order (A),
    // stock decremented only for A, cart retains B and C, CartItem A is deleted from DB.
    // Replay with Worker 1 key succeeds without duplicate mutation.
    [Fact]
    public async Task Scenario20_DeterministicConcurrentPartialCheckouts_SameCartVersion_OneSucceeds_OneInventoryChanged_RollsBack()
    {
        var (_, productA) = await SeedProductAsync(price: 100_000m, stockQuantity: 10);
        var (_, productB) = await SeedProductAsync(price: 200_000m, stockQuantity: 10);
        var (_, productC) = await SeedProductAsync(price: 300_000m, stockQuantity: 10);

        var (user, address, cart) = await SeedUserWithAddressAndMultiProductCartAsync(
            (productA.Id, 2),
            (productB.Id, 3),
            (productC.Id, 1)
        );

        var initialCartVersion = cart.Version;

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

        var userSvc = new TestCurrentUserService(user.Id);
        var handler1 = new CheckoutCartCommandHandler(hookDb1, userSvc);
        var handler2 = new CheckoutCartCommandHandler(hookDb2, userSvc);

        var key1 = "concurrent-c1-k1-" + Guid.NewGuid().ToString("N");
        var key2 = "concurrent-c1-k2-" + Guid.NewGuid().ToString("N");

        try
        {
            var task1 = handler1.Handle(new CheckoutCartCommand(
                address.Id,
                initialCartVersion,
                PaymentMethod.COD,
                null,
                key1,
                [productA.Id]
            ), CancellationToken.None);
            await tcs1ReachedHook.Task.WaitAsync(TimeSpan.FromSeconds(15));

            var task2 = handler2.Handle(new CheckoutCartCommand(
                address.Id,
                initialCartVersion,
                PaymentMethod.COD,
                null,
                key2,
                [productC.Id]
            ), CancellationToken.None);
            await tcs2ReachedHook.Task.WaitAsync(TimeSpan.FromSeconds(15));

            // Release worker 1 (winner) and wait for commit
            tcsAllowDb1ToSave.TrySetResult(true);
            var result1 = await task1;

            // Release worker 2 (loser) and wait for conflict
            tcsAllowDb2ToSave.TrySetResult(true);
            var result2 = await task2;

            result1.IsSuccess.Should().BeTrue("Worker 1 must succeed");
            result2.IsFailure.Should().BeTrue("Worker 2 must fail due to concurrent cart version change");
            result2.Error.Type.Should().Be(ErrorType.Conflict);
            result2.Error.Code.Should().Be("Checkout.InventoryChanged");

            // Fresh context for DB assertions
            using var verifyScope = _fixture.Factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // 1. Exactly 1 order in DB (Worker 1's order)
            var orders = await verifyDb.Orders.AsNoTracking().Include(o => o.Items).ToListAsync();
            orders.Should().ContainSingle("Only worker 1's order should have been persisted");
            var savedOrder = orders.Single();
            savedOrder.CheckoutIdempotencyKey.Should().Be(key1);
            savedOrder.Items.Should().ContainSingle();
            savedOrder.Items.Single().ProductId.Should().Be(productA.Id);
            savedOrder.Items.Single().Quantity.Should().Be(2);

            // 2. Stock assertions: A decremented by 2 (10 -> 8), B (10) and C (10) untouched
            var freshProductA = await verifyDb.Products.AsNoTracking().FirstAsync(p => p.Id == productA.Id);
            freshProductA.StockQuantity.Should().Be(8);
            var freshProductB = await verifyDb.Products.AsNoTracking().FirstAsync(p => p.Id == productB.Id);
            freshProductB.StockQuantity.Should().Be(10);
            var freshProductC = await verifyDb.Products.AsNoTracking().FirstAsync(p => p.Id == productC.Id);
            freshProductC.StockQuantity.Should().Be(10);

            // 3. Stock movements: only 1 entry for product A (-2)
            var movements = await verifyDb.StockMovements.AsNoTracking().ToListAsync();
            movements.Should().ContainSingle(m => m.ProductId == productA.Id && m.QuantityDelta == -2);

            // 4. Cart verification: B (qty 3) and C (qty 1) remain; CartItem A is permanently deleted from DB
            var freshCart = await verifyDb.Carts.AsNoTracking().Include(c => c.Items).FirstAsync(c => c.Id == cart.Id);
            freshCart.Items.Should().HaveCount(2);
            freshCart.Items.Should().Contain(i => i.ProductId == productB.Id && i.Quantity == 3);
            freshCart.Items.Should().Contain(i => i.ProductId == productC.Id && i.Quantity == 1);
            freshCart.Items.Should().NotContain(i => i.ProductId == productA.Id);
            freshCart.Version.Should().BeGreaterThan(initialCartVersion);

            // 5. Zero residual data for K2
            var k2Order = await verifyDb.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.CheckoutIdempotencyKey == key2);
            k2Order.Should().BeNull();
            var attempts = await verifyDb.PaymentAttempts.AsNoTracking().ToListAsync();
            attempts.Should().BeEmpty();

            // 6. Idempotent replay of K1 succeeds even after A was removed from cart
            var replayHandler = new CheckoutCartCommandHandler(verifyDb, userSvc);
            var replayResult = await replayHandler.Handle(new CheckoutCartCommand(
                address.Id,
                9999, // Stale version in replay
                PaymentMethod.COD,
                null,
                key1,
                [productA.Id]
            ), CancellationToken.None);

            replayResult.IsSuccess.Should().BeTrue();
            replayResult.Value.Id.Should().Be(savedOrder.Id);

            // Verify stock and cart were not mutated a second time
            var pAAfterReplay = await verifyDb.Products.AsNoTracking().FirstAsync(p => p.Id == productA.Id);
            pAAfterReplay.StockQuantity.Should().Be(8);
            var freshCartAfterReplay = await verifyDb.Carts.AsNoTracking().Include(c => c.Items).FirstAsync(c => c.Id == cart.Id);
            freshCartAfterReplay.Items.Should().HaveCount(2);
        }
        finally
        {
            tcsAllowDb1ToSave.TrySetResult(true);
            tcsAllowDb2ToSave.TrySetResult(true);
        }
    }

    // 21. (C2) Deterministic concurrent unselected cart mutation with partial checkout:
    // Seed cart A qty 2, B qty 3, C qty 1. Checkout selects [A, C].
    // Checkout stops at pre-save hook. Separate context mutates unselected B qty 3 -> 4, commits to DB.
    // Release checkout SaveChangesAsync: PostgreSQL detects concurrency token conflict on Cart.
    // Transaction rolls back completely: 0 orders, stock untouched, cart preserves mutation of B,
    // and items A and C are NOT deleted.
    [Fact]
    public async Task Scenario21_DeterministicConcurrentUnselectedCartMutation_RollsBackPartialCheckoutTransaction()
    {
        var (_, productA) = await SeedProductAsync(price: 100_000m, stockQuantity: 10);
        var (_, productB) = await SeedProductAsync(price: 200_000m, stockQuantity: 10);
        var (_, productC) = await SeedProductAsync(price: 300_000m, stockQuantity: 10);

        var (user, address, cart) = await SeedUserWithAddressAndMultiProductCartAsync(
            (productA.Id, 2),
            (productB.Id, 3),
            (productC.Id, 1)
        );

        var initialCartVersion = cart.Version;

        using var checkoutScope = _fixture.Factory.Services.CreateScope();
        var checkoutDb = checkoutScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tcsReachedHook = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsAllowCheckoutToSave = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var hookDb = new TestHookApplicationDbContext(checkoutDb, async ct =>
        {
            tcsReachedHook.TrySetResult(true);
            await tcsAllowCheckoutToSave.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
        });

        var userSvc = new TestCurrentUserService(user.Id);
        var handler = new CheckoutCartCommandHandler(hookDb, userSvc);
        var key = "concurrent-c2-key-" + Guid.NewGuid().ToString("N");

        try
        {
            var checkoutTask = handler.Handle(new CheckoutCartCommand(
                address.Id,
                initialCartVersion,
                PaymentMethod.COD,
                null,
                key,
                [productA.Id, productC.Id]
            ), CancellationToken.None);

            await tcsReachedHook.Task.WaitAsync(TimeSpan.FromSeconds(15));

            // While checkout is paused right before SaveChangesAsync, another transaction
            // updates unselected line B: qty 3 -> 4 via domain aggregate and commits to PostgreSQL.
            using (var bgScope = _fixture.Factory.Services.CreateScope())
            {
                var bgDb = bgScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var bgCart = await bgDb.Carts.Include(c => c.Items).FirstAsync(c => c.Id == cart.Id);
                bgCart.AddItem(productB.Id, 1);
                await bgDb.SaveChangesAsync();
            }

            // Release checkout SaveChangesAsync -> PostgreSQL detects concurrent update on Cart
            tcsAllowCheckoutToSave.TrySetResult(true);
            var checkoutResult = await checkoutTask;

            checkoutResult.IsFailure.Should().BeTrue("Checkout transaction must fail when cart was concurrently updated");
            checkoutResult.Error.Type.Should().Be(ErrorType.Conflict);
            checkoutResult.Error.Code.Should().Be("Checkout.InventoryChanged");

            // Fresh context for DB assertions
            using var verifyScope = _fixture.Factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Zero orders created
            (await verifyDb.Orders.AsNoTracking().CountAsync()).Should().Be(0);
            (await verifyDb.OrderItems.AsNoTracking().CountAsync()).Should().Be(0);
            (await verifyDb.StockMovements.AsNoTracking().CountAsync()).Should().Be(0);
            (await verifyDb.PaymentAttempts.AsNoTracking().CountAsync()).Should().Be(0);

            // Stock of A, B, C untouched
            var freshProductA = await verifyDb.Products.AsNoTracking().FirstAsync(p => p.Id == productA.Id);
            freshProductA.StockQuantity.Should().Be(10);
            var freshProductB = await verifyDb.Products.AsNoTracking().FirstAsync(p => p.Id == productB.Id);
            freshProductB.StockQuantity.Should().Be(10);
            var freshProductC = await verifyDb.Products.AsNoTracking().FirstAsync(p => p.Id == productC.Id);
            freshProductC.StockQuantity.Should().Be(10);

            // Cart in DB: mutation of B preserved (qty 4), A (qty 2) and C (qty 1) NOT deleted!
            var freshCart = await verifyDb.Carts.AsNoTracking().Include(c => c.Items).FirstAsync(c => c.Id == cart.Id);
            freshCart.Items.Should().HaveCount(3);
            freshCart.Items.Single(i => i.ProductId == productA.Id).Quantity.Should().Be(2);
            freshCart.Items.Single(i => i.ProductId == productB.Id).Quantity.Should().Be(4);
            freshCart.Items.Single(i => i.ProductId == productC.Id).Quantity.Should().Be(1);
            freshCart.Version.Should().BeGreaterThan(initialCartVersion);
        }
        finally
        {
            tcsAllowCheckoutToSave.TrySetResult(true);
        }
    }

    // 22. Unselected inactive or out-of-stock product does not block selected checkout on PostgreSQL.
    [Fact]
    public async Task Scenario22_UnselectedInactiveOrOutOfStockProduct_DoesNotBlockSelectedCheckout()
    {
        var (_, productA) = await SeedProductAsync(price: 150_000m, stockQuantity: 10, isActive: true);
        var (_, productB) = await SeedProductAsync(price: 250_000m, stockQuantity: 0, isActive: false); // Inactive + out of stock!

        var (client, user, address, cart) = await CreateUserWithAddressAndMultiProductCartAsync(
            (productA.Id, 2),
            (productB.Id, 3)
        );

        // 1. Quote only for product A
        var quoteReq = new CheckoutQuoteRequest(
            AddressId: address.Id,
            SelectedProductIds: [productA.Id]
        );
        var quoteResp = await client.PostAsJsonAsync("/api/v1/orders/checkout/quote", quoteReq);
        quoteResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var quoteDto = (await quoteResp.Content.ReadFromJsonAsync<CheckoutQuoteDto>(JsonOptions))!;
        quoteDto.CanCheckout.Should().BeTrue();
        quoteDto.Items.Should().ContainSingle(i => i.ProductId == productA.Id);
        quoteDto.Issues.Should().BeEmpty();

        // 2. Checkout only for product A
        using var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(
                AddressId: address.Id,
                ExpectedCartVersion: cart.Version,
                PaymentMethod: PaymentMethod.COD,
                Notes: "Only buy active product A",
                SelectedProductIds: [productA.Id]
            ))
        };
        checkoutReq.Headers.Add("Idempotency-Key", "idemp-unselected-inactive-" + Guid.NewGuid().ToString("N"));
        var checkoutResp = await client.SendAsync(checkoutReq);
        checkoutResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var orderDto = (await checkoutResp.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions))!;
        orderDto.Items.Should().ContainSingle(i => i.ProductId == productA.Id && i.Quantity == 2);

        // 3. Verify in DB
        using var verifyScope = _fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var pA = await verifyDb.Products.FindAsync(productA.Id);
        pA!.StockQuantity.Should().Be(8);
        var pB = await verifyDb.Products.FindAsync(productB.Id);
        pB!.StockQuantity.Should().Be(0);

        var freshCart = await verifyDb.Carts.Include(c => c.Items).FirstAsync(c => c.Id == cart.Id);
        freshCart.Items.Should().ContainSingle(i => i.ProductId == productB.Id && i.Quantity == 3);
    }

    // 23. Selected Stripe checkout retains unselected items, idempotent replay retains unselected items,
    // and cancellation restores selected stock once on PostgreSQL.
    [Fact]
    public async Task Scenario23_SelectedStripeCheckout_RetainsUnselectedItems_CancelRestoresStockOnce()
    {
        string? expiredReference = null;
        var (client, fakeGateway) = CreateClientWithFakeGateway(gw =>
        {
            gw.OnExpireSession = pref =>
            {
                expiredReference = pref;
                return Result.Success();
            };
        });

        var (_, productA) = await SeedProductAsync(price: 100_000m, stockQuantity: 10);
        var (_, productB) = await SeedProductAsync(price: 200_000m, stockQuantity: 10);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (user, address, cart) = await SeedUserWithAddressAndMultiProductCartAsync(
            (productA.Id, 2),
            (productB.Id, 3)
        );

        using (var authScope = _fixture.Factory.Services.CreateScope())
        {
            var jwtGenerator = authScope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();
            var token = jwtGenerator.GenerateAccessToken(user);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var key = "idemp-stripe-selected-" + Guid.NewGuid().ToString("N");
        using var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(
                AddressId: address.Id,
                ExpectedCartVersion: cart.Version,
                PaymentMethod: PaymentMethod.Stripe,
                Notes: "Stripe partial checkout",
                SelectedProductIds: [productA.Id]
            ))
        };
        checkoutReq.Headers.Add("Idempotency-Key", key);

        var checkoutResp = await client.SendAsync(checkoutReq);
        checkoutResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderDto = (await checkoutResp.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions))!;
        orderDto.Status.Should().Be(OrderStatus.PendingPayment);
        orderDto.PaymentAction.Should().NotBeNull();
        orderDto.PaymentAction!.Type.Should().Be("Redirect");

        // DB check right after checkout: A stock is 8, cart only has B (qty 3)
        using (var verifyScope1 = _fixture.Factory.Services.CreateScope())
        {
            var db1 = verifyScope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var pA = await db1.Products.FindAsync(productA.Id);
            pA!.StockQuantity.Should().Be(8);
            var cart1 = await db1.Carts.Include(c => c.Items).FirstAsync(c => c.Id == cart.Id);
            cart1.Items.Should().ContainSingle(i => i.ProductId == productB.Id && i.Quantity == 3);
        }

        // Idempotent retry with same key returns same order and cart still has B
        using var retryReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(
                AddressId: address.Id,
                ExpectedCartVersion: 9999, // Stale version in retry
                PaymentMethod: PaymentMethod.Stripe,
                Notes: null,
                SelectedProductIds: [productA.Id]
            ))
        };
        retryReq.Headers.Add("Idempotency-Key", key);
        var retryResp = await client.SendAsync(retryReq);
        retryResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var retryDto = (await retryResp.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions))!;
        retryDto.Id.Should().Be(orderDto.Id);

        // Cancel order via POST /api/v1/orders/{orderId}/cancel
        var cancelResp = await client.PostAsync($"/api/v1/orders/{orderDto.Id}/cancel", null);
        cancelResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var detailDto = (await cancelResp.Content.ReadFromJsonAsync<OrderDetailDto>(JsonOptions))!;
        detailDto.Status.Should().Be(OrderStatus.Cancelled);

        // DB check after cancel: Stock A is restored to 10 (+2), Stock B remains 10
        // Cart STILL has only B (qty 3); A is not re-added to cart!
        using (var verifyScope2 = _fixture.Factory.Services.CreateScope())
        {
            var db2 = verifyScope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var pA = await db2.Products.FindAsync(productA.Id);
            pA!.StockQuantity.Should().Be(10, "Stock of selected item must be restored on cancellation");
            var pB = await db2.Products.FindAsync(productB.Id);
            pB!.StockQuantity.Should().Be(10);

            var cart2 = await db2.Carts.Include(c => c.Items).FirstAsync(c => c.Id == cart.Id);
            cart2.Items.Should().ContainSingle(i => i.ProductId == productB.Id && i.Quantity == 3,
                "Cancelled items must NOT be re-added to cart");
        }

        // Second cancel is idempotent: returns 200 OK without double-restoring stock
        var secondCancelResp = await client.PostAsync($"/api/v1/orders/{orderDto.Id}/cancel", null);
        secondCancelResp.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var verifyScope3 = _fixture.Factory.Services.CreateScope())
        {
            var db3 = verifyScope3.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var pA = await db3.Products.FindAsync(productA.Id);
            pA!.StockQuantity.Should().Be(10, "Stock must NOT be restored a second time");
        }
    }
}
