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
public class PCBuildBuyNowIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public PCBuildBuyNowIntegrationTests(PostgreSqlContainerFixture fixture)
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
            await db.PCBuildItems.ExecuteDeleteAsync();
            await db.PCBuilds.ExecuteDeleteAsync();
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

    private async Task<(HttpClient Client, User User, UserAddress Address, PCBuild PCBuild, Product ProductCpu, Product ProductRam, Cart? Cart)> SeedCustomerWithBuildAndCartAsync(
        int cpuStock = 20,
        int ramStock = 20,
        decimal cpuCurrentPrice = 500_000m,
        decimal ramCurrentPrice = 300_000m,
        decimal cpuSnapshotPrice = 400_000m,
        decimal ramSnapshotPrice = 250_000m,
        bool isBuildCompatible = true,
        bool hasCart = true,
        bool isUserActive = true)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var jwtGenerator = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(
            email: $"buynow_build_{suffix}@riutek.test",
            passwordHash: "dummy_hashed_password",
            fullName: $"Build User {suffix}",
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
            addressLine: "123 Build Ave",
            ward: "Ben Nghe",
            district: "Quan 1",
            city: "TP Ho Chi Minh",
            isDefault: true
        );
        db.UserAddresses.Add(address);

        var catCpu = new Category($"Cat CPU {suffix}", $"cpu-{suffix}", ComponentType.Cpu);
        var catRam = new Category($"Cat RAM {suffix}", $"ram-{suffix}", ComponentType.Ram);
        db.Categories.AddRange(catCpu, catRam);

        var productCpu = new Product(
            categoryId: catCpu.Id,
            name: $"CPU Intel {suffix}",
            slug: $"cpu-intel-{suffix}",
            sku: $"SKU-CPU-{suffix}",
            brand: "Intel",
            price: cpuCurrentPrice,
            stockQuantity: cpuStock,
            imageUrl: "https://riutek.test/cpu.png",
            componentType: ComponentType.Cpu,
            specifications: new CpuSpecification { Socket = CpuSocket.LGA1700 }
        );

        var productRam = new Product(
            categoryId: catRam.Id,
            name: $"RAM Corsair {suffix}",
            slug: $"ram-corsair-{suffix}",
            sku: $"SKU-RAM-{suffix}",
            brand: "Corsair",
            price: ramCurrentPrice,
            stockQuantity: ramStock,
            imageUrl: "https://riutek.test/ram.png",
            componentType: ComponentType.Ram,
            specifications: new RamSpecification { MemoryType = RamType.DDR5, CapacityGb = 32 }
        );
        db.Products.AddRange(productCpu, productRam);

        var pcBuild = new PCBuild($"Gaming Rig {suffix}", user.Id, "Top specs rig");
        if (!isBuildCompatible)
        {
            pcBuild.ApplyCompatibilitySnapshot(false, 750, ["Incompatible CPU/RAM"]);
        }

        var itemCpu = new PCBuildItem(pcBuild.Id, productCpu.Id, ComponentType.Cpu, cpuSnapshotPrice, quantity: 1);
        var itemRam = new PCBuildItem(pcBuild.Id, productRam.Id, ComponentType.Ram, ramSnapshotPrice, quantity: 2);
        pcBuild.AddItem(itemCpu);
        pcBuild.AddItem(itemRam);
        db.PCBuilds.Add(pcBuild);

        Cart? cart = null;
        if (hasCart)
        {
            var catAcc = new Category($"Cat Acc {suffix}", $"acc-{suffix}", ComponentType.Accessory);
            db.Categories.Add(catAcc);
            var cartProduct = new Product(
                categoryId: catAcc.Id,
                name: $"Mousepad {suffix}",
                slug: $"mousepad-{suffix}",
                sku: $"SKU-PAD-{suffix}",
                brand: "SteelSeries",
                price: 50_000m,
                stockQuantity: 100,
                imageUrl: "https://riutek.test/pad.png",
                componentType: ComponentType.Accessory,
                specifications: new AccessorySpecification { Details = "QcK Large" }
            );
            db.Products.Add(cartProduct);

            cart = new Cart(user.Id);
            cart.AddItem(cartProduct.Id, 3);
            db.Carts.Add(cart);
        }

        await db.SaveChangesAsync();

        var token = jwtGenerator.GenerateAccessToken(user);
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return (client, user, address, pcBuild, productCpu, productRam, cart);
    }

    // 1. COD success: 201, Order persisted, all OrderItems have PCBuildId, current Product price used, stock decremented, StockMovement recorded, PCBuild intact, Cart untouched.
    [Fact]
    public async Task BuyNowPCBuild_Cod_PersistsOrder_UsesCurrentPrice_SetsPCBuildId_LeavesCartAndBuildUntouched()
    {
        var (client, user, address, pcBuild, productCpu, productRam, initialCart) = await SeedCustomerWithBuildAndCartAsync(
            cpuStock: 10,
            ramStock: 10,
            cpuCurrentPrice: 600_000m,
            ramCurrentPrice: 350_000m,
            cpuSnapshotPrice: 500_000m,
            ramSnapshotPrice: 200_000m
        );

        initialCart.Should().NotBeNull();
        var initialCartVersion = initialCart!.Version;

        var idempotencyKey = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/buy-now/pc-build/{pcBuild.Id}")
        {
            Content = JsonContent.Create(new BuyNowPCBuildRequest(
                AddressId: address.Id,
                PaymentMethod: PaymentMethod.COD,
                Notes: "Build COD delivery"
            )),
            Headers = { { "Idempotency-Key", idempotencyKey } }
        };

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var orderDto = await response.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions);
        orderDto.Should().NotBeNull();
        orderDto!.Status.Should().Be(OrderStatus.Confirmed);
        orderDto.PaymentMethod.Should().Be(PaymentMethod.COD);
        orderDto.PaymentStatus.Should().Be(PaymentStatus.Pending);
        orderDto.PaymentAction.Should().BeNull();

        // Total should be: 1 * 600_000 + 2 * 350_000 = 1_300_000m (NOT snapshot 500k + 400k = 900k)
        orderDto.TotalAmount.Should().Be(1_300_000m);
        orderDto.FinalAmount.Should().Be(1_300_000m);
        orderDto.Items.Should().HaveCount(2);

        // Verify DB persistence
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var dbOrder = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderDto.Id);
        dbOrder.Should().NotBeNull();
        dbOrder!.UserId.Should().Be(user.Id);
        dbOrder.Items.Should().HaveCount(2);
        dbOrder.Items.Should().AllSatisfy(i => i.PCBuildId.Should().Be(pcBuild.Id));

        // Verify stock decremented
        var dbCpu = await db.Products.FindAsync(productCpu.Id);
        var dbRam = await db.Products.FindAsync(productRam.Id);
        dbCpu!.StockQuantity.Should().Be(9); // 10 - 1
        dbRam!.StockQuantity.Should().Be(8); // 10 - 2

        // Verify StockMovements
        var cpuMovement = await db.StockMovements.FirstOrDefaultAsync(m => m.OrderId == orderDto.Id && m.ProductId == productCpu.Id);
        cpuMovement.Should().NotBeNull();
        cpuMovement!.QuantityDelta.Should().Be(-1);
        cpuMovement.Reason.Should().Be(StockMovementReason.Checkout);

        var ramMovement = await db.StockMovements.FirstOrDefaultAsync(m => m.OrderId == orderDto.Id && m.ProductId == productRam.Id);
        ramMovement.Should().NotBeNull();
        ramMovement!.QuantityDelta.Should().Be(-2);
        ramMovement.Reason.Should().Be(StockMovementReason.Checkout);

        // Verify PCBuild untouched
        var dbBuild = await db.PCBuilds.Include(b => b.Items).FirstOrDefaultAsync(b => b.Id == pcBuild.Id);
        dbBuild.Should().NotBeNull();
        dbBuild!.Items.Should().HaveCount(2);

        // Verify Cart untouched
        var dbCart = await db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.UserId == user.Id);
        dbCart.Should().NotBeNull();
        dbCart!.Version.Should().Be(initialCartVersion);
        dbCart.Items.Should().HaveCount(1);
    }

    // 2. Stripe success: PendingPayment order, PaymentAttempt, redirect session URL, OrderItems have PCBuildId, Cart untouched.
    [Fact]
    public async Task BuyNowPCBuild_Stripe_CreatesPendingOrderAndSession_SetsPCBuildId_LeavesCartUntouched()
    {
        var (_, user, address, pcBuild, productCpu, productRam, initialCart) = await SeedCustomerWithBuildAndCartAsync(
            cpuStock: 10,
            ramStock: 10
        );

        var (client, _) = CreateClientWithFakeGateway();
        using var scopeAuth = _fixture.Factory.Services.CreateScope();
        var jwtGenerator = scopeAuth.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();
        var token = jwtGenerator.GenerateAccessToken(user);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var idempotencyKey = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/buy-now/pc-build/{pcBuild.Id}")
        {
            Content = JsonContent.Create(new BuyNowPCBuildRequest(
                AddressId: address.Id,
                PaymentMethod: PaymentMethod.Stripe,
                Notes: "Build Stripe order"
            )),
            Headers = { { "Idempotency-Key", idempotencyKey } }
        };

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var orderDto = await response.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions);
        orderDto.Should().NotBeNull();
        orderDto!.Status.Should().Be(OrderStatus.PendingPayment);
        orderDto.PaymentMethod.Should().Be(PaymentMethod.Stripe);
        orderDto.PaymentStatus.Should().Be(PaymentStatus.Pending);
        orderDto.PaymentAction.Should().NotBeNull();
        orderDto.PaymentAction!.Url.Should().Contain("checkout.stripe.com");

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var dbOrder = await db.Orders.Include(o => o.Items).Include(o => o.PaymentAttempts).FirstOrDefaultAsync(o => o.Id == orderDto.Id);
        dbOrder.Should().NotBeNull();
        dbOrder!.Items.Should().AllSatisfy(i => i.PCBuildId.Should().Be(pcBuild.Id));
        dbOrder.PaymentAttempts.Should().ContainSingle();

        var attempt = dbOrder.PaymentAttempts.First();
        attempt.Status.Should().Be(PaymentAttemptStatus.Pending);
        attempt.ProviderReference.Should().NotBeNullOrWhiteSpace();
        attempt.IdempotencyKey.Should().Be($"stripe-{dbOrder.Id:N}");
        dbOrder.CheckoutIdempotencyKey.Should().Be(idempotencyKey);

        // Verify Cart untouched
        var dbCart = await db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.UserId == user.Id);
        dbCart!.Version.Should().Be(initialCart!.Version);
    }

    // 3. Cross-user build returns 404 and no mutation.
    [Fact]
    public async Task BuyNowPCBuild_WhenUsingAnotherUsersBuild_ReturnsNotFound()
    {
        var (client1, _, _, _, _, _, _) = await SeedCustomerWithBuildAndCartAsync();
        var (_, _, _, build2, _, _, _) = await SeedCustomerWithBuildAndCartAsync();

        var (client1User, address1) = await GetUserAndDefaultAddressAsync(client1);

        var idempotencyKey = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/buy-now/pc-build/{build2.Id}")
        {
            Content = JsonContent.Create(new BuyNowPCBuildRequest(
                AddressId: address1.Id,
                PaymentMethod: PaymentMethod.COD,
                Notes: null
            )),
            Headers = { { "Idempotency-Key", idempotencyKey } }
        };

        var response = await client1.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Orders.CountAsync(o => o.UserId == client1User.Id)).Should().Be(0);
    }

    // 4. Address of another user returns 404 and no mutation.
    [Fact]
    public async Task BuyNowPCBuild_WhenUsingAnotherUsersAddress_ReturnsNotFound()
    {
        var (client1, user1, _, build1, _, _, _) = await SeedCustomerWithBuildAndCartAsync();
        var (_, _, address2, _, _, _, _) = await SeedCustomerWithBuildAndCartAsync();

        var idempotencyKey = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/buy-now/pc-build/{build1.Id}")
        {
            Content = JsonContent.Create(new BuyNowPCBuildRequest(
                AddressId: address2.Id, // User 2's address
                PaymentMethod: PaymentMethod.COD,
                Notes: null
            )),
            Headers = { { "Idempotency-Key", idempotencyKey } }
        };

        var response = await client1.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Orders.CountAsync(o => o.UserId == user1.Id)).Should().Be(0);
    }

    // 5. Empty build returns 409 Conflict and no mutation.
    [Fact]
    public async Task BuyNowPCBuild_WhenBuildHasNoItems_ReturnsConflict()
    {
        var (client, user, address, _, _, _, _) = await SeedCustomerWithBuildAndCartAsync();

        Guid emptyBuildId;
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var emptyBuild = new PCBuild("Empty Rig", user.Id, "No items");
            db.PCBuilds.Add(emptyBuild);
            await db.SaveChangesAsync();
            emptyBuildId = emptyBuild.Id;
        }

        var idempotencyKey = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/buy-now/pc-build/{emptyBuildId}")
        {
            Content = JsonContent.Create(new BuyNowPCBuildRequest(
                AddressId: address.Id,
                PaymentMethod: PaymentMethod.COD,
                Notes: null
            )),
            Headers = { { "Idempotency-Key", idempotencyKey } }
        };

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var scopeVerify = _fixture.Factory.Services.CreateScope();
        var dbVerify = scopeVerify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await dbVerify.Orders.CountAsync(o => o.UserId == user.Id)).Should().Be(0);
    }

    // 6. Insufficient stock on any product: 409 conflict, atomic rollback, no stock movements, cart untouched.
    [Fact]
    public async Task BuyNowPCBuild_WhenInsufficientStock_ReturnsConflict_NoPartialOrderOrStockChanges()
    {
        var (client, user, address, pcBuild, productCpu, productRam, initialCart) = await SeedCustomerWithBuildAndCartAsync(
            cpuStock: 10,
            ramStock: 1 // Build requires quantity 2 of RAM!
        );

        var idempotencyKey = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/buy-now/pc-build/{pcBuild.Id}")
        {
            Content = JsonContent.Create(new BuyNowPCBuildRequest(
                AddressId: address.Id,
                PaymentMethod: PaymentMethod.COD,
                Notes: null
            )),
            Headers = { { "Idempotency-Key", idempotencyKey } }
        };

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        (await db.Orders.CountAsync(o => o.UserId == user.Id)).Should().Be(0);
        (await db.StockMovements.CountAsync()).Should().Be(0);

        var dbCpu = await db.Products.FindAsync(productCpu.Id);
        var dbRam = await db.Products.FindAsync(productRam.Id);
        dbCpu!.StockQuantity.Should().Be(10);
        dbRam!.StockQuantity.Should().Be(1);

        var dbCart = await db.Carts.Include(c => c.Items).FirstAsync(c => c.UserId == user.Id);
        dbCart.Version.Should().Be(initialCart!.Version);
    }

    // 7. Incompatible or incomplete build checkouts successfully if products are active and in stock.
    [Fact]
    public async Task BuyNowPCBuild_WhenBuildIsIncompatible_AllowsCheckoutSuccessfully()
    {
        var (client, user, address, pcBuild, _, _, _) = await SeedCustomerWithBuildAndCartAsync(
            isBuildCompatible: false // Flagged as incompatible
        );

        pcBuild.IsCompatible.Should().BeFalse();

        var idempotencyKey = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/buy-now/pc-build/{pcBuild.Id}")
        {
            Content = JsonContent.Create(new BuyNowPCBuildRequest(
                AddressId: address.Id,
                PaymentMethod: PaymentMethod.COD,
                Notes: "Buying incompatible custom build"
            )),
            Headers = { { "Idempotency-Key", idempotencyKey } }
        };

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var orderDto = await response.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions);
        orderDto.Should().NotBeNull();
        orderDto!.Status.Should().Be(OrderStatus.Confirmed);
    }

    // 8. Idempotency replay returns same order, does not deduct stock again.
    [Fact]
    public async Task BuyNowPCBuild_SameIdempotencyKeyReplay_ReturnsSameOrder_StockDeductedOnce()
    {
        var (client, user, address, pcBuild, productCpu, productRam, _) = await SeedCustomerWithBuildAndCartAsync(
            cpuStock: 10,
            ramStock: 10
        );

        var idempotencyKey = Guid.NewGuid().ToString("N");

        HttpRequestMessage CreateRequest() => new(HttpMethod.Post, $"/api/v1/orders/buy-now/pc-build/{pcBuild.Id}")
        {
            Content = JsonContent.Create(new BuyNowPCBuildRequest(
                AddressId: address.Id,
                PaymentMethod: PaymentMethod.COD,
                Notes: null
            )),
            Headers = { { "Idempotency-Key", idempotencyKey } }
        };

        // First attempt
        using var req1 = CreateRequest();
        var resp1 = await client.SendAsync(req1);
        resp1.StatusCode.Should().Be(HttpStatusCode.Created);
        var order1 = await resp1.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions);

        // Replay with exact same key
        using var req2 = CreateRequest();
        var resp2 = await client.SendAsync(req2);
        resp2.StatusCode.Should().Be(HttpStatusCode.Created);
        var order2 = await resp2.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions);

        order2!.Id.Should().Be(order1!.Id);
        order2.OrderNumber.Should().Be(order1.OrderNumber);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var dbCpu = await db.Products.FindAsync(productCpu.Id);
        var dbRam = await db.Products.FindAsync(productRam.Id);
        dbCpu!.StockQuantity.Should().Be(9);
        dbRam!.StockQuantity.Should().Be(8);

        var orderCount = await db.Orders.CountAsync(o => o.UserId == user.Id);
        orderCount.Should().Be(1);

        var movements = await db.StockMovements.ToListAsync();
        movements.Should().HaveCount(2); // exactly 1 for CPU and 1 for RAM
    }

    // 9. Concurrent same-key requests create exactly one order.
    [Fact]
    public async Task BuyNowPCBuild_ConcurrentRequests_SameIdempotencyKey_CreatesExactlyOneOrder()
    {
        var (client, user, address, pcBuild, productCpu, productRam, _) = await SeedCustomerWithBuildAndCartAsync(
            cpuStock: 10,
            ramStock: 10
        );

        var idempotencyKey = Guid.NewGuid().ToString("N");

        Func<Task<HttpResponseMessage>> sendFunc = () =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/buy-now/pc-build/{pcBuild.Id}")
            {
                Content = JsonContent.Create(new BuyNowPCBuildRequest(
                    AddressId: address.Id,
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

        order1!.Id.Should().Be(order2!.Id);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var orderCount = await db.Orders.CountAsync(o => o.UserId == user.Id);
        orderCount.Should().Be(1);

        var dbCpu = await db.Products.FindAsync(productCpu.Id);
        var dbRam = await db.Products.FindAsync(productRam.Id);
        dbCpu!.StockQuantity.Should().Be(9);
        dbRam!.StockQuantity.Should().Be(8);
    }

    // 10. Competing requests with limited stock do not oversell.
    [Fact]
    public async Task BuyNowPCBuild_ConcurrentRequests_DifferentKeys_CompeteForStock_NoOversell()
    {
        // Stock only enough for exactly 1 build: CPU stock = 1, RAM stock = 2
        var (client1, _, address1, build1, productCpu, productRam, _) = await SeedCustomerWithBuildAndCartAsync(
            cpuStock: 1,
            ramStock: 2
        );

        // Second user with own build pointing to the exact same productCpu & productRam
        var (client2, user2, address2, _) = await CreateSecondCustomerWithBuildAsync(productCpu.Id, productRam.Id);

        var key1 = Guid.NewGuid().ToString("N");
        var key2 = Guid.NewGuid().ToString("N");

        var req1 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/buy-now/pc-build/{build1.Id}")
        {
            Content = JsonContent.Create(new BuyNowPCBuildRequest(
                AddressId: address1.Id,
                PaymentMethod: PaymentMethod.COD,
                Notes: null
            )),
            Headers = { { "Idempotency-Key", key1 } }
        };

        var req2 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/buy-now/pc-build/{user2.BuildId}")
        {
            Content = JsonContent.Create(new BuyNowPCBuildRequest(
                AddressId: address2.Id,
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

        var dbCpu = await db.Products.FindAsync(productCpu.Id);
        var dbRam = await db.Products.FindAsync(productRam.Id);
        dbCpu!.StockQuantity.Should().Be(0);
        dbRam!.StockQuantity.Should().Be(0);

        (await db.Orders.CountAsync()).Should().Be(1);
    }

    // 11. Regression: Product Buy Now and Cart Checkout continue to work properly.
    [Fact]
    public async Task BuyNowPCBuild_Regression_ProductBuyNowAndCartCheckout_ContinueToFunction()
    {
        var (client, user, address, _, productCpu, _, initialCart) = await SeedCustomerWithBuildAndCartAsync(
            cpuStock: 10,
            hasCart: true
        );

        // A. Product Buy Now works
        var buyNowKey = Guid.NewGuid().ToString("N");
        using var buyNowReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/buy-now/product/{productCpu.Id}")
        {
            Content = JsonContent.Create(new BuyNowProductRequest(
                AddressId: address.Id,
                Quantity: 1,
                PaymentMethod: PaymentMethod.COD,
                Notes: "Product buy now regression"
            )),
            Headers = { { "Idempotency-Key", buyNowKey } }
        };

        var buyNowResp = await client.SendAsync(buyNowReq);
        buyNowResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // B. Cart Checkout works
        var cartCheckoutKey = Guid.NewGuid().ToString("N");
        using var cartReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(
                AddressId: address.Id,
                ExpectedCartVersion: initialCart!.Version,
                PaymentMethod: PaymentMethod.COD,
                Notes: "Cart checkout regression"
            )),
            Headers = { { "Idempotency-Key", cartCheckoutKey } }
        };

        var cartResp = await client.SendAsync(cartReq);
        cartResp.StatusCode.Should().Be(HttpStatusCode.Created);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var dbCart = await db.Carts.Include(c => c.Items).FirstAsync(c => c.UserId == user.Id);
        dbCart.Items.Should().BeEmpty();
        dbCart.Version.Should().Be(initialCart.Version + 1);

        (await db.Orders.CountAsync(o => o.UserId == user.Id)).Should().Be(2);
    }

    private async Task<(User User, UserAddress Address)> GetUserAndDefaultAddressAsync(HttpClient client)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.FirstAsync();
        var address = await db.UserAddresses.FirstAsync(a => a.UserId == user.Id);
        return (user, address);
    }

    private async Task<(HttpClient Client, (Guid UserId, Guid BuildId) User, UserAddress Address, Cart? Cart)> CreateSecondCustomerWithBuildAsync(
        Guid cpuProductId,
        Guid ramProductId)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var jwtGenerator = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(
            email: $"second_customer_{suffix}@riutek.test",
            passwordHash: "dummy_hashed_password",
            fullName: $"Second User {suffix}",
            role: UserRole.Customer,
            phoneNumber: "0909888777"
        );
        db.Users.Add(user);

        var address = new UserAddress(
            userId: user.Id,
            receiverName: $"Receiver2 {suffix}",
            phoneNumber: "0909888777",
            addressLine: "789 Other Road",
            ward: "Ward 5",
            district: "District 5",
            city: "TP Ho Chi Minh",
            isDefault: true
        );
        db.UserAddresses.Add(address);

        var pcBuild = new PCBuild($"Compete Rig {suffix}", user.Id);
        var itemCpu = new PCBuildItem(pcBuild.Id, cpuProductId, ComponentType.Cpu, 100_000m, 1);
        var itemRam = new PCBuildItem(pcBuild.Id, ramProductId, ComponentType.Ram, 100_000m, 2);
        pcBuild.AddItem(itemCpu);
        pcBuild.AddItem(itemRam);
        db.PCBuilds.Add(pcBuild);

        await db.SaveChangesAsync();

        var token = jwtGenerator.GenerateAccessToken(user);
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return (client, (user.Id, pcBuild.Id), address, null);
    }
}
