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

    private static Category CreateCategory(string name, string slug, ComponentType type, Guid? parentId = null) =>
        new(name, slug, type, null, parentId);

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
        // Hierarchy: Root (CPU) -> Child (Intel) -> GrandChild (Core i9)
        // Another branch: GPU
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
        var pGpu = new Product(gpuCategory.Id, "Nvidia RTX 4090", "rtx-4090", "SKU3", "Nvidia", 1500, 5, "img.jpg", ComponentType.Gpu, ProductCommandValidatorTests.CreateValidGpuSpec());
        context.Products.AddRange(pRoot, pIntel, pI9, pGpu);
        await context.SaveChangesAsync();

        var handler = new GetProductsQueryHandler(context);

        // Filtering by cpuRoot should return pRoot, pIntel, and pI9 (3 items)
        var resultRoot = await handler.Handle(new GetProductsQuery(new ProductFilterOptions(CategoryId: cpuRoot.Id)), CancellationToken.None);
        resultRoot.IsSuccess.Should().BeTrue();
        resultRoot.Value.TotalCount.Should().Be(3);

        // Filtering by intelChild should return pIntel and pI9 (2 items)
        var resultIntel = await handler.Handle(new GetProductsQuery(new ProductFilterOptions(CategoryId: intelChild.Id)), CancellationToken.None);
        resultIntel.IsSuccess.Should().BeTrue();
        resultIntel.Value.TotalCount.Should().Be(2);

        // Filtering by i9GrandChild should return only pI9 (1 item)
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
