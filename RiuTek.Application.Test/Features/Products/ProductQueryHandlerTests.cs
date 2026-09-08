using System.Reflection;
using FluentAssertions;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Products.Queries;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Test.Features.Products;

public class ProductQueryHandlerTests
{
    private static CpuSpecification CreateCpuSpec() => ProductCommandValidatorTests.CreateValidCpuSpec();
    private static GpuSpecification CreateGpuSpec() => ProductCommandValidatorTests.CreateValidGpuSpec();
    private static RamSpecification CreateRamSpec() => ProductCommandValidatorTests.CreateValidRamSpec();
    private static PsuSpecification CreatePsuSpec() => ProductCommandValidatorTests.CreateValidPsuSpec();
    private static MotherboardSpecification CreateMotherboardSpec() => ProductCommandValidatorTests.CreateValidMotherboardSpec();

    private static Category CreateCategory(string name, string slug, ComponentType type, Guid? parentId = null) =>
        new(name, slug, type, null, parentId);

    private static T SetEntityId<T>(T entity, Guid id) where T : BaseEntity
    {
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.GetSetMethod(true)!.Invoke(entity, [id]);
        return entity;
    }

    #region Public List Tests (GetProductsQueryHandler)

    [Fact]
    public async Task GetProducts_WhenDatabaseIsEmpty_ReturnsEmptyPagedResult()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var handler = new GetProductsQueryHandler(context);

        var query = new GetProductsQuery(new ProductFilterOptions(PageIndex: 1, PageSize: 10));
        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        result.Value.TotalCount.Should().Be(0);
        result.Value.TotalPages.Should().Be(0);
        result.Value.HasPreviousPage.Should().BeFalse();
        result.Value.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public async Task GetProducts_DefaultFilter_ReturnsBothActiveAndInactiveProducts()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCategory("CPU", "cpu", ComponentType.Cpu);
        context.Categories.Add(category);

        var p1 = new Product(category.Id, "Product 1", "prod-1", "SKU1", "BrandA", 100, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec()) { IsActive = true };
        var p2 = new Product(category.Id, "Product 2", "prod-2", "SKU2", "BrandB", 200, 0, "img.jpg", ComponentType.Cpu, CreateCpuSpec()) { IsActive = false };
        context.Products.AddRange(p1, p2);
        await context.SaveChangesAsync();

