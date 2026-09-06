using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RiuTek.API.Contracts;
using RiuTek.API.IntegrationTest.Infrastructure;
using RiuTek.Application.DTOs;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Data;
using Xunit;

namespace RiuTek.API.IntegrationTest.Http;

[Collection(IntegrationTestCollection.Name)]
public class CatalogBusinessRulesIntegrationTests : CatalogIntegrationTestBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CatalogBusinessRulesIntegrationTests(PostgreSqlContainerFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task CategoryCreate_WhenBusinessRulesFail_ReturnsExactErrorsAndDoesNotPersistRejectedRows()
    {
        using var adminClient = CreateAdminClient();

        // Setup: Create 2 valid root categories (CPU and Storage)
        using var createCpuRootResp = await adminClient.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(
            Name: "Root CPU B2",
            ComponentType: ComponentType.Cpu,
            Description: "Root CPU category",
            ParentId: null
        ));
        createCpuRootResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var cpuRoot = (await createCpuRootResp.Content.ReadFromJsonAsync<CategoryDto>(JsonOptions))!;

        using var createStorageRootResp = await adminClient.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(
            Name: "Root Storage B2",
            ComponentType: ComponentType.Storage,
            Description: "Root Storage category",
            ParentId: null
        ));
        createStorageRootResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var storageRoot = (await createStorageRootResp.Content.ReadFromJsonAsync<CategoryDto>(JsonOptions))!;

        // Case 1: Name generates same slug as Root CPU -> 409 Category.SlugConflict
        var duplicateSlugReq = new CreateCategoryRequest(
            Name: "  root-cpu-b2  ",
            ComponentType: ComponentType.Cpu,
            Description: "Duplicate slug category",
            ParentId: null
        );
        using var dupSlugResp = await adminClient.PostAsJsonAsync("/api/v1/categories", duplicateSlugReq);
        dupSlugResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var dupSlugError = await dupSlugResp.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
        dupSlugError.Should().NotBeNull();
        dupSlugError!.Code.Should().Be("Category.SlugConflict");

        // Case 2: Child with non-existent ParentId -> 404 Category.ParentNotFound
        var nonExistentParentReq = new CreateCategoryRequest(
            Name: "Orphan CPU Category",
            ComponentType: ComponentType.Cpu,
            Description: "Non-existent parent category",
            ParentId: Guid.NewGuid()
        );
        using var nonExistentParentResp = await adminClient.PostAsJsonAsync("/api/v1/categories", nonExistentParentReq);
        nonExistentParentResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var nonExistentParentError = await nonExistentParentResp.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
        nonExistentParentError.Should().NotBeNull();
        nonExistentParentError!.Code.Should().Be("Category.ParentNotFound");

        // Case 3: Child CPU under parent Storage -> 400 Category.ComponentTypeMismatch
        var mismatchTypeReq = new CreateCategoryRequest(
            Name: "CPU Under Storage Category",
            ComponentType: ComponentType.Cpu,
            Description: "Mismatch component type category",
            ParentId: storageRoot.Id
        );
        using var mismatchTypeResp = await adminClient.PostAsJsonAsync("/api/v1/categories", mismatchTypeReq);
        mismatchTypeResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var mismatchTypeError = await mismatchTypeResp.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
        mismatchTypeError.Should().NotBeNull();
        mismatchTypeError!.Code.Should().Be("Category.ComponentTypeMismatch");

        // Fresh DB verification: only the two initial valid categories exist, intact
        using var scope = Fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var categoriesInDb = await dbContext.Categories.AsNoTracking().ToListAsync();
        categoriesInDb.Should().HaveCount(2, "No rejected categories should be persisted in database");

        var dbCpuRoot = categoriesInDb.SingleOrDefault(c => c.Id == cpuRoot.Id);
        dbCpuRoot.Should().NotBeNull();
        dbCpuRoot!.Name.Should().Be("Root CPU B2");
        dbCpuRoot.Slug.Should().Be("root-cpu-b2");
        dbCpuRoot.ComponentType.Should().Be(ComponentType.Cpu);
        dbCpuRoot.ParentId.Should().BeNull();

        var dbStorageRoot = categoriesInDb.SingleOrDefault(c => c.Id == storageRoot.Id);
        dbStorageRoot.Should().NotBeNull();
        dbStorageRoot!.Name.Should().Be("Root Storage B2");
        dbStorageRoot.Slug.Should().Be("root-storage-b2");
        dbStorageRoot.ComponentType.Should().Be(ComponentType.Storage);
        dbStorageRoot.ParentId.Should().BeNull();
    }

    [Fact]
    public async Task CategoryUpdateAndDelete_WhenHierarchyOrReferencesConflict_RejectsWithoutMutation()
    {
        using var adminClient = CreateAdminClient();
        using var staffClient = CreateStaffClient();

        // Setup 1: Hierarchy CPU Root -> Child -> Grandchild
        using var createRootResp = await adminClient.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(
            Name: "Hierarchy Root CPU",
            ComponentType: ComponentType.Cpu,
            Description: "Root of hierarchy",
            ParentId: null
        ));
        createRootResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var root = (await createRootResp.Content.ReadFromJsonAsync<CategoryDto>(JsonOptions))!;

        using var createChildResp = await staffClient.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(
            Name: "Hierarchy Child CPU",
            ComponentType: ComponentType.Cpu,
            Description: "Child of hierarchy",
            ParentId: root.Id
        ));
        createChildResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var child = (await createChildResp.Content.ReadFromJsonAsync<CategoryDto>(JsonOptions))!;

        using var createGrandchildResp = await staffClient.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(
            Name: "Hierarchy Grandchild CPU",
            ComponentType: ComponentType.Cpu,
            Description: "Grandchild of hierarchy",
            ParentId: child.Id
        ));
        createGrandchildResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var grandchild = (await createGrandchildResp.Content.ReadFromJsonAsync<CategoryDto>(JsonOptions))!;

        // Setup 2: Independent CPU Category containing a CPU Product
        using var createCatWithProdResp = await adminClient.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(
            Name: "Category With Product",
            ComponentType: ComponentType.Cpu,
            Description: "Category containing products",
            ParentId: null
        ));
        createCatWithProdResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var catWithProd = (await createCatWithProdResp.Content.ReadFromJsonAsync<CategoryDto>(JsonOptions))!;

        using var createProdResp = await adminClient.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            CategoryId: catWithProd.Id,
            Name: "CPU Product For Category Protection",
            Sku: "SKU-PROT-CPU-001",
            Brand: "Intel",
            Price: 10000000m,
            OriginalPrice: null,
            StockQuantity: 10,
            ImageUrl: "https://example.com/p.jpg",
            AdditionalImages: null,
            ComponentType: ComponentType.Cpu,
            Specifications: CreateSampleCpuSpec(CpuSocket.LGA1700, 3.0, 5.0)
        ));
        createProdResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var product = (await createProdResp.Content.ReadFromJsonAsync<ProductDto>(JsonOptions))!;

        // Setup 3: Another Category for slug conflict
        using var createConflictTargetResp = await adminClient.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(
            Name: "Target Conflict Category",
            ComponentType: ComponentType.Cpu,
            Description: "Category to collide slug with",
            ParentId: null
        ));
        createConflictTargetResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var conflictTarget = (await createConflictTargetResp.Content.ReadFromJsonAsync<CategoryDto>(JsonOptions))!;

        // Case 1: Root self-parent -> 400 Category.SelfParent
        using var selfParentResp = await staffClient.PutAsJsonAsync($"/api/v1/categories/{root.Id}", new UpdateCategoryRequest(
            Name: root.Name,
            ComponentType: root.ComponentType,
            Description: root.Description,
            ParentId: root.Id
        ));
        selfParentResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var selfParentError = await selfParentResp.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
        selfParentError.Should().NotBeNull();
        selfParentError!.Code.Should().Be("Category.SelfParent");

        // Case 2: Root cycle: set ParentId = Grandchild.Id -> 400 Category.CycleDetected
        using var cycleResp = await staffClient.PutAsJsonAsync($"/api/v1/categories/{root.Id}", new UpdateCategoryRequest(
            Name: root.Name,
            ComponentType: root.ComponentType,
            Description: root.Description,
            ParentId: grandchild.Id
        ));
        cycleResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var cycleError = await cycleResp.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
        cycleError.Should().NotBeNull();
        cycleError!.Code.Should().Be("Category.CycleDetected");

        // Case 3: Change ComponentType of Root (which has children) -> 409 Category.HasSubCategories
        using var changeTypeWithSubResp = await staffClient.PutAsJsonAsync($"/api/v1/categories/{root.Id}", new UpdateCategoryRequest(
            Name: root.Name,
            ComponentType: ComponentType.Storage, // Changed
            Description: root.Description,
            ParentId: null
        ));
        changeTypeWithSubResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var changeTypeWithSubError = await changeTypeWithSubResp.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
        changeTypeWithSubError.Should().NotBeNull();
        changeTypeWithSubError!.Code.Should().Be("Category.HasSubCategories");

        // Case 4: Delete Root (which has children) -> 409 Category.HasSubCategories
        using var deleteRootWithSubResp = await adminClient.DeleteAsync($"/api/v1/categories/{root.Id}");
        deleteRootWithSubResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var deleteRootWithSubError = await deleteRootWithSubResp.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
        deleteRootWithSubError.Should().NotBeNull();
        deleteRootWithSubError!.Code.Should().Be("Category.HasSubCategories");

        // Case 5: Change ComponentType of Category containing Product (ParentId null to test product check directly) -> 409 Category.HasProducts
        using var changeTypeWithProdResp = await staffClient.PutAsJsonAsync($"/api/v1/categories/{catWithProd.Id}", new UpdateCategoryRequest(
            Name: catWithProd.Name,
            ComponentType: ComponentType.Storage, // Changed
            Description: catWithProd.Description,
            ParentId: null
        ));
        changeTypeWithProdResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var changeTypeWithProdError = await changeTypeWithProdResp.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
        changeTypeWithProdError.Should().NotBeNull();
        changeTypeWithProdError!.Code.Should().Be("Category.HasProducts");

        // Case 6: Delete Category containing Product -> 409 Category.HasProducts
        using var deleteCatWithProdResp = await adminClient.DeleteAsync($"/api/v1/categories/{catWithProd.Id}");
        deleteCatWithProdResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var deleteCatWithProdError = await deleteCatWithProdResp.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
        deleteCatWithProdError.Should().NotBeNull();
        deleteCatWithProdError!.Code.Should().Be("Category.HasProducts");

        // Case 7: Update Category to name yielding slug of another category -> 409 Category.SlugConflict
        using var catSlugConflictResp = await staffClient.PutAsJsonAsync($"/api/v1/categories/{catWithProd.Id}", new UpdateCategoryRequest(
            Name: "  Target Conflict Category  ", // Yields target-conflict-category
            ComponentType: ComponentType.Cpu,
            Description: catWithProd.Description,
            ParentId: null
        ));
        catSlugConflictResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var catSlugConflictError = await catSlugConflictResp.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
        catSlugConflictError.Should().NotBeNull();
        catSlugConflictError!.Code.Should().Be("Category.SlugConflict");

        // Fresh DB verification: hierarchy untouched, no deleted entities, no partial modifications
        using var scope = Fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var allCategories = await dbContext.Categories.AsNoTracking().ToListAsync();
        allCategories.Should().HaveCount(5, "All 5 setup categories must remain in database");

        var dbRoot = allCategories.Single(c => c.Id == root.Id);
        dbRoot.ParentId.Should().BeNull();
        dbRoot.ComponentType.Should().Be(ComponentType.Cpu);
        dbRoot.Name.Should().Be("Hierarchy Root CPU");

        var dbChild = allCategories.Single(c => c.Id == child.Id);
        dbChild.ParentId.Should().Be(root.Id);
        dbChild.ComponentType.Should().Be(ComponentType.Cpu);

        var dbGrandchild = allCategories.Single(c => c.Id == grandchild.Id);
        dbGrandchild.ParentId.Should().Be(child.Id);
        dbGrandchild.ComponentType.Should().Be(ComponentType.Cpu);

        var dbCatWithProd = allCategories.Single(c => c.Id == catWithProd.Id);
        dbCatWithProd.Name.Should().Be("Category With Product");
        dbCatWithProd.Slug.Should().Be("category-with-product");
        dbCatWithProd.ComponentType.Should().Be(ComponentType.Cpu);

        var dbConflictTarget = allCategories.Single(c => c.Id == conflictTarget.Id);
        dbConflictTarget.Name.Should().Be("Target Conflict Category");
        dbConflictTarget.Slug.Should().Be("target-conflict-category");

        var dbProduct = await dbContext.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == product.Id);
        dbProduct.Should().NotBeNull();
        dbProduct!.CategoryId.Should().Be(catWithProd.Id);
    }

    [Fact]
    public async Task ProductCreate_WhenRelationshipOrUniquenessRulesFail_ReturnsExactErrorsAndPersistsOnlyValidProduct()
    {
        using var adminClient = CreateAdminClient();

        // Setup: Category CPU, Category GPU, and 1 valid CPU Product
        using var createCpuCatResp = await adminClient.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(
            Name: "Category CPU For Product Create",
            ComponentType: ComponentType.Cpu,
            Description: "CPU category",
            ParentId: null
        ));
        createCpuCatResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var cpuCat = (await createCpuCatResp.Content.ReadFromJsonAsync<CategoryDto>(JsonOptions))!;

        using var createGpuCatResp = await adminClient.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(
            Name: "Category GPU For Product Create",
            ComponentType: ComponentType.Gpu,
            Description: "GPU category",
            ParentId: null
        ));
        createGpuCatResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var gpuCat = (await createGpuCatResp.Content.ReadFromJsonAsync<CategoryDto>(JsonOptions))!;

        var validProductReq = new CreateProductRequest(
            CategoryId: cpuCat.Id,
            Name: "Valid Intel Core i9-14900K Processor",
            Sku: "SKU-CPU-ORIGINAL-B2",
            Brand: "Intel",
            Price: 15000000m,
            OriginalPrice: 17000000m,
            StockQuantity: 10,
            ImageUrl: "https://example.com/14900k.jpg",
            AdditionalImages: new List<string> { "https://example.com/14900k-1.jpg" },
            ComponentType: ComponentType.Cpu,
            Specifications: CreateSampleCpuSpec(CpuSocket.LGA1700, 3.2, 6.0)
        );

        using var createProdResp = await adminClient.PostAsJsonAsync("/api/v1/products", validProductReq);
        createProdResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var validProduct = (await createProdResp.Content.ReadFromJsonAsync<ProductDto>(JsonOptions))!;

        // Case 1: CategoryId does not exist -> 404 Product.CategoryNotFound
        var nonExistentCatReq = validProductReq with
        {
            CategoryId = Guid.NewGuid(),
            Sku = "SKU-CPU-NEW-001",
            Name = "Product With Non Existent Category"
        };
        using var nonExistentCatResp = await adminClient.PostAsJsonAsync("/api/v1/products", nonExistentCatReq);
        nonExistentCatResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var nonExistentCatError = await nonExistentCatResp.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
        nonExistentCatError.Should().NotBeNull();
        nonExistentCatError!.Code.Should().Be("Product.CategoryNotFound");

        // Case 2: CPU component/spec pointing to Category GPU -> 400 Product.CategoryComponentTypeMismatch
        var mismatchCatReq = validProductReq with
        {
            CategoryId = gpuCat.Id,
            Sku = "SKU-CPU-NEW-002",
            Name = "Product Mismatching Category Type"
        };
        using var mismatchCatResp = await adminClient.PostAsJsonAsync("/api/v1/products", mismatchCatReq);
        mismatchCatResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var mismatchCatError = await mismatchCatResp.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
        mismatchCatError.Should().NotBeNull();
        mismatchCatError!.Code.Should().Be("Product.CategoryComponentTypeMismatch");

        // Case 3: Different Name, but SKU matching after trim/case normalization -> 409 Product.SkuConflict
        var dupSkuReq = validProductReq with
        {
            Name = "Distinct Processor Name But Duplicate Sku",
            Sku = "  sku-cpu-original-b2  " // Case-insensitive and trimmed duplicate
        };
        using var dupSkuResp = await adminClient.PostAsJsonAsync("/api/v1/products", dupSkuReq);
        dupSkuResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var dupSkuError = await dupSkuResp.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
        dupSkuError.Should().NotBeNull();
        dupSkuError!.Code.Should().Be("Product.SkuConflict");

        // Case 4: Different SKU, but Name generating same slug -> 409 Product.SlugConflict
        var dupSlugReq = validProductReq with
        {
            Name = "  valid intel core i9-14900k processor  ", // Same slug
            Sku = "SKU-CPU-DISTINCT-003"
        };
        using var dupSlugResp = await adminClient.PostAsJsonAsync("/api/v1/products", dupSlugReq);
        dupSlugResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var dupSlugError = await dupSlugResp.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
        dupSlugError.Should().NotBeNull();
        dupSlugError!.Code.Should().Be("Product.SlugConflict");

        // Fresh DB verification: only the 1 valid product exists, intact
        using var scope = Fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var productsInDb = await dbContext.Products.AsNoTracking().ToListAsync();
        productsInDb.Should().HaveCount(1, "Only the single valid product must be persisted in database");

        var dbProduct = productsInDb.Single();
        dbProduct.Id.Should().Be(validProduct.Id);
        dbProduct.CategoryId.Should().Be(cpuCat.Id);
        dbProduct.Name.Should().Be("Valid Intel Core i9-14900K Processor");
        dbProduct.Sku.Should().Be("SKU-CPU-ORIGINAL-B2");
        dbProduct.Slug.Should().Be("valid-intel-core-i9-14900k-processor");
        dbProduct.ComponentType.Should().Be(ComponentType.Cpu);
        dbProduct.Specifications.Should().BeOfType<CpuSpecification>();
        var dbSpec = (CpuSpecification)dbProduct.Specifications;
        dbSpec.Socket.Should().Be(CpuSocket.LGA1700);
        dbSpec.BaseClockGhz.Should().Be(3.2);
        dbSpec.BoostClockGhz.Should().Be(6.0);
    }

    [Fact]
    public async Task ProductUpdate_WhenRelationshipOrUniquenessRulesFail_ReturnsExactErrorsAndLeavesOriginalStateUntouched()
    {
        using var adminClient = CreateAdminClient();
        using var staffClient = CreateStaffClient();

        // Setup: Category CPU, Category GPU, and 2 distinct CPU Products (A and B)
        using var createCpuCatResp = await adminClient.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(
            Name: "Category CPU For Product Update",
            ComponentType: ComponentType.Cpu,
            Description: "CPU category",
            ParentId: null
        ));
        createCpuCatResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var cpuCat = (await createCpuCatResp.Content.ReadFromJsonAsync<CategoryDto>(JsonOptions))!;

        using var createGpuCatResp = await adminClient.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(
            Name: "Category GPU For Product Update",
            ComponentType: ComponentType.Gpu,
            Description: "GPU category",
            ParentId: null
        ));
        createGpuCatResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var gpuCat = (await createGpuCatResp.Content.ReadFromJsonAsync<CategoryDto>(JsonOptions))!;

        var specA = CreateSampleCpuSpec(CpuSocket.LGA1700, 3.5, 5.3);
        using var createProdAResp = await adminClient.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            CategoryId: cpuCat.Id,
            Name: "Intel Core i5-14600K Gaming CPU",
            Sku: "SKU-CPU-A-14600K",
            Brand: "Intel",
            Price: 8000000m,
            OriginalPrice: 9000000m,
            StockQuantity: 30,
            ImageUrl: "https://example.com/14600k.jpg",
            AdditionalImages: new List<string> { "https://example.com/14600k-1.jpg" },
            ComponentType: ComponentType.Cpu,
            Specifications: specA
        ));
        createProdAResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var productA = (await createProdAResp.Content.ReadFromJsonAsync<ProductDto>(JsonOptions))!;

        var specB = CreateSampleCpuSpec(CpuSocket.AM5, 4.2, 5.0);
        using var createProdBResp = await adminClient.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            CategoryId: cpuCat.Id,
            Name: "AMD Ryzen 7 7800X3D Gaming CPU",
            Sku: "SKU-CPU-B-7800X3D",
            Brand: "AMD",
            Price: 11000000m,
            OriginalPrice: 12000000m,
            StockQuantity: 20,
            ImageUrl: "https://example.com/7800x3d.jpg",
            AdditionalImages: new List<string> { "https://example.com/7800x3d-1.jpg" },
            ComponentType: ComponentType.Cpu,
            Specifications: specB
        ));
        createProdBResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var productB = (await createProdBResp.Content.ReadFromJsonAsync<ProductDto>(JsonOptions))!;

        // Snapshot of Product A state before negative updates
        var snapshotCategoryId = productA.CategoryId;
        var snapshotName = productA.Name;
        var snapshotSlug = productA.Slug;
        var snapshotSku = productA.Sku;
        var snapshotBrand = productA.Brand;
        var snapshotPrice = productA.Price;
        var snapshotOriginalPrice = productA.OriginalPrice;
        var snapshotStockQuantity = productA.StockQuantity;
        var snapshotIsActive = productA.IsActive;
        var snapshotImageUrl = productA.ImageUrl;
        var snapshotAdditionalImages = productA.AdditionalImages;
        var snapshotComponentType = productA.ComponentType;

        // Base update request for Product A
        var baseUpdateReq = new UpdateProductRequest(
            CategoryId: productA.CategoryId,
            Name: productA.Name,
            Sku: productA.Sku,
            Brand: productA.Brand,
            Price: productA.Price,
            OriginalPrice: productA.OriginalPrice,
            StockQuantity: productA.StockQuantity,
            IsActive: productA.IsActive,
            ImageUrl: productA.ImageUrl,
            AdditionalImages: productA.AdditionalImages,
            ComponentType: productA.ComponentType,
            Specifications: specA
        );

        // Case 1: Update A pointing to non-existent CategoryId -> 404 Product.CategoryNotFound
        var updateNonExistentCatReq = baseUpdateReq with { CategoryId = Guid.NewGuid() };
        using var updateNonExistentCatResp = await staffClient.PutAsJsonAsync($"/api/v1/products/{productA.Id}", updateNonExistentCatReq);
        updateNonExistentCatResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var updateNonExistentCatError = await updateNonExistentCatResp.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
        updateNonExistentCatError.Should().NotBeNull();
        updateNonExistentCatError!.Code.Should().Be("Product.CategoryNotFound");

        // Case 2: Update A pointing to Category GPU while retaining CPU component type -> 400 Product.CategoryComponentTypeMismatch
        var updateMismatchCatReq = baseUpdateReq with { CategoryId = gpuCat.Id };
        using var updateMismatchCatResp = await staffClient.PutAsJsonAsync($"/api/v1/products/{productA.Id}", updateMismatchCatReq);
        updateMismatchCatResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var updateMismatchCatError = await updateMismatchCatResp.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
        updateMismatchCatError.Should().NotBeNull();
        updateMismatchCatError!.Code.Should().Be("Product.CategoryComponentTypeMismatch");

        // Case 3: Update A using Product B's SKU with different casing/whitespace -> 409 Product.SkuConflict
        var updateDupSkuReq = baseUpdateReq with { Sku = "  sku-cpu-b-7800x3d  " };
        using var updateDupSkuResp = await staffClient.PutAsJsonAsync($"/api/v1/products/{productA.Id}", updateDupSkuReq);
        updateDupSkuResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var updateDupSkuError = await updateDupSkuResp.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
        updateDupSkuError.Should().NotBeNull();
        updateDupSkuError!.Code.Should().Be("Product.SkuConflict");

        // Case 4: Update A using Product B's Name (same slug) but with distinct SKU -> 409 Product.SlugConflict
        var updateDupSlugReq = baseUpdateReq with
        {
            Name = "  AMD Ryzen 7 7800X3D Gaming CPU  ",
            Sku = "SKU-CPU-A-14600K"
        };
        using var updateDupSlugResp = await staffClient.PutAsJsonAsync($"/api/v1/products/{productA.Id}", updateDupSlugReq);
        updateDupSlugResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var updateDupSlugError = await updateDupSlugResp.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
        updateDupSlugError.Should().NotBeNull();
        updateDupSlugError!.Code.Should().Be("Product.SlugConflict");

        // Fresh DB verification: Product A matches its snapshot completely, UpdatedAt is still null
        using var scope = Fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var productsInDb = await dbContext.Products.AsNoTracking().ToListAsync();
        productsInDb.Should().HaveCount(2, "Exactly 2 products must remain in database");

        var dbProductA = productsInDb.Single(p => p.Id == productA.Id);
        dbProductA.CategoryId.Should().Be(snapshotCategoryId);
        dbProductA.Name.Should().Be(snapshotName);
        dbProductA.Slug.Should().Be(snapshotSlug);
        dbProductA.Sku.Should().Be(snapshotSku);
        dbProductA.Brand.Should().Be(snapshotBrand);
        dbProductA.Price.Should().Be(snapshotPrice);
        dbProductA.OriginalPrice.Should().Be(snapshotOriginalPrice);
        dbProductA.StockQuantity.Should().Be(snapshotStockQuantity);
        dbProductA.IsActive.Should().Be(snapshotIsActive);
        dbProductA.ImageUrl.Should().Be(snapshotImageUrl);
        dbProductA.AdditionalImages.Should().BeEquivalentTo(snapshotAdditionalImages);
        dbProductA.ComponentType.Should().Be(snapshotComponentType);
        dbProductA.UpdatedAt.Should().BeNull("Failed update attempts must not modify the UpdatedAt timestamp");

        dbProductA.Specifications.Should().BeOfType<CpuSpecification>();
        var dbSpecA = (CpuSpecification)dbProductA.Specifications;
        dbSpecA.BaseClockGhz.Should().Be(3.5);
        dbSpecA.BoostClockGhz.Should().Be(5.3);

        var dbProductB = productsInDb.Single(p => p.Id == productB.Id);
        dbProductB.CategoryId.Should().Be(cpuCat.Id);
        dbProductB.Name.Should().Be("AMD Ryzen 7 7800X3D Gaming CPU");
        dbProductB.Sku.Should().Be("SKU-CPU-B-7800X3D");
        dbProductB.Slug.Should().Be("amd-ryzen-7-7800x3d-gaming-cpu");
        dbProductB.Brand.Should().Be("AMD");
        dbProductB.Price.Should().Be(11000000m);
        dbProductB.OriginalPrice.Should().Be(12000000m);
        dbProductB.StockQuantity.Should().Be(20);
        dbProductB.IsActive.Should().BeTrue();
        dbProductB.ImageUrl.Should().Be("https://example.com/7800x3d.jpg");
        dbProductB.AdditionalImages.Should().BeEquivalentTo(new List<string> { "https://example.com/7800x3d-1.jpg" });
        dbProductB.ComponentType.Should().Be(ComponentType.Cpu);
        dbProductB.UpdatedAt.Should().BeNull();

        dbProductB.Specifications.Should().BeOfType<CpuSpecification>();
        var dbSpecB = (CpuSpecification)dbProductB.Specifications;
        dbSpecB.Socket.Should().Be(CpuSocket.AM5);
        dbSpecB.BaseClockGhz.Should().Be(4.2);
        dbSpecB.BoostClockGhz.Should().Be(5.0);
    }

    private static CpuSpecification CreateSampleCpuSpec(CpuSocket socket, double baseClock, double boostClock) =>
        new()
        {
            Socket = socket,
            CoreCount = 8,
            ThreadCount = 16,
            BaseClockGhz = baseClock,
            BoostClockGhz = boostClock,
            TdpWattage = 105,
            HasIntegratedGpu = true,
            SupportedMemoryType = RamType.DDR5,
            MaxMemorySpeedMhz = 5600
        };

    private record BusinessErrorResponse(string Code, string Description);
}
