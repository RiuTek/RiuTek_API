using FluentAssertions;
using Moq;
using Pgvector;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Products.Commands;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Test.Features.Products;

public class ProductCommandHandlerTests
{
    private static CpuSpecification CreateCpuSpec() => ProductCommandValidatorTests.CreateValidCpuSpec();

    private static Category CreateCpuCategory(string name = "CPU", string slug = "cpu") =>
        new(name, slug, ComponentType.Cpu);

    private static Category CreateGpuCategory(string name = "GPU", string slug = "gpu") =>
        new(name, slug, ComponentType.Gpu);

    #region Authorization Matrix

    [Fact]
    public async Task CreateProduct_WhenAnonymous_ReturnsUnauthorized()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCpuCategory();
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(false);

        var handler = new CreateProductCommandHandler(context, currentUserMock.Object);
        var command = new CreateProductCommand(
            category.Id, "Product", "SKU1", "Intel", 100, null, 5, "img.jpg", null, ComponentType.Cpu, CreateCpuSpec());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
        context.Products.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateProduct_WhenUserIdIsNull_ReturnsUnauthorized()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCpuCategory();
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns((Guid?)null);

        var handler = new CreateProductCommandHandler(context, currentUserMock.Object);
        var command = new CreateProductCommand(
            category.Id, "Product", "SKU1", "Intel", 100, null, 5, "img.jpg", null, ComponentType.Cpu, CreateCpuSpec());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
        context.Products.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Guest")]
    [InlineData("")]
    [InlineData(null)]
    public async Task CreateProduct_WhenCustomerOrInvalidRole_ReturnsForbidden(string? role)
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCpuCategory();
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(role);

