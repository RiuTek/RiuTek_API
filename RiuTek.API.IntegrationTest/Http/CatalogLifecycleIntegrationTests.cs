using System.Data;
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
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Data;
using Xunit;

namespace RiuTek.API.IntegrationTest.Http;

[Collection(IntegrationTestCollection.Name)]
public class CatalogLifecycleIntegrationTests : CatalogIntegrationTestBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CatalogLifecycleIntegrationTests(PostgreSqlContainerFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Category_HappyLifecycle_EndToEnd_Succeeds()
    {
        var adminClient = CreateAdminClient();
        var staffClient = CreateStaffClient();
        var publicClient = CreatePublicClient();

        // 1. Admin creates root category with outer whitespace in Vietnamese
        var createRootRequest = new CreateCategoryRequest(
            Name: "  Bộ vi xử lý  ",
            ComponentType: ComponentType.Cpu,
            Description: "  Danh mục vi xử lý CPU chính hãng  ",
            ParentId: null
        );

        var createRootResponse = await adminClient.PostAsJsonAsync("/api/v1/categories", createRootRequest);

        // 2. Assert 201 Created and category contract normalization
        createRootResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var rootDto = await createRootResponse.Content.ReadFromJsonAsync<CategoryDto>(JsonOptions);
        rootDto.Should().NotBeNull();
        rootDto!.Id.Should().NotBeEmpty();
        rootDto.Name.Should().Be("Bộ vi xử lý");
        rootDto.Slug.Should().Be("bo-vi-xu-ly");
        rootDto.Description.Should().Be("Danh mục vi xử lý CPU chính hãng");
        rootDto.ParentId.Should().BeNull();
        rootDto.ComponentType.Should().Be(ComponentType.Cpu);
        rootDto.SubCategories.Should().BeEmpty();

        createRootResponse.Headers.Location.Should().NotBeNull();
        createRootResponse.Headers.Location!.ToString().Should().Contain($"/api/v1/Categories/{rootDto.Id}",
            "Location header must point to GetById route for the created root category");

        // 3. Staff creates child category with same ComponentType and ParentId = root.Id
        var createChildRequest = new CreateCategoryRequest(
            Name: "  CPU Đồ Họa  ",
            ComponentType: ComponentType.Cpu,
            Description: "  CPU hiệu năng cao cho render  ",
            ParentId: rootDto.Id
        );

        var createChildResponse = await staffClient.PostAsJsonAsync("/api/v1/categories", createChildRequest);

        // 4. Assert child 201 Created and proper parent relationship
        createChildResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var childDto = await createChildResponse.Content.ReadFromJsonAsync<CategoryDto>(JsonOptions);
        childDto.Should().NotBeNull();
        childDto!.Id.Should().NotBeEmpty();
        childDto.ParentId.Should().Be(rootDto.Id);
        childDto.Name.Should().Be("CPU Đồ Họa");
        childDto.Slug.Should().Be("cpu-do-hoa");
        childDto.Description.Should().Be("CPU hiệu năng cao cho render");
        childDto.ComponentType.Should().Be(ComponentType.Cpu);

        // 5. Guest/public GET child category by Id
        var getChildResponse = await publicClient.GetAsync($"/api/v1/categories/{childDto.Id}");
        getChildResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetchedChild = await getChildResponse.Content.ReadFromJsonAsync<CategoryDto>(JsonOptions);
        fetchedChild.Should().NotBeNull();
        fetchedChild!.Id.Should().Be(childDto.Id);
        fetchedChild.ParentId.Should().Be(rootDto.Id);
        fetchedChild.Name.Should().Be("CPU Đồ Họa");
        fetchedChild.Slug.Should().Be("cpu-do-hoa");

        // 6. Guest/public GET category tree
        var getTreeResponse = await publicClient.GetAsync("/api/v1/categories");
        getTreeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var tree = await getTreeResponse.Content.ReadFromJsonAsync<List<CategoryDto>>(JsonOptions);
        tree.Should().NotBeNull();

        var rootInTree = tree!.FirstOrDefault(c => c.Id == rootDto.Id);
        rootInTree.Should().NotBeNull("Root category must exist at the top level of the category tree");
        rootInTree!.Slug.Should().Be("bo-vi-xu-ly");
        rootInTree.ComponentType.Should().Be(ComponentType.Cpu);
        rootInTree.SubCategories.Should().ContainSingle(c => c.Id == childDto.Id);

        var childInTree = rootInTree.SubCategories.Single(c => c.Id == childDto.Id);
        childInTree.ParentId.Should().Be(rootDto.Id);
        childInTree.Slug.Should().Be("cpu-do-hoa");
        childInTree.ComponentType.Should().Be(ComponentType.Cpu);

        // 7. Staff updates child category
        var updateChildRequest = new UpdateCategoryRequest(
            Name: "  CPU Cao Cấp  ",
            ComponentType: ComponentType.Cpu,
            Description: "  CPU cao cấp dành cho workstation  ",
            ParentId: rootDto.Id
        );

        var updateChildResponse = await staffClient.PutAsJsonAsync($"/api/v1/categories/{childDto.Id}", updateChildRequest);

        // 8. Assert update response: 200 OK, normalized slug and retained IDs
        updateChildResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedChildDto = await updateChildResponse.Content.ReadFromJsonAsync<CategoryDto>(JsonOptions);
        updatedChildDto.Should().NotBeNull();
        updatedChildDto!.Id.Should().Be(childDto.Id);
        updatedChildDto.ParentId.Should().Be(rootDto.Id);
        updatedChildDto.Name.Should().Be("CPU Cao Cấp");
        updatedChildDto.Description.Should().Be("CPU cao cấp dành cho workstation");
        updatedChildDto.Slug.Should().Be("cpu-cao-cap");
        updatedChildDto.ComponentType.Should().Be(ComponentType.Cpu);

        // 9. Public GET updated child to prove persistence across requests
        var getUpdatedChildResponse = await publicClient.GetAsync($"/api/v1/categories/{childDto.Id}");
        getUpdatedChildResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var persistedChild = await getUpdatedChildResponse.Content.ReadFromJsonAsync<CategoryDto>(JsonOptions);
        persistedChild.Should().NotBeNull();
        persistedChild!.Slug.Should().Be("cpu-cao-cap");
        persistedChild.Name.Should().Be("CPU Cao Cấp");

        // 10. Admin DELETE child category -> 204 NoContent, empty body
        var deleteChildResponse = await adminClient.DeleteAsync($"/api/v1/categories/{childDto.Id}");
        deleteChildResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var childDeleteBody = await deleteChildResponse.Content.ReadAsStringAsync();
        childDeleteBody.Should().BeEmpty();

        // 11. Public GET deleted child -> 404 with exact Category.NotFound error
        var getDeletedChildResponse = await publicClient.GetAsync($"/api/v1/categories/{childDto.Id}");
        getDeletedChildResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var childError = await getDeletedChildResponse.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
        childError.Should().NotBeNull();
        childError!.Code.Should().Be("Category.NotFound");

        // 12. Admin DELETE root category -> 204 NoContent; public GET root -> 404 Category.NotFound
        var deleteRootResponse = await adminClient.DeleteAsync($"/api/v1/categories/{rootDto.Id}");
        deleteRootResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var rootDeleteBody = await deleteRootResponse.Content.ReadAsStringAsync();
        rootDeleteBody.Should().BeEmpty();

        var getDeletedRootResponse = await publicClient.GetAsync($"/api/v1/categories/{rootDto.Id}");
        getDeletedRootResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var rootError = await getDeletedRootResponse.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
        rootError.Should().NotBeNull();
        rootError!.Code.Should().Be("Category.NotFound");

        // 13. Fresh DbContext verification: ensure both category rows are deleted from PostgreSQL
        using var scope = Fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var remainingCategories = await dbContext.Categories
            .AsNoTracking()
            .Where(c => c.Id == rootDto.Id || c.Id == childDto.Id)
            .ToListAsync();
        remainingCategories.Should().BeEmpty("Both root and child categories must be completely deleted from PostgreSQL");
    }

    [Fact]
    public async Task Product_HappyLifecycle_EndToEnd_AndJsonbPersistence_Succeeds()
    {
        var adminClient = CreateAdminClient();
        var staffClient = CreateStaffClient();
        var publicClient = CreatePublicClient();

        // 6.1 Create: First create a valid CPU category
        var catResponse = await adminClient.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(
            Name: "CPU Bộ Vi Xử Lý",
            ComponentType: ComponentType.Cpu,
            Description: "Danh mục cho CPU",
            ParentId: null
        ));
        catResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var category = (await catResponse.Content.ReadFromJsonAsync<CategoryDto>(JsonOptions))!;

        // Admin POST Product with outer whitespace, deduplicated casing images, and CpuSpecification
        var initialSpec = new CpuSpecification
        {
            Socket = CpuSocket.LGA1700,
            CoreCount = 20,
            ThreadCount = 28,
            BaseClockGhz = 3.4,
            BoostClockGhz = 5.6,
            TdpWattage = 125,
            HasIntegratedGpu = true,
            SupportedMemoryType = RamType.DDR5,
            MaxMemorySpeedMhz = 5600
        };

        var createProductRequest = new CreateProductRequest(
            CategoryId: category.Id,
            Name: "  Intel Core i7-14700K Gaming Processor  ",
            Sku: "  sku-cpu-14700k-test  ",
            Brand: "  Intel  ",
            Price: 10500000m,
            OriginalPrice: 12000000m,
            StockQuantity: 25,
            ImageUrl: "  https://example.com/images/14700k.jpg  ",
            AdditionalImages: new List<string>
            {
                "  https://example.com/images/14700k-front.jpg  ",
                "https://example.com/images/14700K-FRONT.JPG", // Case duplicate to verify dedup
                "https://example.com/images/14700k-box.jpg"
            },
            ComponentType: ComponentType.Cpu,
            Specifications: initialSpec
        );

        var createProductResponse = await adminClient.PostAsJsonAsync("/api/v1/products", createProductRequest);

        // Assert 201 Created and normalized fields
        createProductResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdProduct = await createProductResponse.Content.ReadFromJsonAsync<ProductDto>(JsonOptions);
        createdProduct.Should().NotBeNull();
        createdProduct!.Id.Should().NotBeEmpty();
        createdProduct.CategoryId.Should().Be(category.Id);
        createdProduct.CategoryName.Should().Be("CPU Bộ Vi Xử Lý");
        createdProduct.Name.Should().Be("Intel Core i7-14700K Gaming Processor");
        createdProduct.Brand.Should().Be("Intel");
        createdProduct.ImageUrl.Should().Be("https://example.com/images/14700k.jpg");
        createdProduct.Sku.Should().Be("SKU-CPU-14700K-TEST");
        createdProduct.Slug.Should().Be("intel-core-i7-14700k-gaming-processor");
        createdProduct.IsActive.Should().BeTrue("Products must default to IsActive = true on creation");
        createdProduct.Price.Should().Be(10500000m);
        createdProduct.OriginalPrice.Should().Be(12000000m);
        createdProduct.StockQuantity.Should().Be(25);

        // AdditionalImages deduped and trimmed: 2 items, preserving first occurrence casing
        createdProduct.AdditionalImages.Should().HaveCount(2);
        createdProduct.AdditionalImages[0].Should().Be("https://example.com/images/14700k-front.jpg");
        createdProduct.AdditionalImages[1].Should().Be("https://example.com/images/14700k-box.jpg");

        // Polymorphic specification verification
        createdProduct.Specifications.Should().BeOfType<CpuSpecification>();
        var returnedCpuSpec = (CpuSpecification)createdProduct.Specifications;
        returnedCpuSpec.Socket.Should().Be(CpuSocket.LGA1700);
        returnedCpuSpec.CoreCount.Should().Be(20);
        returnedCpuSpec.ThreadCount.Should().Be(28);
        returnedCpuSpec.BaseClockGhz.Should().Be(3.4);
        returnedCpuSpec.BoostClockGhz.Should().Be(5.6);
        returnedCpuSpec.TdpWattage.Should().Be(125);
        returnedCpuSpec.HasIntegratedGpu.Should().BeTrue();
        returnedCpuSpec.SupportedMemoryType.Should().Be(RamType.DDR5);
        returnedCpuSpec.MaxMemorySpeedMhz.Should().Be(5600);

        createProductResponse.Headers.Location.Should().NotBeNull();
        createProductResponse.Headers.Location!.ToString().Should().Contain($"/api/v1/Products/{createdProduct.Id}");

        // 6.2 Read: Guest by slug & Admin by Id
        var guestBySlugResponse = await publicClient.GetAsync($"/api/v1/products/slug/{createdProduct.Slug}");
        guestBySlugResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var guestProduct = await guestBySlugResponse.Content.ReadFromJsonAsync<ProductDto>(JsonOptions);
        guestProduct.Should().NotBeNull();
        guestProduct!.Id.Should().Be(createdProduct.Id);
        guestProduct.Sku.Should().Be(createdProduct.Sku);
        guestProduct.Slug.Should().Be(createdProduct.Slug);

        var adminByIdResponse = await adminClient.GetAsync($"/api/v1/products/{createdProduct.Id}");
        adminByIdResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var adminProduct = await adminByIdResponse.Content.ReadFromJsonAsync<ProductDto>(JsonOptions);
        adminProduct.Should().NotBeNull();
        adminProduct!.Id.Should().Be(createdProduct.Id);
        adminProduct.Sku.Should().Be(createdProduct.Sku);
        adminProduct.Specifications.Should().BeOfType<CpuSpecification>();

        // 6.3 Update and Deactivate: Staff PUT product with IsActive = false and modified spec
        var updatedSpec = new CpuSpecification
        {
            Socket = CpuSocket.LGA1700,
            CoreCount = 20,
            ThreadCount = 28,
            BaseClockGhz = 3.5, // Changed
            BoostClockGhz = 5.7, // Changed
            TdpWattage = 125,
            HasIntegratedGpu = false, // Changed
            SupportedMemoryType = RamType.DDR5,
            MaxMemorySpeedMhz = 5600
        };

        var updateProductRequest = new UpdateProductRequest(
            CategoryId: category.Id,
            Name: "  Intel Core i7-14700KF Special Edition  ",
            Sku: "  sku-cpu-14700kf-special  ",
            Brand: "  Intel Core  ",
            Price: 11200000m,
            OriginalPrice: 12500000m,
            StockQuantity: 15,
            IsActive: false,
            ImageUrl: "  https://example.com/images/14700kf-special.jpg  ",
            AdditionalImages: new List<string> { "https://example.com/images/special-1.jpg" },
            ComponentType: ComponentType.Cpu,
            Specifications: updatedSpec
        );

        var updateProductResponse = await staffClient.PutAsJsonAsync($"/api/v1/products/{createdProduct.Id}", updateProductRequest);
        updateProductResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedProduct = await updateProductResponse.Content.ReadFromJsonAsync<ProductDto>(JsonOptions);
        updatedProduct.Should().NotBeNull();
        updatedProduct!.Name.Should().Be("Intel Core i7-14700KF Special Edition");
        updatedProduct.Sku.Should().Be("SKU-CPU-14700KF-SPECIAL");
        updatedProduct.Brand.Should().Be("Intel Core");
        updatedProduct.Slug.Should().Be("intel-core-i7-14700kf-special-edition");
        updatedProduct.Price.Should().Be(11200000m);
        updatedProduct.OriginalPrice.Should().Be(12500000m);
        updatedProduct.StockQuantity.Should().Be(15);
        updatedProduct.IsActive.Should().BeFalse();
        updatedProduct.AdditionalImages.Should().ContainSingle("https://example.com/images/special-1.jpg");

        var updatedCpuSpec = (CpuSpecification)updatedProduct.Specifications;
        updatedCpuSpec.BaseClockGhz.Should().Be(3.5);
        updatedCpuSpec.BoostClockGhz.Should().Be(5.7);
        updatedCpuSpec.HasIntegratedGpu.Should().BeFalse();

        // Old slug should now return 404 Product.NotFound
        var getOldSlugResponse = await publicClient.GetAsync($"/api/v1/products/slug/{createdProduct.Slug}");
        getOldSlugResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var oldSlugError = await getOldSlugResponse.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
        oldSlugError.Should().NotBeNull();
        oldSlugError!.Code.Should().Be("Product.NotFound");

        // New slug returns 200 with IsActive = false
        var getNewSlugResponse = await publicClient.GetAsync($"/api/v1/products/slug/{updatedProduct.Slug}");
        getNewSlugResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var newSlugProduct = await getNewSlugResponse.Content.ReadFromJsonAsync<ProductDto>(JsonOptions);
        newSlugProduct.Should().NotBeNull();
        newSlugProduct!.IsActive.Should().BeFalse();

        // Visibility assertion 1: Default public list contains inactive product (IsActive is business state, not visibility)
        var defaultListResponse = await publicClient.GetAsync("/api/v1/products");
        defaultListResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var defaultPagedResult = await defaultListResponse.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions);
        defaultPagedResult.Should().NotBeNull();
        defaultPagedResult!.Items.Should().Contain(p => p.Id == createdProduct.Id && !p.IsActive);

        // Visibility assertion 2: Filter isActive=false contains inactive product
        var inactiveListResponse = await publicClient.GetAsync("/api/v1/products?isActive=false");
        inactiveListResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var inactivePagedResult = await inactiveListResponse.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions);
        inactivePagedResult.Should().NotBeNull();
        inactivePagedResult!.Items.Should().Contain(p => p.Id == createdProduct.Id);

        // Visibility assertion 3: Filter isActive=true does NOT contain inactive product
        var activeListResponse = await publicClient.GetAsync("/api/v1/products?isActive=true");
        activeListResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var activePagedResult = await activeListResponse.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions);
        activePagedResult.Should().NotBeNull();
        activePagedResult!.Items.Should().NotContain(p => p.Id == createdProduct.Id);

        // 6.4 PostgreSQL persistence in fresh DbContext
        using var scope = Fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var productInDb = await dbContext.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == createdProduct.Id);
        productInDb.Should().NotBeNull();
        productInDb!.Name.Should().Be("Intel Core i7-14700KF Special Edition");
        productInDb.Sku.Should().Be("SKU-CPU-14700KF-SPECIAL");
        productInDb.CategoryId.Should().Be(category.Id);
        productInDb.IsActive.Should().BeFalse();
        productInDb.UpdatedAt.Should().NotBeNull();
        productInDb.AdditionalImages.Should().ContainSingle("https://example.com/images/special-1.jpg");

        // EF Deserialization of polymorphic JSONB column
        productInDb.Specifications.Should().BeOfType<CpuSpecification>();
        var dbCpuSpec = (CpuSpecification)productInDb.Specifications;
        dbCpuSpec.BaseClockGhz.Should().Be(3.5);
        dbCpuSpec.BoostClockGhz.Should().Be(5.7);
        dbCpuSpec.HasIntegratedGpu.Should().BeFalse();

        // Vector embedding remains untouched (null)
        productInDb.Embedding.Should().BeNull();

        // Direct parameterized ADO.NET query to verify raw jsonb representation
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT \"Specifications\"::text FROM \"Products\" WHERE \"Id\" = @id";
        var param = cmd.CreateParameter();
        param.ParameterName = "@id";
        param.Value = createdProduct.Id;
        cmd.Parameters.Add(param);

        var rawJson = (string)(await cmd.ExecuteScalarAsync())!;
        rawJson.Should().NotBeNullOrWhiteSpace();

        using var jsonDoc = JsonDocument.Parse(rawJson);
        var root = jsonDoc.RootElement;
        GetPropertyCaseInsensitive(root, "$type").GetString().Should().Be("cpu",
            "Polymorphic jsonb column in PostgreSQL must contain $type discriminator 'cpu'");
        GetPropertyCaseInsensitive(root, "BaseClockGhz").GetDouble().Should().Be(3.5);
        GetPropertyCaseInsensitive(root, "BoostClockGhz").GetDouble().Should().Be(5.7);
        GetPropertyCaseInsensitive(root, "HasIntegratedGpu").GetBoolean().Should().BeFalse();
    }

    private static JsonElement GetPropertyCaseInsensitive(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var directProp))
        {
            return directProp;
        }

        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return prop.Value;
            }
        }

        throw new KeyNotFoundException($"Property '{propertyName}' was not found in JSON element.");
    }

    private record BusinessErrorResponse(string Code, string Description);
}
