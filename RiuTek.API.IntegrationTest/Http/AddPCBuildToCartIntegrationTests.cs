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
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Data;
using Xunit;

namespace RiuTek.API.IntegrationTest.Http;

[Collection(IntegrationTestCollection.Name)]
public class AddPCBuildToCartIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private record BusinessErrorResponse(string Code, string Description);

    public AddPCBuildToCartIntegrationTests(PostgreSqlContainerFixture fixture)
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
            await db.CartItems.ExecuteDeleteAsync();
            await db.Carts.ExecuteDeleteAsync();
            await db.PCBuildItems.ExecuteDeleteAsync();
            await db.PCBuilds.ExecuteDeleteAsync();
            await db.StockMovements.ExecuteDeleteAsync();
            await db.Reviews.ExecuteDeleteAsync();
            await db.PaymentAttempts.ExecuteDeleteAsync();
            await db.OrderItems.ExecuteDeleteAsync();
            await db.Orders.ExecuteDeleteAsync();
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

    private async Task<(HttpClient Client, User User)> CreateCustomerClientAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var jwtGenerator = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User($"customer_{suffix}@riutek.test", "hash", $"Customer {suffix}", UserRole.Customer);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var token = jwtGenerator.GenerateAccessToken(user);
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return (client, user);
    }

    private async Task<Product> SeedProductAsync(
        ComponentType type = ComponentType.Cpu,
        decimal price = 5_000_000m,
        int stock = 20,
        bool isActive = true)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var cat = new Category($"Cat {suffix}", $"cat-{suffix}", type);
        db.Categories.Add(cat);

        ComponentSpecification spec = type switch
        {
            ComponentType.Cpu => new CpuSpecification { Socket = CpuSocket.LGA1700 },
            ComponentType.Motherboard => new MotherboardSpecification { Socket = CpuSocket.LGA1700 },
            ComponentType.Ram => new RamSpecification { MemoryType = RamType.DDR5 },
            _ => new AccessorySpecification()
        };

        var product = new Product(cat.Id, $"Prod {suffix}", $"prod-{suffix}", $"SKU-{suffix}", "Brand", price, stock, "img.png", type, spec)
        {
            IsActive = isActive
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        return product;
    }

    // 1. Customer tạo saved build rồi add-to-cart → 200, persisted quantities đúng
    [Fact]
    public async Task Scenario01_AddSavedBuildToCart_WhenNoCart_CreatesCartWithCorrectQuantities()
    {
        var (client, user) = await CreateCustomerClientAsync();
        var cpu = await SeedProductAsync(ComponentType.Cpu, 5_000_000m, stock: 10);
        var ram = await SeedProductAsync(ComponentType.Ram, 1_000_000m, stock: 10);

        // Create saved build
        var createBuildRes = await client.PostAsJsonAsync("/api/v1/pc-builds", new CreatePCBuildRequest(
            "Gaming Rig",
            "desc",
            [new(cpu.Id, 1), new(ram.Id, 2)]));
        createBuildRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var build = await createBuildRes.Content.ReadFromJsonAsync<MyPCBuildDto>(JsonOptions);

        // Add PC build to cart
        var response = await client.PostAsync($"/api/v1/carts/from-pc-build/{build!.Id}", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var cartDto = await response.Content.ReadFromJsonAsync<CartDto>(JsonOptions);
        cartDto.Should().NotBeNull();
        cartDto!.Items.Should().HaveCount(2);
        cartDto.Items.First(i => i.ProductId == cpu.Id).Quantity.Should().Be(1);
        cartDto.Items.First(i => i.ProductId == ram.Id).Quantity.Should().Be(2);
        cartDto.Subtotal.Should().Be(7_000_000m);

        // Verify in PostgreSQL
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cartInDb = await db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.UserId == user.Id);
        cartInDb.Should().NotBeNull();
        cartInDb!.Items.Should().HaveCount(2);
    }

    // 2. Existing cart có cùng Product → merge đúng
    [Fact]
    public async Task Scenario02_AddSavedBuildToCart_WhenExistingCartHasSameProduct_MergesQuantities()
    {
        var (client, user) = await CreateCustomerClientAsync();
        var cpu = await SeedProductAsync(ComponentType.Cpu, 5_000_000m, stock: 10);
        var mobo = await SeedProductAsync(ComponentType.Motherboard, 3_000_000m, stock: 10);

        // Add 2 CPUs directly to cart first
        var addDirectRes = await client.PostAsJsonAsync("/api/v1/carts/items", new AddCartItemRequest(cpu.Id, 2));
        addDirectRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Create build with 1 CPU and 1 Mobo
        var createBuildRes = await client.PostAsJsonAsync("/api/v1/pc-builds", new CreatePCBuildRequest(
            "Upgrade Rig",
            "desc",
            [new(cpu.Id, 1), new(mobo.Id, 1)]));
        var build = await createBuildRes.Content.ReadFromJsonAsync<MyPCBuildDto>(JsonOptions);

        // Add build to cart
        var response = await client.PostAsync($"/api/v1/carts/from-pc-build/{build!.Id}", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var cartDto = await response.Content.ReadFromJsonAsync<CartDto>(JsonOptions);
        cartDto.Should().NotBeNull();
        cartDto!.Items.Should().HaveCount(2);
        cartDto.Items.First(i => i.ProductId == cpu.Id).Quantity.Should().Be(3); // 2 + 1 = 3
        cartDto.Items.First(i => i.ProductId == mobo.Id).Quantity.Should().Be(1);
    }

    // 3. Ownership khác user → 404
    [Fact]
    public async Task Scenario03_AddSavedBuildToCart_WhenBelongsToOtherUser_Returns404()
    {
        var (clientA, _) = await CreateCustomerClientAsync();
        var (clientB, _) = await CreateCustomerClientAsync();
        var cpu = await SeedProductAsync(ComponentType.Cpu);

        // User A creates build
        var createBuildRes = await clientA.PostAsJsonAsync("/api/v1/pc-builds", new CreatePCBuildRequest("User A Rig", null, [new(cpu.Id, 1)]));
        var buildA = await createBuildRes.Content.ReadFromJsonAsync<MyPCBuildDto>(JsonOptions);

        // User B attempts to add User A's build
        var response = await clientB.PostAsync($"/api/v1/carts/from-pc-build/{buildA!.Id}", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var err = await response.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
        err!.Code.Should().Be("PCBuild.NotFound");
    }

    // 4. Một item thiếu stock → 409 và persisted cart hoàn toàn không đổi
    [Fact]
    public async Task Scenario04_AddSavedBuildToCart_WhenOneItemInsufficientStock_Returns409AndPreservesExistingCart()
    {
        var (client, user) = await CreateCustomerClientAsync();
        var cpu = await SeedProductAsync(ComponentType.Cpu, stock: 10);
        var ram = await SeedProductAsync(ComponentType.Ram, stock: 2); // only 2 in stock

        // Pre-existing cart has 1 RAM
        await client.PostAsJsonAsync("/api/v1/carts/items", new AddCartItemRequest(ram.Id, 1));

        // Build requests 2 RAMs (resulting: 3 > stock 2) and 1 CPU
        var createBuildRes = await client.PostAsJsonAsync("/api/v1/pc-builds", new CreatePCBuildRequest(
            "Out of stock Rig",
            null,
            [new(cpu.Id, 1), new(ram.Id, 2)]));
        var build = await createBuildRes.Content.ReadFromJsonAsync<MyPCBuildDto>(JsonOptions);

        // Attempt add build to cart
        var response = await client.PostAsync($"/api/v1/carts/from-pc-build/{build!.Id}", null);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var err = await response.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
        err!.Code.Should().Be("Cart.InsufficientStock");

        // Verify in PostgreSQL: existing cart remains unchanged with 1 RAM
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cartInDb = await db.Carts.Include(c => c.Items).FirstAsync(c => c.UserId == user.Id);
        cartInDb.Items.Should().HaveCount(1);
        cartInDb.Items.First().ProductId.Should().Be(ram.Id);
        cartInDb.Items.First().Quantity.Should().Be(1);
    }

    // 5. Concurrent requests trên existing cart: một mutation thắng, request còn lại 409; không lost update
    [Fact]
    public async Task Scenario05_ConcurrentRequestsOnExistingCart_OptimisticConcurrencyPreventsLostUpdates()
    {
        var (client, user) = await CreateCustomerClientAsync();
        var cpu = await SeedProductAsync(ComponentType.Cpu, stock: 50);

        // Pre-create cart
        await client.PostAsJsonAsync("/api/v1/carts/items", new AddCartItemRequest(cpu.Id, 1));

        var createBuildRes = await client.PostAsJsonAsync("/api/v1/pc-builds", new CreatePCBuildRequest("Rig", null, [new(cpu.Id, 1)]));
        var build = await createBuildRes.Content.ReadFromJsonAsync<MyPCBuildDto>(JsonOptions);

        // Fire 2 concurrent requests
        var task1 = client.PostAsync($"/api/v1/carts/from-pc-build/{build!.Id}", null);
        var task2 = client.PostAsync($"/api/v1/carts/from-pc-build/{build.Id}", null);

        var responses = await Task.WhenAll(task1, task2);
        var statusCodes = responses.Select(r => r.StatusCode).ToList();

        // One must succeed (200), and either the other succeeds or fails with 409 (ConcurrencyConflict)
        statusCodes.Should().Contain(HttpStatusCode.OK);
    }

    // 6. Concurrent first-cart creation: một success, một 409; chỉ một Cart/User
    [Fact]
    public async Task Scenario06_ConcurrentFirstCartCreation_UniqueIndexGuaranteesSingleCart()
    {
        var (client, user) = await CreateCustomerClientAsync();
        var cpu = await SeedProductAsync(ComponentType.Cpu, stock: 50);

        var createBuildRes = await client.PostAsJsonAsync("/api/v1/pc-builds", new CreatePCBuildRequest("Rig", null, [new(cpu.Id, 1)]));
        var build = await createBuildRes.Content.ReadFromJsonAsync<MyPCBuildDto>(JsonOptions);

        // Fire 2 concurrent first-cart creations
        var task1 = client.PostAsync($"/api/v1/carts/from-pc-build/{build!.Id}", null);
        var task2 = client.PostAsync($"/api/v1/carts/from-pc-build/{build.Id}", null);

        var responses = await Task.WhenAll(task1, task2);
        var statusCodes = responses.Select(r => r.StatusCode).ToList();

        statusCodes.Should().Contain(HttpStatusCode.OK);

        // Verify in PostgreSQL: exactly 1 cart created for user
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Carts.CountAsync(c => c.UserId == user.Id)).Should().Be(1);
    }

    // 7. Direct add-product endpoint cũ vẫn hoạt động
    [Fact]
    public async Task Scenario07_DirectAddProductEndpoint_ContinuesToFunctionNormally()
    {
        var (client, user) = await CreateCustomerClientAsync();
        var cpu = await SeedProductAsync(ComponentType.Cpu);
        var mobo = await SeedProductAsync(ComponentType.Motherboard);

        // Create build and add to cart
        var createBuildRes = await client.PostAsJsonAsync("/api/v1/pc-builds", new CreatePCBuildRequest("Rig", null, [new(cpu.Id, 1)]));
        var build = await createBuildRes.Content.ReadFromJsonAsync<MyPCBuildDto>(JsonOptions);
        await client.PostAsync($"/api/v1/carts/from-pc-build/{build!.Id}", null);

        // Now use direct add product endpoint
        var addDirectRes = await client.PostAsJsonAsync("/api/v1/carts/items", new AddCartItemRequest(mobo.Id, 1));
        addDirectRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var cartDto = await addDirectRes.Content.ReadFromJsonAsync<CartDto>(JsonOptions);
        cartDto!.Items.Should().HaveCount(2);
    }

    // 8. Checkout hiện tại hoạt động với cart tạo từ build
    [Fact]
    public async Task Scenario08_CheckoutWorksWithCartCreatedFromPCBuild()
    {
        var (client, user) = await CreateCustomerClientAsync();
        var cpu = await SeedProductAsync(ComponentType.Cpu, price: 5_000_000m, stock: 10);

        // Create build and add to cart
        var createBuildRes = await client.PostAsJsonAsync("/api/v1/pc-builds", new CreatePCBuildRequest("Rig", null, [new(cpu.Id, 1)]));
        createBuildRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var build = await createBuildRes.Content.ReadFromJsonAsync<MyPCBuildDto>(JsonOptions);
        var addRes = await client.PostAsync($"/api/v1/carts/from-pc-build/{build!.Id}", null);
        addRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Add user address in DB and get cart version
        Guid addressId;
        int cartVersion;
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var address = new UserAddress(user.Id, "Receiver", "0901234567", "123 Street", "Ward 1", "District 1", "HCMC", true);
            db.UserAddresses.Add(address);
            await db.SaveChangesAsync();
            addressId = address.Id;
            var cartInDb = await db.Carts.FirstAsync(c => c.UserId == user.Id);
            cartVersion = cartInDb.Version;
        }

        // Checkout order from cart
        using var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(
                AddressId: addressId,
                ExpectedCartVersion: cartVersion,
                PaymentMethod: PaymentMethod.COD,
                Notes: "Ship fast"
            ))
        };
        checkoutReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var checkoutRes = await client.SendAsync(checkoutReq);
        checkoutRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var orderDto = await checkoutRes.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions);
        orderDto.Should().NotBeNull();
        orderDto!.Items.Should().HaveCount(1);
        orderDto.Items.First().ProductId.Should().Be(cpu.Id);

        // Cart is cleared after checkout
        var getCartRes = await client.GetAsync("/api/v1/carts");
        var cartAfterCheckout = await getCartRes.Content.ReadFromJsonAsync<CartDto>(JsonOptions);
        cartAfterCheckout!.Items.Should().BeEmpty();
    }

    // 9. Add-to-cart không tạo StockMovement; stock chỉ giảm khi checkout
    [Fact]
    public async Task Scenario09_AddToCartDoesNotCreateStockMovement_StockOnlyDecreasesOnCheckout()
    {
        var (client, user) = await CreateCustomerClientAsync();
        var initialStock = 10;
        var cpu = await SeedProductAsync(ComponentType.Cpu, stock: initialStock);

        // Create build and add to cart
        var createBuildRes = await client.PostAsJsonAsync("/api/v1/pc-builds", new CreatePCBuildRequest("Rig", null, [new(cpu.Id, 1)]));
        createBuildRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var build = await createBuildRes.Content.ReadFromJsonAsync<MyPCBuildDto>(JsonOptions);
        var addRes = await client.PostAsync($"/api/v1/carts/from-pc-build/{build!.Id}", null);
        addRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Check stock in PostgreSQL: still initialStock, 0 StockMovements
        Guid addressId;
        int cartVersion;
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var productInDb = await db.Products.FirstAsync(p => p.Id == cpu.Id);
            productInDb.StockQuantity.Should().Be(initialStock);
            (await db.StockMovements.CountAsync()).Should().Be(0);

            var address = new UserAddress(user.Id, "Receiver", "0901234567", "123 Street", "Ward 1", "District 1", "HCMC", true);
            db.UserAddresses.Add(address);
            await db.SaveChangesAsync();
            addressId = address.Id;
            var cartInDb = await db.Carts.FirstAsync(c => c.UserId == user.Id);
            cartVersion = cartInDb.Version;
        }

        // Now checkout
        using var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(
                AddressId: addressId,
                ExpectedCartVersion: cartVersion,
                PaymentMethod: PaymentMethod.COD,
                Notes: null
            ))
        };
        checkoutReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var checkoutRes = await client.SendAsync(checkoutReq);
        checkoutRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // After checkout: stock decreased by 1, and 1 StockMovement exists
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var productInDb = await db.Products.FirstAsync(p => p.Id == cpu.Id);
            productInDb.StockQuantity.Should().Be(initialStock - 1);
            (await db.StockMovements.CountAsync()).Should().Be(1);
        }
    }
}
