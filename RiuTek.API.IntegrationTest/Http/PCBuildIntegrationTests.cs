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
using RiuTek.Application.Common.Models;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.PCBuilds.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Data;
using Xunit;

namespace RiuTek.API.IntegrationTest.Http;

[Collection(IntegrationTestCollection.Name)]
public class PCBuildIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private record BusinessErrorResponse(string Code, string Description);

    public PCBuildIntegrationTests(PostgreSqlContainerFixture fixture)
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

    private async Task<(HttpClient Client, User User)> CreateUserClientAsync(UserRole role = UserRole.Customer, bool isActive = true)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var jwtGenerator = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(
            email: $"user_{suffix}@riutek.test",
            passwordHash: "dummy_hashed_password",
            fullName: $"User {suffix}",
            role: role)
        {
            IsActive = isActive
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        var token = jwtGenerator.GenerateAccessToken(user);
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return (client, user);
    }

    private async Task<(Category Category, Product Product)> SeedProductAsync(
        ComponentType type = ComponentType.Cpu,
        decimal price = 5_000_000m,
        int stock = 10,
        bool isActive = true,
        ComponentSpecification? spec = null)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = new Category(
            name: $"Cat {type} {suffix}",
            slug: $"cat-{type.ToString().ToLower()}-{suffix}",
            componentType: type);
        db.Categories.Add(category);

        spec ??= type switch
        {
            ComponentType.Cpu => new CpuSpecification { Socket = CpuSocket.LGA1700, CoreCount = 10, ThreadCount = 16, TdpWattage = 125, SupportedMemoryType = RamType.DDR5 },
            ComponentType.Motherboard => new MotherboardSpecification { Socket = CpuSocket.LGA1700, Chipset = "B760", FormFactor = MotherboardFormFactor.ATX, MemoryType = RamType.DDR5, MemorySlots = 4, MaxMemoryCapacityGb = 128 },
            ComponentType.Ram => new RamSpecification { MemoryType = RamType.DDR5, ModuleCount = 2, CapacityGb = 32 },
            ComponentType.Gpu => new GpuSpecification { Chipset = "RTX 4070", VramGb = 12, TdpWattage = 200, LengthMm = 242 },
            ComponentType.Storage => new StorageSpecification { StorageType = StorageType.NVMe_M2, CapacityGb = 1000 },
            ComponentType.Psu => new PsuSpecification { Wattage = 750, Efficiency = PsuEfficiency.Plus80Gold, FormFactor = PsuFormFactor.ATX },
            ComponentType.Case => new CaseSpecification { SupportedFormFactors = [MotherboardFormFactor.ATX], MaxGpuLengthMm = 350, MaxCpuCoolerHeightMm = 165 },
            ComponentType.Cooler => new CoolerSpecification { CoolerType = CoolerType.Air, SupportedSockets = [CpuSocket.LGA1700], HeightMm = 155, MaxTdpRating = 180 },
            _ => new StorageSpecification { StorageType = StorageType.SATA_SSD, CapacityGb = 500 }
        };

        var product = new Product(
            categoryId: category.Id,
            name: $"Product {type} {suffix}",
            slug: $"prod-{type.ToString().ToLower()}-{suffix}",
            sku: $"SKU_{suffix}",
            brand: "BrandX",
            price: price,
            stockQuantity: stock,
            imageUrl: $"https://img.test/{suffix}.jpg",
            componentType: type,
            specifications: spec)
        {
            IsActive = isActive
        };

        db.Products.Add(product);
        await db.SaveChangesAsync();

        return (category, product);
    }

    [Fact]
    public async Task Preview_WhenAnonymous_ShouldReturn200_AndNotPersistAnyPCBuild()
    {
        var client = _fixture.Factory.CreateClient();
        var (_, cpu) = await SeedProductAsync(ComponentType.Cpu, 7_000_000m);
        var (_, mobo) = await SeedProductAsync(ComponentType.Motherboard, 4_000_000m);

        var request = new PreviewPCBuildRequest(
        [
            new(cpu.Id, 1),
            new(mobo.Id, 1)
        ]);

        var response = await client.PostAsJsonAsync("/api/v1/pc-builds/preview", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var preview = await response.Content.ReadFromJsonAsync<PCBuildPreviewResultDto>(JsonOptions);
        preview.Should().NotBeNull();
        preview!.TotalPrice.Should().Be(11_000_000m);
        preview.Items.Should().HaveCount(2);

        // Verify NO records were written to PostgreSQL
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.PCBuilds.CountAsync()).Should().Be(0);
        (await db.PCBuildItems.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Preview_WhenMissingProductId_ShouldReturn404NotFound()
    {
        var client = _fixture.Factory.CreateClient();
        var missingId = Guid.NewGuid();

        var request = new PreviewPCBuildRequest([new(missingId, 1)]);

        var response = await client.PostAsJsonAsync("/api/v1/pc-builds/preview", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var err = await response.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
        err.Should().NotBeNull();
        err!.Code.Should().Be("PCBuild.ProductNotFound");
    }

    [Fact]
    public async Task Preview_WhenIncompatibleHardware_ShouldReturn200WithIsCompatibleFalse()
    {
        var client = _fixture.Factory.CreateClient();
        // LGA1700 Intel CPU + AM5 AMD Motherboard
        var (_, cpu) = await SeedProductAsync(ComponentType.Cpu, spec: new CpuSpecification { Socket = CpuSocket.LGA1700, SupportedMemoryType = RamType.DDR5 });
        var (_, mobo) = await SeedProductAsync(ComponentType.Motherboard, spec: new MotherboardSpecification { Socket = CpuSocket.AM5, MemoryType = RamType.DDR5 });

        var request = new PreviewPCBuildRequest([new(cpu.Id, 1), new(mobo.Id, 1)]);

        var response = await client.PostAsJsonAsync("/api/v1/pc-builds/preview", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var preview = await response.Content.ReadFromJsonAsync<PCBuildPreviewResultDto>(JsonOptions);
        preview.Should().NotBeNull();
        preview!.IsCompatible.Should().BeFalse();
        preview.Issues.Should().Contain(i => i.RuleId == "COMP-01" && i.Severity == "Error");
    }

    [Fact]
    public async Task Preview_WhenProductInactiveOrOutOfStock_ShouldReturnCanPurchaseFalse()
    {
        var client = _fixture.Factory.CreateClient();
        var (_, activeCpu) = await SeedProductAsync(ComponentType.Cpu, stock: 5, isActive: true);
        var (_, inactiveMobo) = await SeedProductAsync(ComponentType.Motherboard, stock: 5, isActive: false);
        var (_, outOfStockRam) = await SeedProductAsync(ComponentType.Ram, stock: 0, isActive: true);

        var request = new PreviewPCBuildRequest(
        [
            new(activeCpu.Id, 1),
            new(inactiveMobo.Id, 1),
            new(outOfStockRam.Id, 1)
        ]);

        var response = await client.PostAsJsonAsync("/api/v1/pc-builds/preview", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var preview = await response.Content.ReadFromJsonAsync<PCBuildPreviewResultDto>(JsonOptions);
        preview.Should().NotBeNull();
        preview!.CanPurchaseAll.Should().BeFalse();

        var cpuItem = preview.Items.First(i => i.ProductId == activeCpu.Id);
        cpuItem.CanPurchase.Should().BeTrue();
        cpuItem.IssueCode.Should().BeNull();

        var moboItem = preview.Items.First(i => i.ProductId == inactiveMobo.Id);
        moboItem.CanPurchase.Should().BeFalse();
        moboItem.IssueCode.Should().Be(PCBuildResponseBuilder.IssueProductInactive);

        var ramItem = preview.Items.First(i => i.ProductId == outOfStockRam.Id);
        ramItem.CanPurchase.Should().BeFalse();
        ramItem.IssueCode.Should().Be(PCBuildResponseBuilder.IssueInsufficientStock);
    }

    [Fact]
    public async Task CustomerBuildLifecycle_Create_Mine_Get_Update_Delete()
    {
        var (client, customer) = await CreateUserClientAsync(UserRole.Customer);
        var (_, cpu) = await SeedProductAsync(ComponentType.Cpu, 8_000_000m);
        var (_, mobo) = await SeedProductAsync(ComponentType.Motherboard, 4_000_000m);
        var (_, ram) = await SeedProductAsync(ComponentType.Ram, 2_000_000m);

        // 1. Create Build
        var createRequest = new CreatePCBuildRequest(
            Name: "Master Build",
            Description: "High-end developer PC",
            Items:
            [
                new(cpu.Id, 1),
                new(mobo.Id, 1)
            ]);

        var createResponse = await client.PostAsJsonAsync("/api/v1/pc-builds", createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<MyPCBuildDto>(JsonOptions);
        created.Should().NotBeNull();
        created!.Name.Should().Be("Master Build");
        created.Status.Should().Be(PCBuildStatus.Saved);
        created.TotalPrice.Should().Be(12_000_000m);
        var buildId = created.Id;

        // 2. Mine List
        var mineResponse = await client.GetAsync("/api/v1/pc-builds/mine?pageIndex=1&pageSize=10");
        mineResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var paged = await mineResponse.Content.ReadFromJsonAsync<PagedResult<MyPCBuildSummaryDto>>(JsonOptions);
        paged.Should().NotBeNull();
        paged!.TotalCount.Should().Be(1);
        paged.Items[0].Id.Should().Be(buildId);

        // 3. Get By ID
        var getResponse = await client.GetAsync($"/api/v1/pc-builds/{buildId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResponse.Content.ReadFromJsonAsync<MyPCBuildDto>(JsonOptions);
        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(buildId);
        fetched.Items.Should().HaveCount(2);

        // 4. Update Build
        var updateRequest = new UpdatePCBuildRequest(
            Name: "Master Build V2",
            Description: "Added RAM",
            Items:
            [
                new(cpu.Id, 1),
                new(mobo.Id, 1),
                new(ram.Id, 2)
            ]);

        var updateResponse = await client.PutAsJsonAsync($"/api/v1/pc-builds/{buildId}", updateRequest);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<MyPCBuildDto>(JsonOptions);
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Master Build V2");
        updated.TotalPrice.Should().Be(16_000_000m); // 8M + 4M + 2M*2
        updated.Items.Should().HaveCount(3);

        // 5. Delete Build
        var deleteResponse = await client.DeleteAsync($"/api/v1/pc-builds/{buildId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 6. Verify 404 after deletion
        var getDeletedResponse = await client.GetAsync($"/api/v1/pc-builds/{buildId}");
        getDeletedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CustomerIsolation_CustomerACannotReadUpdateOrDeleteCustomerBBuild()
    {
        var (clientA, _) = await CreateUserClientAsync(UserRole.Customer);
        var (clientB, _) = await CreateUserClientAsync(UserRole.Customer);
        var (_, cpu) = await SeedProductAsync(ComponentType.Cpu);

        // Customer A creates build
        var createResponse = await clientA.PostAsJsonAsync("/api/v1/pc-builds", new CreatePCBuildRequest("Build A", null, [new(cpu.Id, 1)]));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<MyPCBuildDto>(JsonOptions);
        var buildId = created!.Id;

        // Customer B tries to read Customer A's build -> 404
        var getB = await clientB.GetAsync($"/api/v1/pc-builds/{buildId}");
        getB.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Customer B tries to update Customer A's build -> 404
        var putB = await clientB.PutAsJsonAsync($"/api/v1/pc-builds/{buildId}", new UpdatePCBuildRequest("Hacked", null, [new(cpu.Id, 1)]));
        putB.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Customer B tries to delete Customer A's build -> 404
        var deleteB = await clientB.DeleteAsync($"/api/v1/pc-builds/{buildId}");
        deleteB.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Customer A can still read build untouched
        var getA = await clientA.GetAsync($"/api/v1/pc-builds/{buildId}");
        getA.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CustomerPersistenceEndpoints_StaffAndAdmin_ShouldReturn403Forbidden()
    {
        var (staffClient, _) = await CreateUserClientAsync(UserRole.Staff);
        var (adminClient, _) = await CreateUserClientAsync(UserRole.Admin);
        var (_, cpu) = await SeedProductAsync(ComponentType.Cpu);

        // Staff attempts create
        var staffCreate = await staffClient.PostAsJsonAsync("/api/v1/pc-builds", new CreatePCBuildRequest("Staff Build", null, [new(cpu.Id, 1)]));
        staffCreate.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Admin attempts mine query
        var adminMine = await adminClient.GetAsync("/api/v1/pc-builds/mine");
        adminMine.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Anonymous attempts mine query
        var anonClient = _fixture.Factory.CreateClient();
        var anonMine = await anonClient.GetAsync("/api/v1/pc-builds/mine");
        anonMine.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task StablePagination_AndLongOffset_ShouldBeSafe()
    {
        var (client, _) = await CreateUserClientAsync(UserRole.Customer);
        var (_, cpu) = await SeedProductAsync(ComponentType.Cpu);

        // Create 3 builds
        for (var i = 1; i <= 3; i++)
        {
            var res = await client.PostAsJsonAsync("/api/v1/pc-builds", new CreatePCBuildRequest($"Build #{i}", null, [new(cpu.Id, 1)]));
            res.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // Page 1 with size 2
        var page1 = await client.GetFromJsonAsync<PagedResult<MyPCBuildSummaryDto>>("/api/v1/pc-builds/mine?pageIndex=1&pageSize=2", JsonOptions);
        page1.Should().NotBeNull();
        page1!.Items.Should().HaveCount(2);
        page1.TotalCount.Should().Be(3);

        // Page 2 with size 2
        var page2 = await client.GetFromJsonAsync<PagedResult<MyPCBuildSummaryDto>>("/api/v1/pc-builds/mine?pageIndex=2&pageSize=2", JsonOptions);
        page2.Should().NotBeNull();
        page2!.Items.Should().HaveCount(1);

        // Far beyond page
        var pageFar = await client.GetFromJsonAsync<PagedResult<MyPCBuildSummaryDto>>("/api/v1/pc-builds/mine?pageIndex=999&pageSize=20", JsonOptions);
        pageFar.Should().NotBeNull();
        pageFar!.Items.Should().BeEmpty();
        pageFar.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task PostgreSQL_UniqueItemConstraint_ShouldPreventDuplicateProductsOnSameBuild()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (_, user) = await CreateUserClientAsync(UserRole.Customer);
        var (_, product) = await SeedProductAsync(ComponentType.Cpu);

        var build = new PCBuild("Duplicate Test", user.Id);
        build.AddItem(new PCBuildItem(build.Id, product.Id, ComponentType.Cpu, product.Price, 1));
        db.PCBuilds.Add(build);
        await db.SaveChangesAsync();

        // Attempt to insert duplicate (PCBuildId, ProductId)
        var duplicateItem = new PCBuildItem(build.Id, product.Id, ComponentType.Cpu, product.Price, 1);
        db.PCBuildItems.Add(duplicateItem);

        var act = async () => await db.SaveChangesAsync();
        var ex = await act.Should().ThrowAsync<DbUpdateException>();
        db.IsUniqueViolation(ex.Which).Should().BeTrue();
    }

    [Fact]
    public async Task PostgreSQL_CheckConstraints_ShouldRejectInvalidQuantityAndNegativeUnitPrice()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (_, user) = await CreateUserClientAsync(UserRole.Customer);
        var (_, product) = await SeedProductAsync(ComponentType.Cpu);

        var build = new PCBuild("Check Constraint Test", user.Id);
        db.PCBuilds.Add(build);
        await db.SaveChangesAsync();

        // Check constraint 1: Quantity > 0
        var actQty = async () =>
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO \"PCBuildItems\" (\"Id\", \"PCBuildId\", \"ProductId\", \"ComponentType\", \"Quantity\", \"UnitPrice\", \"CreatedAt\") VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})",
                Guid.NewGuid(), build.Id, product.Id, (int)ComponentType.Cpu, 0, 100m, DateTime.UtcNow);
        };
        await actQty.Should().ThrowAsync<Npgsql.PostgresException>();

        // Check constraint 2: UnitPrice >= 0
        var actPrice = async () =>
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO \"PCBuildItems\" (\"Id\", \"PCBuildId\", \"ProductId\", \"ComponentType\", \"Quantity\", \"UnitPrice\", \"CreatedAt\") VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})",
                Guid.NewGuid(), build.Id, product.Id, (int)ComponentType.Cpu, 1, -10m, DateTime.UtcNow);
        };
        await actPrice.Should().ThrowAsync<Npgsql.PostgresException>();
    }

    [Fact]
    public async Task PostgreSQL_ConcurrencyToken_ShouldDetectConcurrentMutation()
    {
        var (_, user) = await CreateUserClientAsync(UserRole.Customer);
        var (_, cpu) = await SeedProductAsync(ComponentType.Cpu);

        Guid buildId;
        using (var setupScope = _fixture.Factory.Services.CreateScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var build = new PCBuild("Initial Build", user.Id);
            build.AddItem(new PCBuildItem(build.Id, cpu.Id, ComponentType.Cpu, cpu.Price, 1));
            db.PCBuilds.Add(build);
            await db.SaveChangesAsync();
            buildId = build.Id;
        }

        // Two independent scopes loading the exact same build at the same version
        using var scope1 = _fixture.Factory.Services.CreateScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        using var scope2 = _fixture.Factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var build1 = await db1.PCBuilds.FindAsync(buildId);
        var build2 = await db2.PCBuilds.FindAsync(buildId);

        build1.Should().NotBeNull();
        build2.Should().NotBeNull();
        build1!.UpdatedAt.Should().Be(build2!.UpdatedAt);

        // Mutate both in memory
        build1.UpdateDetails("Name by User 1", "Desc 1");
        build2.UpdateDetails("Name by User 2", "Desc 2");

        // Save first context - succeeds and updates UpdatedAt concurrency token in PostgreSQL
        await db1.SaveChangesAsync();

        // Save second context - must throw DbUpdateConcurrencyException because UpdatedAt in DB changed
        var save2Act = async () => await db2.SaveChangesAsync();
        await save2Act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task DeleteCascade_ShouldDeleteItems_ButKeepProducts()
    {
        var (client, user) = await CreateUserClientAsync(UserRole.Customer);
        var (_, cpu) = await SeedProductAsync(ComponentType.Cpu);

        var createResponse = await client.PostAsJsonAsync("/api/v1/pc-builds", new CreatePCBuildRequest("Cascade Test", null, [new(cpu.Id, 1)]));
        var created = await createResponse.Content.ReadFromJsonAsync<MyPCBuildDto>(JsonOptions);
        var buildId = created!.Id;

        // Delete build
        var deleteResponse = await client.DeleteAsync($"/api/v1/pc-builds/{buildId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify in PostgreSQL
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Build is gone
        (await db.PCBuilds.AnyAsync(b => b.Id == buildId)).Should().BeFalse();

        // PCBuildItems are gone
        (await db.PCBuildItems.AnyAsync(i => i.PCBuildId == buildId)).Should().BeFalse();

        // Product is STILL in database
        (await db.Products.AnyAsync(p => p.Id == cpu.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Delete_WhenBuildReferencedByOrderItem_ShouldReturn409AndPreserveBuild()
    {
        var (client, customer) = await CreateUserClientAsync(UserRole.Customer);
        var (_, cpu) = await SeedProductAsync(ComponentType.Cpu);

        var createResponse = await client.PostAsJsonAsync("/api/v1/pc-builds", new CreatePCBuildRequest("Ordered Build", null, [new(cpu.Id, 1)]));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<MyPCBuildDto>(JsonOptions);
        var buildId = created!.Id;

        // Create Order and OrderItem referencing buildId in database
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var order = new Order("ORD-INT-001", customer.Id, "idem-int-1", customer.FullName, customer.Email, "0900000000", "Shipping Address", PaymentMethod.COD);
            db.Orders.Add(order);
            var orderItem = new OrderItem(order.Id, cpu.Id, cpu.Name, cpu.Sku, cpu.Price, 1, pcBuildId: buildId);
            db.OrderItems.Add(orderItem);
            await db.SaveChangesAsync();
        }

        // Attempt delete
        var deleteResponse = await client.DeleteAsync($"/api/v1/pc-builds/{buildId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var error = await deleteResponse.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
        error.Should().NotBeNull();
        error!.Code.Should().Be("PCBuild.CannotDeleteOrderedBuild");

        // Verify in PostgreSQL that build still exists
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.PCBuilds.AnyAsync(b => b.Id == buildId)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task GetMine_WhenExtremePageIndex_ShouldReturnEmptyListSafely()
    {
        var (client, _) = await CreateUserClientAsync(UserRole.Customer);
        var (_, cpu) = await SeedProductAsync(ComponentType.Cpu);

        await client.PostAsJsonAsync("/api/v1/pc-builds", new CreatePCBuildRequest("Rig 1", null, [new(cpu.Id, 1)]));

        // Int Max page index must return empty without overflow
        var response = await client.GetAsync($"/api/v1/pc-builds/mine?pageIndex={int.MaxValue}&pageSize=20");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<MyPCBuildSummaryDto>>(JsonOptions);
        result.Should().NotBeNull();
        result!.TotalCount.Should().Be(1);
        result.Items.Should().BeEmpty();
    }
}
