using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RiuTek.API.Contracts;
using RiuTek.API.IntegrationTest.Infrastructure;
using RiuTek.Application.Common.Models;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Data;
using Xunit;

namespace RiuTek.API.IntegrationTest.Http;

[Collection(IntegrationTestCollection.Name)]
public class ProductStockAuditIntegrationTests : CatalogIntegrationTestBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ProductStockAuditIntegrationTests(PostgreSqlContainerFixture fixture)
        : base(fixture)
    {
    }

    private async Task<(CategoryDto Category, ProductDto Product)> SeedCategoryAndProductAsync(
        HttpClient adminClient,
        int initialStock = 10)
    {
        var categorySuffix = Guid.NewGuid().ToString("N")[..8];
        using var catResp = await adminClient.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(
            Name: $"CPU Cat {categorySuffix}",
            ComponentType: ComponentType.Cpu,
            Description: "Test category",
            ParentId: null
        ));
        catResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var category = (await catResp.Content.ReadFromJsonAsync<CategoryDto>(JsonOptions))!;

        var productSuffix = Guid.NewGuid().ToString("N")[..8];
        using var prodResp = await adminClient.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            CategoryId: category.Id,
            Name: $"Intel Core i7 {productSuffix}",
            Sku: $"SKU-CPU-{productSuffix.ToUpperInvariant()}",
            Brand: "Intel",
            Price: 8000000m,
            OriginalPrice: 9000000m,
            StockQuantity: initialStock,
            ImageUrl: "https://example.com/cpu.jpg",
            AdditionalImages: null,
            ComponentType: ComponentType.Cpu,
            Specifications: new CpuSpecification
            {
                Socket = CpuSocket.LGA1700,
                CoreCount = 16,
                ThreadCount = 24,
                BaseClockGhz = 3.4,
                BoostClockGhz = 5.4,
                TdpWattage = 125,
                HasIntegratedGpu = true,
                SupportedMemoryType = RamType.DDR5,
                MaxMemorySpeedMhz = 5600
            }
        ));
        prodResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var product = (await prodResp.Content.ReadFromJsonAsync<ProductDto>(JsonOptions))!;

        return (category, product);
    }

    [Fact]
    public async Task AdjustStock_HappyPath_UpdatesStockAndPersistsAudit()
    {
        using var adminClient = CreateAdminClient();
        using var staffClient = CreateStaffClient();
        var (_, product) = await SeedCategoryAndProductAsync(adminClient, initialStock: 10);

        // 1. Staff increases stock by 5
        var request1 = new AdjustStockRequest(
            ExpectedStockQuantity: 10,
            QuantityDelta: 5,
            Note: "Nhập thêm hàng từ nhà phân phối"
        );

        using var response1 = await staffClient.PostAsJsonAsync(
            $"/api/v1/products/{product.Id}/stock-adjustments", request1);

        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        var result1 = await response1.Content.ReadFromJsonAsync<StockAdjustmentResultDto>(JsonOptions);
        result1.Should().NotBeNull();
        result1!.ProductId.Should().Be(product.Id);
        result1.Sku.Should().Be(product.Sku);
        result1.StockBefore.Should().Be(10);
        result1.QuantityDelta.Should().Be(5);
        result1.StockAfter.Should().Be(15);
        result1.Reason.Should().Be(StockMovementReason.ManualAdjustment);
        result1.Note.Should().Be("Nhập thêm hàng từ nhà phân phối");

        // 2. Staff decreases stock by 3
        var request2 = new AdjustStockRequest(
            ExpectedStockQuantity: 15,
            QuantityDelta: -3,
            Note: "Xuất hủy hàng lỗi bao bì"
        );

        using var response2 = await staffClient.PostAsJsonAsync(
            $"/api/v1/products/{product.Id}/stock-adjustments", request2);

        response2.StatusCode.Should().Be(HttpStatusCode.OK);
        var result2 = await response2.Content.ReadFromJsonAsync<StockAdjustmentResultDto>(JsonOptions);
        result2.Should().NotBeNull();
        result2!.StockBefore.Should().Be(15);
        result2.QuantityDelta.Should().Be(-3);
        result2.StockAfter.Should().Be(12);

        // 3. Verify database state directly
        using var scope = Fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var dbProduct = await db.Products.FindAsync(product.Id);
        dbProduct!.StockQuantity.Should().Be(12);

        var movements = await db.StockMovements
            .Where(m => m.ProductId == product.Id)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        movements.Should().HaveCount(3);
        // Movement 1: ProductCreated
        movements[0].Reason.Should().Be(StockMovementReason.ProductCreated);
        movements[0].QuantityDelta.Should().Be(10);
        movements[0].StockBefore.Should().Be(0);
        movements[0].StockAfter.Should().Be(10);

        // Movement 2: ManualAdjustment +5
        movements[1].Reason.Should().Be(StockMovementReason.ManualAdjustment);
        movements[1].QuantityDelta.Should().Be(5);
        movements[1].StockBefore.Should().Be(10);
        movements[1].StockAfter.Should().Be(15);

        // Movement 3: ManualAdjustment -3
        movements[2].Reason.Should().Be(StockMovementReason.ManualAdjustment);
        movements[2].QuantityDelta.Should().Be(-3);
        movements[2].StockBefore.Should().Be(15);
        movements[2].StockAfter.Should().Be(12);
    }

    [Fact]
    public async Task AdjustStock_WhenExpectedStockMismatches_Returns409Conflict()
    {
        using var adminClient = CreateAdminClient();
        using var staffClient = CreateStaffClient();
        var (_, product) = await SeedCategoryAndProductAsync(adminClient, initialStock: 10);

        var request = new AdjustStockRequest(
            ExpectedStockQuantity: 8, // Actual is 10 -> Conflict!
            QuantityDelta: 5,
            Note: "Thử điều chỉnh khi lệch expected"
        );

        using var response = await staffClient.PostAsJsonAsync(
            $"/api/v1/products/{product.Id}/stock-adjustments", request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = await response.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
        error.Should().NotBeNull();
        error!.Code.Should().Be("Inventory.StockConflict");

        // Verify stock untouched
        using var scope = Fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var dbProduct = await db.Products.FindAsync(product.Id);
        dbProduct!.StockQuantity.Should().Be(10);
    }

    [Fact]
    public async Task AdjustStock_WhenInsufficientStock_Returns409Conflict()
    {
        using var adminClient = CreateAdminClient();
        using var staffClient = CreateStaffClient();
        var (_, product) = await SeedCategoryAndProductAsync(adminClient, initialStock: 5);

        var request = new AdjustStockRequest(
            ExpectedStockQuantity: 5,
            QuantityDelta: -10, // Stock insufficient!
            Note: "Giảm vượt quá tồn kho hiện có"
        );

        using var response = await staffClient.PostAsJsonAsync(
            $"/api/v1/products/{product.Id}/stock-adjustments", request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = await response.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
        error.Should().NotBeNull();
        error!.Code.Should().Be("Inventory.InsufficientStock");
    }

    [Fact]
    public async Task AdjustStock_Authorization_EnforcesPrivileges()
    {
        using var adminClient = CreateAdminClient();
        var (_, product) = await SeedCategoryAndProductAsync(adminClient, initialStock: 10);

        var request = new AdjustStockRequest(
            ExpectedStockQuantity: 10,
            QuantityDelta: 2,
            Note: "Kiểm tra phân quyền"
        );

        // Anonymous client -> 401
        using var publicClient = CreatePublicClient();
        using var anonResp = await publicClient.PostAsJsonAsync(
            $"/api/v1/products/{product.Id}/stock-adjustments", request);
        anonResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Customer client -> 403
        using var customerClient = IntegrationTestAuth.CreateClientForRole(Fixture.Factory, UserRole.Customer);
        using var custResp = await customerClient.PostAsJsonAsync(
            $"/api/v1/products/{product.Id}/stock-adjustments", request);
        custResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetStockMovements_HappyPath_ReturnsPagedAuditsInDescendingOrder()
    {
        using var adminClient = CreateAdminClient();
        using var staffClient = CreateStaffClient();
        var (_, product) = await SeedCategoryAndProductAsync(adminClient, initialStock: 10);

        // Add 2 manual adjustments
        await staffClient.PostAsJsonAsync($"/api/v1/products/{product.Id}/stock-adjustments", new AdjustStockRequest(
            ExpectedStockQuantity: 10,
            QuantityDelta: 5,
            Note: "Adjustment 1"
        ));

        await staffClient.PostAsJsonAsync($"/api/v1/products/{product.Id}/stock-adjustments", new AdjustStockRequest(
            ExpectedStockQuantity: 15,
            QuantityDelta: -2,
            Note: "Adjustment 2"
        ));

        // Query all movements
        using var response = await staffClient.GetAsync(
            $"/api/v1/products/{product.Id}/stock-movements?pageIndex=1&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var pagedResult = await response.Content.ReadFromJsonAsync<PagedResult<StockMovementDto>>(JsonOptions);
        pagedResult.Should().NotBeNull();
        pagedResult!.TotalCount.Should().Be(3);
        pagedResult.Items.Should().HaveCount(3);

        // First item is newest (Adjustment 2)
        pagedResult.Items[0].Reason.Should().Be(StockMovementReason.ManualAdjustment);
        pagedResult.Items[0].QuantityDelta.Should().Be(-2);
        pagedResult.Items[0].StockBefore.Should().Be(15);
        pagedResult.Items[0].StockAfter.Should().Be(13);
        pagedResult.Items[0].Note.Should().Be("Adjustment 2");

        // Second item is Adjustment 1
        pagedResult.Items[1].Reason.Should().Be(StockMovementReason.ManualAdjustment);
        pagedResult.Items[1].QuantityDelta.Should().Be(5);
        pagedResult.Items[1].StockBefore.Should().Be(10);
        pagedResult.Items[1].StockAfter.Should().Be(15);

        // Third item is ProductCreated
        pagedResult.Items[2].Reason.Should().Be(StockMovementReason.ProductCreated);
        pagedResult.Items[2].QuantityDelta.Should().Be(10);

        // Filter by Reason = ProductCreated
        using var filterResp = await staffClient.GetAsync(
            $"/api/v1/products/{product.Id}/stock-movements?reason=ProductCreated");

        filterResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var filteredResult = await filterResp.Content.ReadFromJsonAsync<PagedResult<StockMovementDto>>(JsonOptions);
        filteredResult.Should().NotBeNull();
        filteredResult!.TotalCount.Should().Be(1);
        filteredResult.Items.Should().ContainSingle();
        filteredResult.Items[0].Reason.Should().Be(StockMovementReason.ProductCreated);
    }

    [Fact]
    public async Task GetStockMovements_Authorization_EnforcesPrivileges()
    {
        using var adminClient = CreateAdminClient();
        var (_, product) = await SeedCategoryAndProductAsync(adminClient, initialStock: 5);

        // Anonymous client -> 401
        using var publicClient = CreatePublicClient();
        using var anonResp = await publicClient.GetAsync($"/api/v1/products/{product.Id}/stock-movements");
        anonResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Customer client -> 403
        using var customerClient = IntegrationTestAuth.CreateClientForRole(Fixture.Factory, UserRole.Customer);
        using var custResp = await customerClient.GetAsync($"/api/v1/products/{product.Id}/stock-movements");
        custResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateProduct_DoesNotMutateStockQuantityOrAddMovement()
    {
        using var adminClient = CreateAdminClient();
        using var staffClient = CreateStaffClient();
        var (category, product) = await SeedCategoryAndProductAsync(adminClient, initialStock: 20);

        // Staff updates name, price, active status (UpdateProductRequest does not contain StockQuantity)
        var updateRequest = new UpdateProductRequest(
            CategoryId: category.Id,
            Name: "Updated Name After Price Cut",
            Sku: product.Sku,
            Brand: product.Brand,
            Price: 7500000m,
            OriginalPrice: 8500000m,
            IsActive: true,
            ImageUrl: product.ImageUrl,
            AdditionalImages: null,
            ComponentType: product.ComponentType,
            Specifications: product.Specifications
        );

        using var updateResp = await staffClient.PutAsJsonAsync(
            $"/api/v1/products/{product.Id}", updateRequest);

        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedProduct = await updateResp.Content.ReadFromJsonAsync<ProductDto>(JsonOptions);
        updatedProduct.Should().NotBeNull();
        updatedProduct!.StockQuantity.Should().Be(20, "Stock quantity must remain unchanged after product update");

        // Verify no extra movements added
        using var scope = Fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var movements = await db.StockMovements.Where(m => m.ProductId == product.Id).ToListAsync();
        movements.Should().ContainSingle();
        movements[0].Reason.Should().Be(StockMovementReason.ProductCreated);
    }

    private record BusinessErrorResponse(string Code, string Description);
}
