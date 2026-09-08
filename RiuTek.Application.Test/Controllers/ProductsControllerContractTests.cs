using System.Reflection;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RiuTek.API.Contracts;
using RiuTek.API.Controllers;
using RiuTek.Application.Common.Models;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Products.Commands;
using RiuTek.Application.Features.Products.Queries;
using RiuTek.Application.Test.Features.Products;
using RiuTek.Core.Common;
using RiuTek.Core.Constants;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Test.Controllers;

public class ProductsControllerContractTests : IDisposable
{
    private readonly Mock<ISender> _senderMock;
    private readonly ServiceProvider _serviceProvider;
    private readonly ProductsController _controller;

    public ProductsControllerContractTests()
    {
        _senderMock = new Mock<ISender>();
        var services = new ServiceCollection();
        services.AddScoped<ISender>(_ => _senderMock.Object);
        _serviceProvider = services.BuildServiceProvider();

        _controller = new ProductsController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = _serviceProvider
                }
            }
        };
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    private static CpuSpecification CreateCpuSpec() => ProductCommandValidatorTests.CreateValidCpuSpec();

    private static ProductDto CreateSampleProductDto(Guid id, string name = "Core i7", bool isActive = true) =>
        new(id, Guid.NewGuid(), "CPUs", name, "core-i7", "SKU1", "Intel", 350m, 400m, 10, isActive, "img.jpg", ["img2.jpg"], ComponentType.Cpu, CreateCpuSpec(), DateTime.UtcNow);

    #region Attribute & Routing Reflection Tests

    [Fact]
    public void GetProducts_HasHttpGetAndAllowAnonymousAttributes()
    {
        var method = typeof(ProductsController).GetMethod(nameof(ProductsController.GetProducts));
        method.Should().NotBeNull();

        method!.GetCustomAttribute<HttpGetAttribute>().Should().NotBeNull();
        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
    }

    [Fact]
    public void GetBySlug_HasHttpGetWithSlugTemplateAndAllowAnonymousAttributes()
    {
        var method = typeof(ProductsController).GetMethod(nameof(ProductsController.GetBySlug));
        method.Should().NotBeNull();

        var httpGet = method!.GetCustomAttribute<HttpGetAttribute>();
        httpGet.Should().NotBeNull();
        httpGet!.Template.Should().Be("slug/{slug}");

        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
    }

    [Fact]
    public void GetById_HasHttpGetWithIdTemplateAndAuthorizeWithContentManagerPolicy()
    {
        var method = typeof(ProductsController).GetMethod(nameof(ProductsController.GetById));
        method.Should().NotBeNull();

        var httpGet = method!.GetCustomAttribute<HttpGetAttribute>();
        httpGet.Should().NotBeNull();
        httpGet!.Template.Should().Be("{id:guid}");

        var auth = method.GetCustomAttribute<AuthorizeAttribute>();
        auth.Should().NotBeNull();
        auth!.Policy.Should().Be(Policies.ContentManager);
    }

    [Fact]
    public void Create_HasHttpPostAndAuthorizeWithContentManagerPolicy()
    {
        var method = typeof(ProductsController).GetMethod(nameof(ProductsController.Create));
        method.Should().NotBeNull();

        method!.GetCustomAttribute<HttpPostAttribute>().Should().NotBeNull();

        var auth = method.GetCustomAttribute<AuthorizeAttribute>();
        auth.Should().NotBeNull();
        auth!.Policy.Should().Be(Policies.ContentManager);
    }

    [Fact]
    public void Update_HasHttpPutWithIdTemplateAndAuthorizeWithContentManagerPolicy()
    {
        var method = typeof(ProductsController).GetMethod(nameof(ProductsController.Update));
        method.Should().NotBeNull();

        var httpPut = method!.GetCustomAttribute<HttpPutAttribute>();
        httpPut.Should().NotBeNull();
        httpPut!.Template.Should().Be("{id:guid}");

        var auth = method.GetCustomAttribute<AuthorizeAttribute>();
        auth.Should().NotBeNull();
        auth!.Policy.Should().Be(Policies.ContentManager);
    }

    [Fact]
    public void ProductsController_HasNoDeleteOrSetActiveEndpoints()
    {
        var methods = typeof(ProductsController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        methods.Should().NotContain(m => m.GetCustomAttribute<HttpDeleteAttribute>() != null);
        methods.Should().NotContain(m => m.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase));
        methods.Should().NotContain(m => m.Name.Contains("SetActive", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetProducts_DeclaresProducesResponseTypeNotFound()
    {
        var method = typeof(ProductsController).GetMethod(nameof(ProductsController.GetProducts));
        method.Should().NotBeNull();

        var produces = method!.GetCustomAttributes<ProducesResponseTypeAttribute>();
        produces.Should().Contain(a => a.StatusCode == StatusCodes.Status404NotFound);
    }

    [Fact]
    public void Create_DeclaresProducesResponseTypeNotFound()
    {
        var method = typeof(ProductsController).GetMethod(nameof(ProductsController.Create));
        method.Should().NotBeNull();

        var produces = method!.GetCustomAttributes<ProducesResponseTypeAttribute>();
        produces.Should().Contain(a => a.StatusCode == StatusCodes.Status404NotFound);
    }

    #endregion

    #region Action Mapping & Behavior Tests

    [Fact]
    public async Task GetProducts_DefaultFilter_SendsIsActiveNullAndCorrectDefaults_AndForwardsCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var pagedResult = PagedResult<ProductSummaryDto>.Create([], 0, 1, 20);

        _senderMock.Setup(s => s.Send(
                It.Is<GetProductsQuery>(q =>
                    q.Options.PageIndex == 1 &&
                    q.Options.PageSize == 20 &&
                    q.Options.SearchTerm == null &&
                    q.Options.CategoryId == null &&
                    q.Options.ComponentType == null &&
                    q.Options.Brand == null &&
                    q.Options.MinPrice == null &&
                    q.Options.MaxPrice == null &&
                    q.Options.InStock == null &&
                    q.Options.IsActive == null &&
                    q.Options.SortBy == ProductSortOption.Newest),
                cts.Token))
            .ReturnsAsync(Result.Success(pagedResult));

        var request = new ProductListRequest();
        var actionResult = await _controller.GetProducts(request, cts.Token);

        var okResult = actionResult as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public async Task GetProducts_WhenAll11FiltersSpecified_MapsAllFieldsExactlyWithoutOverwritingIsActive(bool? isActive)
    {
        var categoryId = Guid.NewGuid();
        var pagedResult = PagedResult<ProductSummaryDto>.Create([], 0, 2, 15);

        _senderMock.Setup(s => s.Send(
                It.Is<GetProductsQuery>(q =>
                    q.Options.PageIndex == 2 &&
                    q.Options.PageSize == 15 &&
                    q.Options.SearchTerm == "intel" &&
                    q.Options.CategoryId == categoryId &&
                    q.Options.ComponentType == ComponentType.Cpu &&
                    q.Options.Brand == "Intel" &&
                    q.Options.MinPrice == 100m &&
                    q.Options.MaxPrice == 500m &&
                    q.Options.InStock == true &&
                    q.Options.IsActive == isActive &&
                    q.Options.SortBy == ProductSortOption.PriceLowToHigh),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(pagedResult));

        var request = new ProductListRequest
        {
            PageIndex = 2,
            PageSize = 15,
            SearchTerm = "intel",
            CategoryId = categoryId,
            ComponentType = ComponentType.Cpu,
            Brand = "Intel",
            MinPrice = 100m,
            MaxPrice = 500m,
            InStock = true,
            IsActive = isActive,
            SortBy = ProductSortOption.PriceLowToHigh
        };

        var actionResult = await _controller.GetProducts(request, CancellationToken.None);

        var okResult = actionResult as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetBySlug_MapsSlug_ReturnsProductEvenIfInactive_WithoutControllerFiltering(bool isActive)
    {
        var productDto = CreateSampleProductDto(Guid.NewGuid(), "Test Slug", isActive: isActive);
        using var cts = new CancellationTokenSource();

        _senderMock.Setup(s => s.Send(It.Is<GetProductBySlugQuery>(q => q.Slug == "test-slug"), cts.Token))
            .ReturnsAsync(Result.Success(productDto));

        var actionResult = await _controller.GetBySlug("test-slug", cts.Token);

        var okResult = actionResult as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(StatusCodes.Status200OK);
        okResult.Value.Should().Be(productDto);
        ((ProductDto)okResult.Value!).IsActive.Should().Be(isActive);
    }

    [Fact]
    public async Task GetById_MapsIdAndForwardsCancellationToken_ReturnsOk()
    {
        var productId = Guid.NewGuid();
        var productDto = CreateSampleProductDto(productId);
        using var cts = new CancellationTokenSource();

        _senderMock.Setup(s => s.Send(It.Is<GetProductByIdQuery>(q => q.Id == productId), cts.Token))
            .ReturnsAsync(Result.Success(productDto));

        var actionResult = await _controller.GetById(productId, cts.Token);

        var okResult = actionResult as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(StatusCodes.Status200OK);
        okResult.Value.Should().Be(productDto);
    }

    [Fact]
    public async Task Create_MapsAllFieldsIncludingNullableAndSubtype_ReturnsCreatedAtAction()
    {
        var productId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var spec = CreateCpuSpec();
        var productDto = CreateSampleProductDto(productId);
        using var cts = new CancellationTokenSource();

        _senderMock.Setup(s => s.Send(
                It.Is<CreateProductCommand>(c =>
                    c.CategoryId == categoryId &&
                    c.Name == "Core i7" &&
                    c.Sku == "SKU-I7" &&
                    c.Brand == "Intel" &&
                    c.Price == 350m &&
                    c.OriginalPrice == 400m &&
                    c.StockQuantity == 10 &&
                    c.ImageUrl == "img.jpg" &&
                    c.AdditionalImages != null && c.AdditionalImages.Count == 1 && c.AdditionalImages[0] == "img2.jpg" &&
                    c.ComponentType == ComponentType.Cpu &&
                    c.Specifications == spec),
                cts.Token))
            .ReturnsAsync(Result.Success(productDto));

        var request = new CreateProductRequest(
            CategoryId: categoryId,
            Name: "Core i7",
            Sku: "SKU-I7",
            Brand: "Intel",
            Price: 350m,
            OriginalPrice: 400m,
            StockQuantity: 10,
            ImageUrl: "img.jpg",
            AdditionalImages: ["img2.jpg"],
            ComponentType: ComponentType.Cpu,
            Specifications: spec
        );

        var actionResult = await _controller.Create(request, cts.Token);

        var createdResult = actionResult as CreatedAtActionResult;
        createdResult.Should().NotBeNull();
        createdResult!.StatusCode.Should().Be(StatusCodes.Status201Created);
        createdResult.ActionName.Should().Be(nameof(ProductsController.GetById));
        createdResult.RouteValues.Should().ContainKey("id").WhoseValue.Should().Be(productId);
        createdResult.Value.Should().Be(productDto);

        _senderMock.Verify(s => s.Send(It.IsAny<CreateProductCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Update_PassesRouteIdAndAllFieldsIncludingIsActive_ReturnsOk(bool isActive)
    {
        var productId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var spec = CreateCpuSpec();
        var productDto = CreateSampleProductDto(productId, isActive: isActive);
        using var cts = new CancellationTokenSource();

        _senderMock.Setup(s => s.Send(
                It.Is<UpdateProductCommand>(c =>
                    c.Id == productId &&
                    c.CategoryId == categoryId &&
                    c.Name == "Updated Name" &&
                    c.Sku == "SKU-UPD" &&
                    c.Brand == "Intel" &&
                    c.Price == 300m &&
                    c.OriginalPrice == null &&
                    c.StockQuantity == 5 &&
                    c.IsActive == isActive &&
                    c.ImageUrl == "new-img.jpg" &&
                    c.AdditionalImages == null &&
                    c.ComponentType == ComponentType.Cpu &&
                    c.Specifications == spec),
                cts.Token))
            .ReturnsAsync(Result.Success(productDto));

        var request = new UpdateProductRequest(
            CategoryId: categoryId,
            Name: "Updated Name",
            Sku: "SKU-UPD",
            Brand: "Intel",
            Price: 300m,
            OriginalPrice: null,
            StockQuantity: 5,
            IsActive: isActive,
            ImageUrl: "new-img.jpg",
            AdditionalImages: null,
            ComponentType: ComponentType.Cpu,
            Specifications: spec
        );

        var actionResult = await _controller.Update(productId, request, cts.Token);

        var okResult = actionResult as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(StatusCodes.Status200OK);
        okResult.Value.Should().Be(productDto);
    }

    #endregion

    #region Result Error Mapping Tests

    [Fact]
    public async Task GetBySlug_WhenNotFound_ReturnsNotFoundStatus()
    {
        _senderMock.Setup(s => s.Send(It.IsAny<GetProductBySlugQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ProductDto>(Error.NotFound("Product.NotFound", "Product not found")));

        var actionResult = await _controller.GetBySlug("unknown-slug", CancellationToken.None);

        var notFoundResult = actionResult as NotFoundObjectResult;
        notFoundResult.Should().NotBeNull();
        notFoundResult!.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task GetById_WhenNotFound_ReturnsNotFoundStatus()
    {
        _senderMock.Setup(s => s.Send(It.IsAny<GetProductByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ProductDto>(Error.NotFound("Product.NotFound", "Product not found")));

        var actionResult = await _controller.GetById(Guid.NewGuid(), CancellationToken.None);

        var notFoundResult = actionResult as NotFoundObjectResult;
        notFoundResult.Should().NotBeNull();
        notFoundResult!.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task Create_WhenConflict_ReturnsConflictStatus_AndDoesNotAccessValue()
    {
        _senderMock.Setup(s => s.Send(It.IsAny<CreateProductCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ProductDto>(Error.Conflict("Product.SkuConflict", "SKU already exists")));

        var request = new CreateProductRequest(
            CategoryId: Guid.NewGuid(),
            Name: "Name",
            Sku: "SKU1",
            Brand: "Brand",
            Price: 100,
            OriginalPrice: null,
            StockQuantity: 1,
            ImageUrl: "img.jpg",
            AdditionalImages: null,
            ComponentType: ComponentType.Cpu,
            Specifications: CreateCpuSpec()
        );

        var actionResult = await _controller.Create(request, CancellationToken.None);

        var conflictResult = actionResult as ConflictObjectResult;
        conflictResult.Should().NotBeNull();
        conflictResult!.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task Update_WhenConflict_ReturnsConflictStatus()
    {
        _senderMock.Setup(s => s.Send(It.IsAny<UpdateProductCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ProductDto>(Error.Conflict("Product.SkuConflict", "SKU already exists")));

        var request = new UpdateProductRequest(
            CategoryId: Guid.NewGuid(),
            Name: "Name",
            Sku: "SKU1",
            Brand: "Brand",
            Price: 100,
            OriginalPrice: null,
            StockQuantity: 1,
            IsActive: true,
            ImageUrl: "img.jpg",
            AdditionalImages: null,
            ComponentType: ComponentType.Cpu,
            Specifications: CreateCpuSpec()
        );

        var actionResult = await _controller.Update(Guid.NewGuid(), request, CancellationToken.None);

        var conflictResult = actionResult as ConflictObjectResult;
        conflictResult.Should().NotBeNull();
        conflictResult!.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task Create_WhenValidationError_ReturnsBadRequestStatus()
    {
        _senderMock.Setup(s => s.Send(It.IsAny<CreateProductCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ProductDto>(Error.Validation("Product.Validation", "Invalid product data")));

        var request = new CreateProductRequest(
            CategoryId: Guid.NewGuid(),
            Name: "",
            Sku: "SKU1",
            Brand: "Brand",
            Price: 100,
            OriginalPrice: null,
            StockQuantity: 1,
            ImageUrl: "img.jpg",
            AdditionalImages: null,
            ComponentType: ComponentType.Cpu,
            Specifications: CreateCpuSpec()
        );

        var actionResult = await _controller.Create(request, CancellationToken.None);

        var badRequestResult = actionResult as BadRequestObjectResult;
        badRequestResult.Should().NotBeNull();
        badRequestResult!.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task GetById_WhenUnauthorized_ReturnsUnauthorizedStatus()
    {
        _senderMock.Setup(s => s.Send(It.IsAny<GetProductByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ProductDto>(Error.Unauthorized("Auth.Unauthorized", "Unauthorized")));

        var actionResult = await _controller.GetById(Guid.NewGuid(), CancellationToken.None);

        var unauthorizedResult = actionResult as UnauthorizedObjectResult;
        unauthorizedResult.Should().NotBeNull();
        unauthorizedResult!.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task GetById_WhenForbidden_ReturnsForbiddenStatus()
    {
        _senderMock.Setup(s => s.Send(It.IsAny<GetProductByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ProductDto>(Error.Forbidden("Auth.Forbidden", "Forbidden")));

        var actionResult = await _controller.GetById(Guid.NewGuid(), CancellationToken.None);

        var forbiddenResult = actionResult as ObjectResult;
        forbiddenResult.Should().NotBeNull();
        forbiddenResult!.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task GetProducts_WhenCategoryNotFound_ReturnsNotFoundStatus_WithErrorCodeAndDescription()
    {
        using var cts = new CancellationTokenSource();
        var categoryId = Guid.NewGuid();
        var request = new ProductListRequest { CategoryId = categoryId };

        _senderMock.Setup(s => s.Send(It.Is<GetProductsQuery>(q => q.Options.CategoryId == categoryId), cts.Token))
            .ReturnsAsync(Result.Failure<PagedResult<ProductSummaryDto>>(Error.NotFound("Product.CategoryNotFound", "Category not found")));

        var actionResult = await _controller.GetProducts(request, cts.Token);

        var notFoundResult = actionResult as NotFoundObjectResult;
        notFoundResult.Should().NotBeNull();
        notFoundResult!.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        notFoundResult.Value.Should().BeEquivalentTo(new { Code = "Product.CategoryNotFound", Description = "Category not found" });

        _senderMock.Verify(s => s.Send(It.IsAny<GetProductsQuery>(), cts.Token), Times.Once);
    }

    [Fact]
    public async Task Create_WhenCategoryNotFound_ReturnsNotFoundStatus_DoesNotReturnCreatedAtAction_AndDoesNotAccessValue()
    {
        using var cts = new CancellationTokenSource();
        var categoryId = Guid.NewGuid();
        var request = new CreateProductRequest(
            CategoryId: categoryId,
            Name: "Core i7",
            Sku: "SKU-I7",
            Brand: "Intel",
            Price: 350m,
            OriginalPrice: 400m,
            StockQuantity: 10,
            ImageUrl: "img.jpg",
            AdditionalImages: null,
            ComponentType: ComponentType.Cpu,
            Specifications: CreateCpuSpec()
        );

        _senderMock.Setup(s => s.Send(It.Is<CreateProductCommand>(c => c.CategoryId == categoryId), cts.Token))
            .ReturnsAsync(Result.Failure<ProductDto>(Error.NotFound("Product.CategoryNotFound", "Category not found")));

        var actionResult = await _controller.Create(request, cts.Token);

        actionResult.Should().NotBeOfType<CreatedAtActionResult>();
        var notFoundResult = actionResult as NotFoundObjectResult;
        notFoundResult.Should().NotBeNull();
        notFoundResult!.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        notFoundResult.Value.Should().BeEquivalentTo(new { Code = "Product.CategoryNotFound", Description = "Category not found" });

        _senderMock.Verify(s => s.Send(It.IsAny<CreateProductCommand>(), cts.Token), Times.Once);
    }

    #endregion
}