        var handler = new CreateProductCommandHandler(context, currentUserMock.Object);
        var command = new CreateProductCommand(
            category.Id, "Product", "SKU1", "Intel", 100, null, 5, "img.jpg", null, ComponentType.Cpu, CreateCpuSpec());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        context.Products.Should().BeEmpty();
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Staff)]
    public async Task CreateProduct_WhenAdminOrStaff_Succeeds(UserRole role)
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCpuCategory();
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(role.ToString());

        var handler = new CreateProductCommandHandler(context, currentUserMock.Object);
        var command = new CreateProductCommand(
            category.Id, "Intel Core i5-14600K", "CPU-INT-14600K", "Intel", 320, 350, 20, "img.jpg", null, ComponentType.Cpu, CreateCpuSpec());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Sku.Should().Be("CPU-INT-14600K");
    }

    [Fact]
    public async Task UpdateProduct_WhenAnonymous_ReturnsUnauthorizedAndDoesNotModify()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCpuCategory();
        context.Categories.Add(category);

        var product = new Product(category.Id, "Original", "original", "SKU-ORIG", "Intel", 100, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec());
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(false);

        var handler = new UpdateProductCommandHandler(context, currentUserMock.Object);
        var command = new UpdateProductCommand(
            product.Id, category.Id, "Modified", "SKU-MOD", "Intel", 200, null, 5, false, "img2.jpg", null, ComponentType.Cpu, CreateCpuSpec());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);

        var unchanged = await context.Products.FindAsync(product.Id);
        unchanged!.Name.Should().Be("Original");
        unchanged.Price.Should().Be(100);
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Guest")]
    [InlineData("")]
    [InlineData(null)]
    public async Task UpdateProduct_WhenCustomerOrInvalidRole_ReturnsForbiddenAndDoesNotModify(string? role)
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCpuCategory();
        context.Categories.Add(category);

        var product = new Product(category.Id, "Original", "original", "SKU-ORIG", "Intel", 100, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec());
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(role);

        var handler = new UpdateProductCommandHandler(context, currentUserMock.Object);
        var command = new UpdateProductCommand(
            product.Id, category.Id, "Modified", "SKU-MOD", "Intel", 200, null, 5, false, "img2.jpg", null, ComponentType.Cpu, CreateCpuSpec());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Forbidden);

        var unchanged = await context.Products.FindAsync(product.Id);
        unchanged!.Name.Should().Be("Original");
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Staff)]
    public async Task UpdateProduct_WhenAdminOrStaff_Succeeds(UserRole role)
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCpuCategory();
        context.Categories.Add(category);

        var product = new Product(category.Id, "Original", "original", "SKU-ORIG", "Intel", 100, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec());
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(role.ToString());

        var handler = new UpdateProductCommandHandler(context, currentUserMock.Object);
        var command = new UpdateProductCommand(
            product.Id, category.Id, "Updated Product", "SKU-UPD", "Intel", 150, null, 8, true, "img2.jpg", null, ComponentType.Cpu, CreateCpuSpec());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Updated Product");
    }

    #endregion

    #region Create Product Business Rules

    [Fact]
    public async Task CreateProduct_WhenCategoryNotFound_ReturnsCategoryNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new CreateProductCommandHandler(context, currentUserMock.Object);
        var command = new CreateProductCommand(
            Guid.NewGuid(), "Name", "SKU1", "Brand", 100, null, 10, "img.jpg", null, ComponentType.Cpu, CreateCpuSpec());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Product.CategoryNotFound");
    }

    [Fact]
    public async Task CreateProduct_WhenCategoryComponentTypeDiffers_ReturnsMismatch()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var gpuCategory = CreateGpuCategory();
        context.Categories.Add(gpuCategory);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new CreateProductCommandHandler(context, currentUserMock.Object);
        var command = new CreateProductCommand(
            gpuCategory.Id, "Intel CPU", "CPU-SKU", "Intel", 100, null, 10, "img.jpg", null, ComponentType.Cpu, CreateCpuSpec());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Product.CategoryComponentTypeMismatch");
    }

    [Fact]
    public async Task CreateProduct_WhenSkuConflictsCaseInsensitively_ReturnsSkuConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCpuCategory();
        context.Categories.Add(category);

        var existing = new Product(category.Id, "Existing", "existing", "CPU-SKU-1", "Intel", 100, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec());
        context.Products.Add(existing);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new CreateProductCommandHandler(context, currentUserMock.Object);
        var command = new CreateProductCommand(
            category.Id, "New Product", "cpu-sku-1", "Intel", 100, null, 10, "img.jpg", null, ComponentType.Cpu, CreateCpuSpec());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Product.SkuConflict");
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task CreateProduct_WhenSlugConflictsCaseInsensitively_ReturnsSlugConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCpuCategory();
        context.Categories.Add(category);

        var existing = new Product(category.Id, "Intel Core i9", "intel-core-i9", "SKU1", "Intel", 100, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec());
        context.Products.Add(existing);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new CreateProductCommandHandler(context, currentUserMock.Object);
        var command = new CreateProductCommand(
            category.Id, "Intel  Core  i9", "SKU2", "Intel", 100, null, 10, "img.jpg", null, ComponentType.Cpu, CreateCpuSpec());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Product.SlugConflict");
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task CreateProduct_Success_NormalizesDataAndPersistsCorrectly()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCpuCategory("Processors", "processors");
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new CreateProductCommandHandler(context, currentUserMock.Object);
        var command = new CreateProductCommand(
            CategoryId: category.Id,
            Name: "  AMD Ryzen 7 7800X3D  ",
            Sku: "  amd-r7-7800x3d  ",
            Brand: "  AMD  ",
            Price: 380,
            OriginalPrice: 420,
            StockQuantity: 50,
            ImageUrl: "  https://example.com/7800x3d.png  ",
            AdditionalImages: ["  https://example.com/box.png  ", "https://example.com/box.png", "   ", "https://example.com/pins.png"],
            ComponentType: ComponentType.Cpu,
            Specifications: CreateCpuSpec()
        );

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("AMD Ryzen 7 7800X3D");
        result.Value.Slug.Should().Be("amd-ryzen-7-7800x3d");
        result.Value.Sku.Should().Be("AMD-R7-7800X3D");
        result.Value.Brand.Should().Be("AMD");
        result.Value.CategoryName.Should().Be("Processors");
        result.Value.ImageUrl.Should().Be("https://example.com/7800x3d.png");
        result.Value.AdditionalImages.Should().BeEquivalentTo(new[] { "https://example.com/box.png", "https://example.com/pins.png" }, options => options.WithStrictOrdering());
        result.Value.IsActive.Should().BeTrue();

        var inDb = await context.Products.FindAsync(result.Value.Id);
        inDb.Should().NotBeNull();
        inDb!.Embedding.Should().BeNull();
    }

    #endregion

    #region Update Product Business Rules

    [Fact]
    public async Task UpdateProduct_WhenProductNotFound_ReturnsNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new UpdateProductCommandHandler(context, currentUserMock.Object);
        var command = new UpdateProductCommand(
            Guid.NewGuid(), Guid.NewGuid(), "Name", "SKU1", "Brand", 100, null, 10, true, "img.jpg", null, ComponentType.Cpu, CreateCpuSpec());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task UpdateProduct_WhenTargetCategoryNotFound_ReturnsCategoryNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCpuCategory();
        context.Categories.Add(category);

        var product = new Product(category.Id, "Original", "original", "SKU1", "Intel", 100, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec());
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new UpdateProductCommandHandler(context, currentUserMock.Object);
        var command = new UpdateProductCommand(
            product.Id, Guid.NewGuid(), "Original", "SKU1", "Intel", 100, null, 10, true, "img.jpg", null, ComponentType.Cpu, CreateCpuSpec());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Product.CategoryNotFound");
    }

    [Fact]
    public async Task UpdateProduct_WhenTargetCategoryComponentTypeDiffers_ReturnsMismatch()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var cpuCategory = CreateCpuCategory();
        var gpuCategory = CreateGpuCategory();
        context.Categories.AddRange(cpuCategory, gpuCategory);

        var product = new Product(cpuCategory.Id, "Original", "original", "SKU1", "Intel", 100, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec());
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new UpdateProductCommandHandler(context, currentUserMock.Object);
        var command = new UpdateProductCommand(
            product.Id, gpuCategory.Id, "Original", "SKU1", "Intel", 100, null, 10, true, "img.jpg", null, ComponentType.Cpu, CreateCpuSpec());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Product.CategoryComponentTypeMismatch");
    }

    [Fact]
    public async Task UpdateProduct_WhenSkuOrSlugConflictsWithOtherProduct_ReturnsConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCpuCategory();
        context.Categories.Add(category);

        var prod1 = new Product(category.Id, "Product One", "product-one", "SKU-ONE", "Intel", 100, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec());
        var prod2 = new Product(category.Id, "Product Two", "product-two", "SKU-TWO", "Intel", 100, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec());
        context.Products.AddRange(prod1, prod2);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new UpdateProductCommandHandler(context, currentUserMock.Object);

        // SKU conflict with prod1
        var skuConflictCmd = new UpdateProductCommand(
            prod2.Id, category.Id, "Unique Name", "sku-one", "Intel", 100, null, 10, true, "img.jpg", null, ComponentType.Cpu, CreateCpuSpec());
        var skuResult = await handler.Handle(skuConflictCmd, CancellationToken.None);
        skuResult.IsSuccess.Should().BeFalse();
        skuResult.Error.Code.Should().Be("Product.SkuConflict");

        // Slug conflict with prod1
        var slugConflictCmd = new UpdateProductCommand(
            prod2.Id, category.Id, "Product One", "SKU-TWO", "Intel", 100, null, 10, true, "img.jpg", null, ComponentType.Cpu, CreateCpuSpec());
        var slugResult = await handler.Handle(slugConflictCmd, CancellationToken.None);
        slugResult.IsSuccess.Should().BeFalse();
        slugResult.Error.Code.Should().Be("Product.SlugConflict");
    }

    [Fact]
    public async Task UpdateProduct_KeepingSameSkuAndSlug_DoesNotTriggerSelfConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCpuCategory();
        context.Categories.Add(category);

        var product = new Product(category.Id, "Intel Core i7", "intel-core-i7", "SKU-I7", "Intel", 300, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec());
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new UpdateProductCommandHandler(context, currentUserMock.Object);
        var command = new UpdateProductCommand(
            product.Id, category.Id, "Intel Core i7", "SKU-I7", "Intel", 320, 350, 12, true, "img-new.jpg", null, ComponentType.Cpu, CreateCpuSpec());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Price.Should().Be(320);
    }

    [Fact]
    public async Task UpdateProduct_CanChangeCategoryAndDtoReturnsNewCategoryName()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var catA = new Category("High End CPUs", "high-end-cpus", ComponentType.Cpu);
        var catB = new Category("Mainstream CPUs", "mainstream-cpus", ComponentType.Cpu);
        context.Categories.AddRange(catA, catB);

        var product = new Product(catA.Id, "Intel Core i5", "intel-core-i5", "SKU-I5", "Intel", 200, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec())
        {
            Category = catA
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new UpdateProductCommandHandler(context, currentUserMock.Object);
        var command = new UpdateProductCommand(
            product.Id, catB.Id, "Intel Core i5", "SKU-I5", "Intel", 200, null, 10, true, "img.jpg", null, ComponentType.Cpu, CreateCpuSpec());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CategoryId.Should().Be(catB.Id);
        result.Value.CategoryName.Should().Be("Mainstream CPUs");
    }

    [Fact]
    public async Task UpdateProduct_CanDiscontinueProduct_LeavingRowInDatabase()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCpuCategory();
        context.Categories.Add(category);

        var product = new Product(category.Id, "Intel Core i3", "intel-core-i3", "SKU-I3", "Intel", 100, 5, "img.jpg", ComponentType.Cpu, CreateCpuSpec())
        {
            IsActive = true
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new UpdateProductCommandHandler(context, currentUserMock.Object);
        var command = new UpdateProductCommand(
            product.Id, category.Id, "Intel Core i3", "SKU-I3", "Intel", 90, null, 0, false, "img.jpg", null, ComponentType.Cpu, CreateCpuSpec());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsActive.Should().BeFalse();

        // Verify product still exists in DB
        var inDb = await context.Products.FindAsync(product.Id);
        inDb.Should().NotBeNull();
        inDb!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateProduct_CanReenableDiscontinuedProduct()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCpuCategory();
        context.Categories.Add(category);

        var product = new Product(category.Id, "Intel Core i3", "intel-core-i3", "SKU-I3", "Intel", 100, 0, "img.jpg", ComponentType.Cpu, CreateCpuSpec())
        {
            IsActive = false
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new UpdateProductCommandHandler(context, currentUserMock.Object);
        var command = new UpdateProductCommand(
            product.Id, category.Id, "Intel Core i3", "SKU-I3", "Intel", 105, null, 15, true, "img.jpg", null, ComponentType.Cpu, CreateCpuSpec());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsActive.Should().BeTrue();

        var inDb = await context.Products.FindAsync(product.Id);
        inDb!.IsActive.Should().BeTrue();
        inDb.StockQuantity.Should().Be(15);
    }

    [Fact]
    public async Task UpdateProduct_PreservesExistingEmbedding()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = CreateCpuCategory();
        context.Categories.Add(category);

        var initialVector = new Vector(new float[] { 0.1f, 0.2f, 0.3f });
        var product = new Product(category.Id, "Intel Core i7", "intel-core-i7", "SKU-I7", "Intel", 300, 10, "img.jpg", ComponentType.Cpu, CreateCpuSpec())
        {
            Embedding = initialVector
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new UpdateProductCommandHandler(context, currentUserMock.Object);
        var command = new UpdateProductCommand(
            product.Id, category.Id, "Intel Core i7 Gen 14", "SKU-I7", "Intel", 350, null, 8, true, "img-gen14.jpg", null, ComponentType.Cpu, CreateCpuSpec());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var inDb = await context.Products.FindAsync(product.Id);
        inDb!.Embedding.Should().NotBeNull();
        inDb.Embedding!.ToArray().Should().BeEquivalentTo(new float[] { 0.1f, 0.2f, 0.3f });
    }

    #endregion
}