        var handler = new GetProductsQueryHandler(context);
        var query = new GetProductsQuery(new ProductFilterOptions());
        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(2);
        result.Value.Items.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 1)]
    [InlineData(null, 2)]
    public async Task GetProducts_IsActiveFilter_FiltersCorrectly(bool? isActiveFilter, int expectedCount)
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCategory("CPU", "cpu", ComponentType.Cpu);
        context.Categories.Add(category);

        var p1 = new Product(category.Id, "Active Prod", "active-prod", "SKU1", "BrandA", 100, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec()) { IsActive = true };
        var p2 = new Product(category.Id, "Inactive Prod", "inactive-prod", "SKU2", "BrandB", 200, 0, "img.jpg", ComponentType.Cpu, CreateCpuSpec()) { IsActive = false };
        context.Products.AddRange(p1, p2);
        await context.SaveChangesAsync();

        var handler = new GetProductsQueryHandler(context);
        var query = new GetProductsQuery(new ProductFilterOptions(IsActive: isActiveFilter));
        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(expectedCount);
        if (isActiveFilter.HasValue)
        {
            result.Value.Items.All(p => p.IsActive == isActiveFilter.Value).Should().BeTrue();
        }
    }

    [Theory]
    [InlineData("Ryzen", 1)]
    [InlineData("intel", 1)]
    [InlineData("sku-amd", 1)]
    [InlineData("ASUS", 1)]
    [InlineData("   ", 3)]
    public async Task GetProducts_SearchTerm_FiltersAcrossNameSkuAndBrandCaseInsensitively(string searchTerm, int expectedCount)
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCategory("CPU", "cpu", ComponentType.Cpu);
        context.Categories.Add(category);

        var p1 = new Product(category.Id, "AMD Ryzen 7 7800X3D", "amd-ryzen-7-7800x3d", "SKU-AMD-7800X3D", "AMD", 400, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec());
        var p2 = new Product(category.Id, "Intel Core i7-14700K", "intel-core-i7-14700k", "SKU-INT-14700K", "Intel", 420, 15, "img.jpg", ComponentType.Cpu, CreateCpuSpec());
        var p3 = new Product(category.Id, "ROG Strix Cooler", "rog-strix-cooler", "SKU-ROG-01", "Asus", 150, 5, "img.jpg", ComponentType.Cooler, ProductCommandValidatorTests.CreateValidAirCoolerSpec());
        context.Products.AddRange(p1, p2, p3);
        await context.SaveChangesAsync();

        var handler = new GetProductsQueryHandler(context);
        var query = new GetProductsQuery(new ProductFilterOptions(SearchTerm: searchTerm));
        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(expectedCount);
    }

    [Fact]
    public async Task GetProducts_BrandAndComponentTypeFilters_FiltersCorrectly()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCategory("CPU", "cpu", ComponentType.Cpu);
        context.Categories.Add(category);

        var p1 = new Product(category.Id, "Intel Core i5", "i5", "SKU1", "Intel", 200, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec());
        var p2 = new Product(category.Id, "Intel Core i7", "i7", "SKU2", "Intel", 300, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec());
        var p3 = new Product(category.Id, "AMD Ryzen 5", "r5", "SKU3", "AMD", 200, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec());
        context.Products.AddRange(p1, p2, p3);
        await context.SaveChangesAsync();

        var handler = new GetProductsQueryHandler(context);
        var query = new GetProductsQuery(new ProductFilterOptions(Brand: "  intel  ", ComponentType: ComponentType.Cpu));
        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(2);
        result.Value.Items.All(p => p.Brand == "Intel").Should().BeTrue();
    }

    [Fact]
    public async Task GetProducts_PriceRangeFilter_FiltersInclusively()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCategory("CPU", "cpu", ComponentType.Cpu);
        context.Categories.Add(category);

        var p1 = new Product(category.Id, "P1", "p1", "SKU1", "Brand", 100, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec());
        var p2 = new Product(category.Id, "P2", "p2", "SKU2", "Brand", 200, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec());
        var p3 = new Product(category.Id, "P3", "p3", "SKU3", "Brand", 300, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec());
        context.Products.AddRange(p1, p2, p3);
        await context.SaveChangesAsync();

        var handler = new GetProductsQueryHandler(context);
        var query = new GetProductsQuery(new ProductFilterOptions(MinPrice: 100, MaxPrice: 200));
        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(2);
        result.Value.Items.Select(p => p.Price).Should().BeEquivalentTo(new[] { 100m, 200m });
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 1)]
    [InlineData(null, 2)]
    public async Task GetProducts_InStockFilter_FiltersCorrectly(bool? inStock, int expectedCount)
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCategory("CPU", "cpu", ComponentType.Cpu);
        context.Categories.Add(category);

        var p1 = new Product(category.Id, "In Stock", "in-stock", "SKU1", "Brand", 100, 5, "img.jpg", ComponentType.Cpu, CreateCpuSpec());
        var p2 = new Product(category.Id, "Out of Stock", "out-of-stock", "SKU2", "Brand", 100, 0, "img.jpg", ComponentType.Cpu, CreateCpuSpec());
        context.Products.AddRange(p1, p2);
        await context.SaveChangesAsync();

        var handler = new GetProductsQueryHandler(context);
        var query = new GetProductsQuery(new ProductFilterOptions(InStock: inStock));
        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(expectedCount);
    }

    [Fact]
    public async Task GetProducts_CategoryFilter_IncludesSelfAndAllMultiLevelDescendants()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var cpuRoot = CreateCategory("CPU", "cpu", ComponentType.Cpu);
        context.Categories.Add(cpuRoot);
        await context.SaveChangesAsync();

        var intelChild = CreateCategory("Intel", "intel", ComponentType.Cpu, cpuRoot.Id);
        context.Categories.Add(intelChild);
        await context.SaveChangesAsync();

        var i9GrandChild = CreateCategory("Core i9", "core-i9", ComponentType.Cpu, intelChild.Id);
        var gpuCategory = CreateCategory("GPU", "gpu", ComponentType.Gpu);
        context.Categories.AddRange(i9GrandChild, gpuCategory);
        await context.SaveChangesAsync();

        var pRoot = new Product(cpuRoot.Id, "Generic CPU", "generic-cpu", "SKU0", "Generic", 100, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec());
        var pIntel = new Product(intelChild.Id, "Intel Core i5", "intel-core-i5", "SKU1", "Intel", 200, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec());
        var pI9 = new Product(i9GrandChild.Id, "Intel Core i9", "intel-core-i9", "SKU2", "Intel", 500, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec());
        var pGpu = new Product(gpuCategory.Id, "Nvidia RTX 4090", "rtx-4090", "SKU3", "Nvidia", 1500, 5, "img.jpg", ComponentType.Gpu, CreateGpuSpec());
        context.Products.AddRange(pRoot, pIntel, pI9, pGpu);
        await context.SaveChangesAsync();

        var handler = new GetProductsQueryHandler(context);

        var resultRoot = await handler.Handle(new GetProductsQuery(new ProductFilterOptions(CategoryId: cpuRoot.Id)), CancellationToken.None);
        resultRoot.IsSuccess.Should().BeTrue();
        resultRoot.Value.TotalCount.Should().Be(3);

        var resultIntel = await handler.Handle(new GetProductsQuery(new ProductFilterOptions(CategoryId: intelChild.Id)), CancellationToken.None);
        resultIntel.IsSuccess.Should().BeTrue();
        resultIntel.Value.TotalCount.Should().Be(2);

        var resultI9 = await handler.Handle(new GetProductsQuery(new ProductFilterOptions(CategoryId: i9GrandChild.Id)), CancellationToken.None);
        resultI9.IsSuccess.Should().BeTrue();
        resultI9.Value.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetProducts_WhenCategoryIdDoesNotExist_ReturnsCategoryNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var handler = new GetProductsQueryHandler(context);

        var query = new GetProductsQuery(new ProductFilterOptions(CategoryId: Guid.NewGuid()));
        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Product.CategoryNotFound");
    }

    [Fact]
    public async Task GetProducts_AllSortOptions_SortsCorrectlyWithStableTieBreakers()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCategory("CPU", "cpu", ComponentType.Cpu);
        context.Categories.Add(category);

        var now = DateTime.UtcNow;
        var pA = new Product(category.Id, "Alpha", "alpha", "SKU1", "Brand", 100, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec()) { CreatedAt = now.AddHours(-3) };
        var pB = new Product(category.Id, "Beta", "beta", "SKU2", "Brand", 300, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec()) { CreatedAt = now.AddHours(-2) };
        var pC = new Product(category.Id, "Gamma", "gamma", "SKU3", "Brand", 200, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec()) { CreatedAt = now.AddHours(-1) };
        context.Products.AddRange(pA, pB, pC);
        await context.SaveChangesAsync();

        var handler = new GetProductsQueryHandler(context);

        // 1. Newest (pC, pB, pA)
        var resNewest = await handler.Handle(new GetProductsQuery(new ProductFilterOptions(SortBy: ProductSortOption.Newest)), CancellationToken.None);
        resNewest.Value.Items.Select(p => p.Name).Should().ContainInOrder("Gamma", "Beta", "Alpha");

        // 2. PriceLowToHigh (pA:100, pC:200, pB:300)
        var resPriceAsc = await handler.Handle(new GetProductsQuery(new ProductFilterOptions(SortBy: ProductSortOption.PriceLowToHigh)), CancellationToken.None);
        resPriceAsc.Value.Items.Select(p => p.Name).Should().ContainInOrder("Alpha", "Gamma", "Beta");

        // 3. PriceHighToLow (pB:300, pC:200, pA:100)
        var resPriceDesc = await handler.Handle(new GetProductsQuery(new ProductFilterOptions(SortBy: ProductSortOption.PriceHighToLow)), CancellationToken.None);
        resPriceDesc.Value.Items.Select(p => p.Name).Should().ContainInOrder("Beta", "Gamma", "Alpha");

        // 4. NameAToZ (Alpha, Beta, Gamma)
        var resNameAsc = await handler.Handle(new GetProductsQuery(new ProductFilterOptions(SortBy: ProductSortOption.NameAToZ)), CancellationToken.None);
        resNameAsc.Value.Items.Select(p => p.Name).Should().ContainInOrder("Alpha", "Beta", "Gamma");

        // 5. NameZToA (Gamma, Beta, Alpha)
        var resNameDesc = await handler.Handle(new GetProductsQuery(new ProductFilterOptions(SortBy: ProductSortOption.NameZToA)), CancellationToken.None);
        resNameDesc.Value.Items.Select(p => p.Name).Should().ContainInOrder("Gamma", "Beta", "Alpha");
    }

    [Fact]
    public async Task GetProducts_SortByName_IsNormalizedCaseInsensitiveWithIdTieBreaker()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCategory("CPU", "cpu", ComponentType.Cpu);
        context.Categories.Add(category);

        var id1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var id2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var id3 = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var id4 = Guid.Parse("00000000-0000-0000-0000-000000000004");

        var p1 = SetEntityId(new Product(category.Id, "alpha", "alpha-1", "SKU1", "Brand", 100, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec()), id1);
        var p2 = SetEntityId(new Product(category.Id, "ALPHA", "alpha-2", "SKU2", "Brand", 100, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec()), id2);
        var p3 = SetEntityId(new Product(category.Id, "Beta", "beta-1", "SKU3", "Brand", 100, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec()), id3);
        var p4 = SetEntityId(new Product(category.Id, "beta", "beta-2", "SKU4", "Brand", 100, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec()), id4);

        // Seed in order (p4, p2, p3, p1) which is different from expected sort order (id1, id2, id3, id4)
        // to strictly verify that explicit Id tie-breaker takes precedence over insertion order.
        context.Products.AddRange(p4, p2, p3, p1);
        await context.SaveChangesAsync();

        var handler = new GetProductsQueryHandler(context);

        // NameAToZ: alpha/ALPHA first (tied on normalized name, ordered by Id: id1, id2), then Beta/beta (tied, ordered by Id: id3, id4)
        var resultAsc = await handler.Handle(new GetProductsQuery(new ProductFilterOptions(SortBy: ProductSortOption.NameAToZ)), CancellationToken.None);
        resultAsc.Value.Items.Select(p => p.Id).Should().Equal(id1, id2, id3, id4);

        // NameZToA: Beta/beta first (tied on normalized name, ordered by Id: id3, id4), then alpha/ALPHA (tied, ordered by Id: id1, id2)
        var resultDesc = await handler.Handle(new GetProductsQuery(new ProductFilterOptions(SortBy: ProductSortOption.NameZToA)), CancellationToken.None);
        resultDesc.Value.Items.Select(p => p.Id).Should().Equal(id3, id4, id1, id2);
    }

    [Fact]
    public async Task GetProducts_SortByPrice_SortsByPriceThenNormalizedNameThenId()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCategory("CPU", "cpu", ComponentType.Cpu);
        context.Categories.Add(category);

        var id1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var id2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var id3 = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var id4 = Guid.Parse("00000000-0000-0000-0000-000000000004");

        var p1 = SetEntityId(new Product(category.Id, "beta", "p1", "SKU1", "Brand", 100, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec()), id1);
        var p2 = SetEntityId(new Product(category.Id, "ALPHA", "p2", "SKU2", "Brand", 100, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec()), id2);
        var p3 = SetEntityId(new Product(category.Id, "alpha", "p3", "SKU3", "Brand", 100, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec()), id3);
        var p4 = SetEntityId(new Product(category.Id, "gamma", "p4", "SKU4", "Brand", 200, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec()), id4);

        // Seed p3 before p2 within the tied (Price=100, normalized Name="alpha") group
        // to strictly verify that Id tie-breaker (id2 < id3) takes precedence over insertion order.
        context.Products.AddRange(p4, p3, p1, p2);
        await context.SaveChangesAsync();

        var handler = new GetProductsQueryHandler(context);

        // PriceLowToHigh: Price 100 (p2, p3: alpha/ALPHA ordered by Id [id2, id3], then p1: beta [id1]), then Price 200 (p4 [id4])
        var resAsc = await handler.Handle(new GetProductsQuery(new ProductFilterOptions(SortBy: ProductSortOption.PriceLowToHigh)), CancellationToken.None);
        resAsc.Value.Items.Select(p => p.Id).Should().Equal(id2, id3, id1, id4);

        // PriceHighToLow: Price 200 (p4 [id4]), then Price 100 (p2, p3: alpha/ALPHA ordered by Id [id2, id3], then p1: beta [id1])
        var resDesc = await handler.Handle(new GetProductsQuery(new ProductFilterOptions(SortBy: ProductSortOption.PriceHighToLow)), CancellationToken.None);
        resDesc.Value.Items.Select(p => p.Id).Should().Equal(id4, id2, id3, id1);
    }

    [Fact]
    public async Task GetProducts_SortByNewest_UsesCreatedAtDescendingThenIdTieBreaker()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCategory("CPU", "cpu", ComponentType.Cpu);
        context.Categories.Add(category);

        var id1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var id2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var id3 = Guid.Parse("00000000-0000-0000-0000-000000000003");

        var fixedTime = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

        var p1 = SetEntityId(new Product(category.Id, "Prod1", "p1", "SKU1", "Brand", 100, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec()) { CreatedAt = fixedTime }, id1);
        var p2 = SetEntityId(new Product(category.Id, "Prod2", "p2", "SKU2", "Brand", 100, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec()) { CreatedAt = fixedTime }, id2);
        var p3 = SetEntityId(new Product(category.Id, "Prod3", "p3", "SKU3", "Brand", 100, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec()) { CreatedAt = fixedTime.AddHours(-1) }, id3);

        // Seed p2 before p1 within the tied CreatedAt group
        // to strictly verify that Id tie-breaker (id1 < id2) takes precedence over insertion order.
        context.Products.AddRange(p3, p2, p1);
        await context.SaveChangesAsync();

        var handler = new GetProductsQueryHandler(context);
        var result = await handler.Handle(new GetProductsQuery(new ProductFilterOptions(SortBy: ProductSortOption.Newest)), CancellationToken.None);

        // p1 and p2 tied on CreatedAt -> ordered by Id ascending: id1, then id2. Followed by older p3: id3.
        result.Value.Items.Select(p => p.Id).Should().Equal(id1, id2, id3);
    }

    [Fact]
    public async Task GetProducts_Pagination_WithDuplicateSortValues_DoesNotOverlapAcrossPages()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCategory("CPU", "cpu", ComponentType.Cpu);
        context.Categories.Add(category);

        var id1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var id2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var id3 = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var id4 = Guid.Parse("00000000-0000-0000-0000-000000000004");

        // Identical Price, Name, and CreatedAt
        var fixedTime = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var p1 = SetEntityId(new Product(category.Id, "SameName", "s1", "SKU1", "Brand", 100, 5, "img.jpg", ComponentType.Cpu, CreateCpuSpec()) { CreatedAt = fixedTime }, id1);
        var p2 = SetEntityId(new Product(category.Id, "SameName", "s2", "SKU2", "Brand", 100, 5, "img.jpg", ComponentType.Cpu, CreateCpuSpec()) { CreatedAt = fixedTime }, id2);
        var p3 = SetEntityId(new Product(category.Id, "SameName", "s3", "SKU3", "Brand", 100, 5, "img.jpg", ComponentType.Cpu, CreateCpuSpec()) { CreatedAt = fixedTime }, id3);
        var p4 = SetEntityId(new Product(category.Id, "SameName", "s4", "SKU4", "Brand", 100, 5, "img.jpg", ComponentType.Cpu, CreateCpuSpec()) { CreatedAt = fixedTime }, id4);

        // Seed in reverse order (p4, p3, p2, p1) to verify deterministic page partitioning by Id
        context.Products.AddRange(p4, p3, p2, p1);
        await context.SaveChangesAsync();

        var handler = new GetProductsQueryHandler(context);

        // Page 1 with size 2 -> should take [id1, id2]
        var page1 = await handler.Handle(new GetProductsQuery(new ProductFilterOptions(PageIndex: 1, PageSize: 2, SortBy: ProductSortOption.NameAToZ)), CancellationToken.None);
        page1.Value.Items.Select(p => p.Id).Should().Equal(id1, id2);

        // Page 2 with size 2 -> should take [id3, id4]
        var page2 = await handler.Handle(new GetProductsQuery(new ProductFilterOptions(PageIndex: 2, PageSize: 2, SortBy: ProductSortOption.NameAToZ)), CancellationToken.None);
        page2.Value.Items.Select(p => p.Id).Should().Equal(id3, id4);

        var allIds = page1.Value.Items.Select(p => p.Id).Concat(page2.Value.Items.Select(p => p.Id)).ToList();
        allIds.Should().Equal(id1, id2, id3, id4);

        var page1Ids = page1.Value.Items.Select(p => p.Id).ToHashSet();
        var page2Ids = page2.Value.Items.Select(p => p.Id).ToHashSet();
        page1Ids.Overlaps(page2Ids).Should().BeFalse();
    }

    [Fact]
    public async Task GetProducts_WhenOffsetCausesOverflowOrBeyondEnd_ReturnsEmptyItemsWithoutThrowing()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCategory("CPU", "cpu", ComponentType.Cpu);
        context.Categories.Add(category);

        // Seed 6 products (instead of 2) so that if (PageIndex - 1) * PageSize wraps around to 4,
        // Skip(4) would have returned items 5 and 6, proving this test truly discriminates against int overflow bug.
        var products = Enumerable.Range(1, 6).Select(i =>
            new Product(category.Id, $"Prod{i}", $"prod-{i}", $"SKU{i}", "Brand", i * 50, 5, "img.jpg", ComponentType.Cpu, CreateCpuSpec())
        ).ToList();
        context.Products.AddRange(products);
        await context.SaveChangesAsync();

        var handler = new GetProductsQueryHandler(context);

        // 1. PageIndex = 214748366 with PageSize = 20 -> previously would overflow int offset to 4 and return 2 items.
        // With long offset calculation, offset >= totalCount (6) -> safely returns empty items.
        var overflowQuery = new GetProductsQuery(new ProductFilterOptions(PageIndex: 214748366, PageSize: 20));
        var overflowResult = await handler.Handle(overflowQuery, CancellationToken.None);

        overflowResult.IsSuccess.Should().BeTrue();
        overflowResult.Value.Items.Should().BeEmpty();
        overflowResult.Value.TotalCount.Should().Be(6);
        overflowResult.Value.TotalPages.Should().Be(1);
        overflowResult.Value.PageIndex.Should().Be(214748366);
        overflowResult.Value.PageSize.Should().Be(20);
        overflowResult.Value.HasPreviousPage.Should().BeTrue();
        overflowResult.Value.HasNextPage.Should().BeFalse();

        // 2. PageIndex = int.MaxValue with PageSize = 50 -> must not throw OverflowException
        var maxPageQuery = new GetProductsQuery(new ProductFilterOptions(PageIndex: int.MaxValue, PageSize: 50));
        var maxPageResult = await handler.Handle(maxPageQuery, CancellationToken.None);

        maxPageResult.IsSuccess.Should().BeTrue();
        maxPageResult.Value.Items.Should().BeEmpty();
        maxPageResult.Value.TotalCount.Should().Be(6);
        maxPageResult.Value.TotalPages.Should().Be(1);
        maxPageResult.Value.PageIndex.Should().Be(int.MaxValue);
        maxPageResult.Value.PageSize.Should().Be(50);
        maxPageResult.Value.HasPreviousPage.Should().BeTrue();
        maxPageResult.Value.HasNextPage.Should().BeFalse();

        // 3. Case directly past last page: with PageSize = 1 and 6 products, use PageIndex = 7 (TotalPages = 6)
        var pastEndQuery = new GetProductsQuery(new ProductFilterOptions(PageIndex: 7, PageSize: 1));
        var pastEndResult = await handler.Handle(pastEndQuery, CancellationToken.None);

        pastEndResult.IsSuccess.Should().BeTrue();
        pastEndResult.Value.Items.Should().BeEmpty();
        pastEndResult.Value.TotalCount.Should().Be(6);
        pastEndResult.Value.TotalPages.Should().Be(6);
        pastEndResult.Value.PageIndex.Should().Be(7);
        pastEndResult.Value.PageSize.Should().Be(1);
        pastEndResult.Value.HasPreviousPage.Should().BeTrue();
        pastEndResult.Value.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public async Task GetProducts_ComponentTypeFilter_FiltersIndependentlyAndExcludesOtherTypes()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var catCpu = CreateCategory("CPU", "cpu", ComponentType.Cpu);
        var catGpu = CreateCategory("GPU", "gpu", ComponentType.Gpu);
        var catMobo = CreateCategory("Motherboard", "motherboard", ComponentType.Motherboard);
        context.Categories.AddRange(catCpu, catGpu, catMobo);

        var pCpu = new Product(catCpu.Id, "ASUS ROG CPU", "asus-rog-cpu", "SKU-CPU", "ASUS", 500, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec());
        var pGpu = new Product(catGpu.Id, "ASUS ROG GPU", "asus-rog-gpu", "SKU-GPU", "ASUS", 1000, 10, "img.jpg", ComponentType.Gpu, CreateGpuSpec());
        var pMobo = new Product(catMobo.Id, "ASUS ROG Motherboard", "asus-rog-mobo", "SKU-MOBO", "ASUS", 400, 10, "img.jpg", ComponentType.Motherboard, CreateMotherboardSpec());
        context.Products.AddRange(pCpu, pGpu, pMobo);
        await context.SaveChangesAsync();

        var handler = new GetProductsQueryHandler(context);
        var query = new GetProductsQuery(new ProductFilterOptions(ComponentType: ComponentType.Gpu));
        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1);
        result.Value.Items.Should().ContainSingle(p => p.Id == pGpu.Id);
        result.Value.Items.Should().NotContain(p => p.Id == pCpu.Id);
        result.Value.Items.Should().NotContain(p => p.Id == pMobo.Id);
    }

    [Fact]
    public async Task GetProducts_CombinedFilters_EnforcesAndSemanticsAcrossAllConditions()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var catRam = CreateCategory("RAM", "ram", ComponentType.Ram);
        var catPsu = CreateCategory("PSU", "psu", ComponentType.Psu);
        context.Categories.AddRange(catRam, catPsu);

        // Filter Target: Brand="Corsair", ComponentType=Ram, MinPrice=100, MaxPrice=200, InStock=true, IsActive=true

        // 1. Matches all
        var pTarget = new Product(catRam.Id, "Corsair Vengeance", "c-v", "SKU1", "Corsair", 150, 5, "img.jpg", ComponentType.Ram, CreateRamSpec()) { IsActive = true };

        // 2. Fails Brand
        var pFailBrand = new Product(catRam.Id, "Kingston Fury", "k-f", "SKU2", "Kingston", 150, 5, "img.jpg", ComponentType.Ram, CreateRamSpec()) { IsActive = true };

        // 3. Fails ComponentType
        var pFailType = new Product(catPsu.Id, "Corsair RM850", "c-rm", "SKU3", "Corsair", 150, 5, "img.jpg", ComponentType.Psu, CreatePsuSpec()) { IsActive = true };

        // 4. Fails Price (> MaxPrice)
        var pFailPrice = new Product(catRam.Id, "Corsair Dominator", "c-d", "SKU4", "Corsair", 250, 5, "img.jpg", ComponentType.Ram, CreateRamSpec()) { IsActive = true };

        // 5. Fails InStock (StockQuantity = 0)
        var pFailStock = new Product(catRam.Id, "Corsair LPX", "c-lpx", "SKU5", "Corsair", 150, 0, "img.jpg", ComponentType.Ram, CreateRamSpec()) { IsActive = true };

        // 6. Fails IsActive (IsActive = false)
        var pFailActive = new Product(catRam.Id, "Corsair Old", "c-old", "SKU6", "Corsair", 150, 5, "img.jpg", ComponentType.Ram, CreateRamSpec()) { IsActive = false };

        context.Products.AddRange(pTarget, pFailBrand, pFailType, pFailPrice, pFailStock, pFailActive);
        await context.SaveChangesAsync();

        var handler = new GetProductsQueryHandler(context);
        var query = new GetProductsQuery(new ProductFilterOptions(
            Brand: "corsair",
            ComponentType: ComponentType.Ram,
            MinPrice: 100,
            MaxPrice: 200,
            InStock: true,
            IsActive: true
        ));

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1);
        result.Value.Items.Should().ContainSingle(p => p.Id == pTarget.Id);
    }

    [Fact]
    public async Task ProductQueries_DoNotTrackEntitiesInChangeTracker()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCategory("CPU", "cpu", ComponentType.Cpu);
        context.Categories.Add(category);

        var product = new Product(category.Id, "Test CPU", "test-cpu", "SKU1", "Brand", 200, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec()) { IsActive = true };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        // 1. Test GetProductsQuery
        context.ChangeTracker.Clear();
        context.ChangeTracker.Entries().Should().BeEmpty();

        var listHandler = new GetProductsQueryHandler(context);
        var listResult = await listHandler.Handle(new GetProductsQuery(new ProductFilterOptions()), CancellationToken.None);
        listResult.IsSuccess.Should().BeTrue();
        context.ChangeTracker.Entries().Should().BeEmpty("GetProductsQuery must not track any entities");

        // 2. Test GetProductBySlugQuery
        context.ChangeTracker.Clear();
        var slugHandler = new GetProductBySlugQueryHandler(context);
        var slugResult = await slugHandler.Handle(new GetProductBySlugQuery("test-cpu"), CancellationToken.None);
        slugResult.IsSuccess.Should().BeTrue();
        context.ChangeTracker.Entries().Should().BeEmpty("GetProductBySlugQuery must not track any entities");

        // 3. Test GetProductByIdQuery
        context.ChangeTracker.Clear();
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var idHandler = new GetProductByIdQueryHandler(context, currentUserMock.Object);
        var idResult = await idHandler.Handle(new GetProductByIdQuery(product.Id), CancellationToken.None);
        idResult.IsSuccess.Should().BeTrue();
        context.ChangeTracker.Entries().Should().BeEmpty("GetProductByIdQuery must not track any entities");
    }

    [Fact]
    public async Task GetProducts_Pagination_CalculatesMetadataAndNonOverlappingPagesCorrectly()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCategory("CPU", "cpu", ComponentType.Cpu);
        context.Categories.Add(category);

        var products = Enumerable.Range(1, 15).Select(i =>
            new Product(category.Id, $"Product {i:D2}", $"prod-{i:D2}", $"SKU{i}", "Brand", i * 10, 5, "img.jpg", ComponentType.Cpu, CreateCpuSpec())
        ).ToList();
        context.Products.AddRange(products);
        await context.SaveChangesAsync();

        var handler = new GetProductsQueryHandler(context);

        // Page 1 of size 10 (items 1..10)
        var page1 = await handler.Handle(new GetProductsQuery(new ProductFilterOptions(PageIndex: 1, PageSize: 10, SortBy: ProductSortOption.NameAToZ)), CancellationToken.None);
        page1.Value.TotalCount.Should().Be(15);
        page1.Value.TotalPages.Should().Be(2);
        page1.Value.PageIndex.Should().Be(1);
        page1.Value.PageSize.Should().Be(10);
        page1.Value.HasPreviousPage.Should().BeFalse();
        page1.Value.HasNextPage.Should().BeTrue();
        page1.Value.Items.Should().HaveCount(10);

        // Page 2 of size 10 (items 11..15)
        var page2 = await handler.Handle(new GetProductsQuery(new ProductFilterOptions(PageIndex: 2, PageSize: 10, SortBy: ProductSortOption.NameAToZ)), CancellationToken.None);
        page2.Value.TotalCount.Should().Be(15);
        page2.Value.TotalPages.Should().Be(2);
        page2.Value.PageIndex.Should().Be(2);
        page2.Value.HasPreviousPage.Should().BeTrue();
        page2.Value.HasNextPage.Should().BeFalse();
        page2.Value.Items.Should().HaveCount(5);

        // Non-overlapping verification
        var page1Ids = page1.Value.Items.Select(p => p.Id).ToHashSet();
        var page2Ids = page2.Value.Items.Select(p => p.Id).ToHashSet();
        page1Ids.Overlaps(page2Ids).Should().BeFalse();
    }

    #endregion

    #region Public Detail Tests (GetProductBySlugQueryHandler)

    [Fact]
    public async Task GetProductBySlug_WhenActiveProductFound_ReturnsFullDto()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCategory("CPUs", "cpus", ComponentType.Cpu);
        context.Categories.Add(category);

        var product = new Product(category.Id, "Intel Core i7-14700K", "intel-core-i7-14700k", "CPU-I7", "Intel", 400, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec(), 450)
        {
            AdditionalImages = ["img-box.jpg"],
            Category = category,
            IsActive = true
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var handler = new GetProductBySlugQueryHandler(context);
        var result = await handler.Handle(new GetProductBySlugQuery("  INTEL-CORE-I7-14700K  "), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Intel Core i7-14700K");
        result.Value.Slug.Should().Be("intel-core-i7-14700k");
        result.Value.CategoryName.Should().Be("CPUs");
        result.Value.AdditionalImages.Should().ContainSingle().Which.Should().Be("img-box.jpg");
        result.Value.Specifications.Should().NotBeNull();
        result.Value.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetProductBySlug_WhenInactiveProductFound_ReturnsDtoWithIsActiveFalse()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCategory("CPUs", "cpus", ComponentType.Cpu);
        context.Categories.Add(category);

        var product = new Product(category.Id, "Intel Core i3-10100", "intel-core-i3-10100", "CPU-I3", "Intel", 100, 0, "img.jpg", ComponentType.Cpu, CreateCpuSpec())
        {
            Category = category,
            IsActive = false
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var handler = new GetProductBySlugQueryHandler(context);
        var result = await handler.Handle(new GetProductBySlugQuery("intel-core-i3-10100"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsActive.Should().BeFalse();
        result.Value.Name.Should().Be("Intel Core i3-10100");
    }

    [Fact]
    public async Task GetProductBySlug_WhenNotFound_ReturnsNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var handler = new GetProductBySlugQueryHandler(context);

        var result = await handler.Handle(new GetProductBySlugQuery("non-existent-slug"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("Product.NotFound");
    }

    #endregion

    #region Management Detail Tests (GetProductByIdQueryHandler)

    [Fact]
    public async Task GetProductById_WhenAnonymous_ReturnsUnauthorized()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(false);

        var handler = new GetProductByIdQueryHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new GetProductByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
        result.Error.Code.Should().Be("Auth.Unauthorized");
    }

    [Fact]
    public async Task GetProductById_WhenUserIdIsNull_ReturnsUnauthorized()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns((Guid?)null);

        var handler = new GetProductByIdQueryHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new GetProductByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
        result.Error.Code.Should().Be("Auth.Unauthorized");
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Guest")]
    [InlineData("")]
    [InlineData(null)]
    public async Task GetProductById_WhenCustomerOrInvalidRole_ReturnsForbidden(string? role)
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(role);

        var handler = new GetProductByIdQueryHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new GetProductByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        result.Error.Code.Should().Be("Product.Forbidden");
    }

    [Theory]
    [InlineData(UserRole.Admin, true)]
    [InlineData(UserRole.Admin, false)]
    [InlineData(UserRole.Staff, true)]
    [InlineData(UserRole.Staff, false)]
    public async Task GetProductById_WhenAdminOrStaff_ReturnsProductRegardlessOfActiveStatus(UserRole role, bool isActive)
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCategory("CPU", "cpu", ComponentType.Cpu);
        context.Categories.Add(category);

        var product = new Product(category.Id, "Test CPU", "test-cpu", "SKU1", "Brand", 250, 5, "img.jpg", ComponentType.Cpu, CreateCpuSpec())
        {
            Category = category,
            IsActive = isActive
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(role.ToString());

        var handler = new GetProductByIdQueryHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new GetProductByIdQuery(product.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(product.Id);
        result.Value.IsActive.Should().Be(isActive);
        result.Value.Name.Should().Be("Test CPU");
    }

    [Fact]
    public async Task GetProductById_WhenProductNotFound_ReturnsNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new GetProductByIdQueryHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new GetProductByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("Product.NotFound");
    }

    #endregion
}
