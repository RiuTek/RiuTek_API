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
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Categories.Commands;
using RiuTek.Application.Features.Categories.Queries;
using RiuTek.Core.Common;
using RiuTek.Core.Constants;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Test.Controllers;

public class CategoriesControllerContractTests : IDisposable
{
    private readonly Mock<ISender> _senderMock;
    private readonly ServiceProvider _serviceProvider;
    private readonly CategoriesController _controller;

    public CategoriesControllerContractTests()
    {
        _senderMock = new Mock<ISender>();
        var services = new ServiceCollection();
        services.AddScoped<ISender>(_ => _senderMock.Object);
        _serviceProvider = services.BuildServiceProvider();

        _controller = new CategoriesController
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

    #region Attribute & Routing Reflection Tests

    [Fact]
    public void GetTree_HasHttpGetAndAllowAnonymousAttributes()
    {
        var method = typeof(CategoriesController).GetMethod(nameof(CategoriesController.GetTree));
        method.Should().NotBeNull();

        method!.GetCustomAttribute<HttpGetAttribute>().Should().NotBeNull();
        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
    }

    [Fact]
    public void GetById_HasHttpGetWithIdTemplateAndAllowAnonymousAttributes()
    {
        var method = typeof(CategoriesController).GetMethod(nameof(CategoriesController.GetById));
        method.Should().NotBeNull();

        var httpGet = method!.GetCustomAttribute<HttpGetAttribute>();
        httpGet.Should().NotBeNull();
        httpGet!.Template.Should().Be("{id:guid}");

        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
    }

    [Fact]
    public void Create_HasHttpPostAndAuthorizeWithContentManagerPolicy()
    {
        var method = typeof(CategoriesController).GetMethod(nameof(CategoriesController.Create));
        method.Should().NotBeNull();

        method!.GetCustomAttribute<HttpPostAttribute>().Should().NotBeNull();

        var auth = method.GetCustomAttribute<AuthorizeAttribute>();
        auth.Should().NotBeNull();
        auth!.Policy.Should().Be(Policies.ContentManager);
    }

    [Fact]
    public void Update_HasHttpPutWithIdTemplateAndAuthorizeWithContentManagerPolicy()
    {
        var method = typeof(CategoriesController).GetMethod(nameof(CategoriesController.Update));
        method.Should().NotBeNull();

        var httpPut = method!.GetCustomAttribute<HttpPutAttribute>();
        httpPut.Should().NotBeNull();
        httpPut!.Template.Should().Be("{id:guid}");

        var auth = method.GetCustomAttribute<AuthorizeAttribute>();
        auth.Should().NotBeNull();
        auth!.Policy.Should().Be(Policies.ContentManager);
    }

    [Fact]
    public void Delete_HasHttpDeleteWithIdTemplateAndAuthorizeWithContentManagerPolicy()
    {
        var method = typeof(CategoriesController).GetMethod(nameof(CategoriesController.Delete));
        method.Should().NotBeNull();

        var httpDelete = method!.GetCustomAttribute<HttpDeleteAttribute>();
        httpDelete.Should().NotBeNull();
        httpDelete!.Template.Should().Be("{id:guid}");

        var auth = method.GetCustomAttribute<AuthorizeAttribute>();
        auth.Should().NotBeNull();
        auth!.Policy.Should().Be(Policies.ContentManager);
    }

    [Fact]
    public void GetTree_DeclaresProducesResponseTypeBadRequest()
    {
        var method = typeof(CategoriesController).GetMethod(nameof(CategoriesController.GetTree));
        method.Should().NotBeNull();

        var produces = method!.GetCustomAttributes<ProducesResponseTypeAttribute>();
        produces.Should().Contain(a => a.StatusCode == StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void Create_DeclaresProducesResponseTypeNotFound()
    {
        var method = typeof(CategoriesController).GetMethod(nameof(CategoriesController.Create));
        method.Should().NotBeNull();

        var produces = method!.GetCustomAttributes<ProducesResponseTypeAttribute>();
        produces.Should().Contain(a => a.StatusCode == StatusCodes.Status404NotFound);
    }

    #endregion

    #region Action Mapping & Behavior Tests

    [Fact]
    public async Task GetTree_WhenEmpty_ReturnsOkWithEmptyList_AndForwardsCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        _senderMock.Setup(s => s.Send(It.IsAny<GetCategoryTreeQuery>(), cts.Token))
            .ReturnsAsync(Result.Success<List<CategoryDto>>([]));

        var actionResult = await _controller.GetTree(cts.Token);

        var okResult = actionResult as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(StatusCodes.Status200OK);
        okResult.Value.Should().BeEquivalentTo(new List<CategoryDto>());
    }

    [Fact]
    public async Task GetById_MapsIdAndForwardsCancellationToken_ReturnsOk()
    {
        var categoryId = Guid.NewGuid();
        var categoryDto = new CategoryDto(categoryId, "CPUs", "cpus", ComponentType.Cpu, "Processors", null, []);
        using var cts = new CancellationTokenSource();

        _senderMock.Setup(s => s.Send(It.Is<GetCategoryByIdQuery>(q => q.Id == categoryId), cts.Token))
            .ReturnsAsync(Result.Success(categoryDto));

        var actionResult = await _controller.GetById(categoryId, cts.Token);

        var okResult = actionResult as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(StatusCodes.Status200OK);
        okResult.Value.Should().Be(categoryDto);
    }

    [Fact]
    public async Task Create_MapsAllFieldsAndReturnsCreatedAtAction_WithLocationAndPayload()
    {
        var categoryId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var categoryDto = new CategoryDto(categoryId, "Intel CPUs", "intel-cpus", ComponentType.Cpu, "Intel chips", parentId, []);
        using var cts = new CancellationTokenSource();

        _senderMock.Setup(s => s.Send(
                It.Is<CreateCategoryCommand>(c =>
                    c.Name == "Intel CPUs" &&
                    c.ComponentType == ComponentType.Cpu &&
                    c.Description == "Intel chips" &&
                    c.ParentId == parentId),
                cts.Token))
            .ReturnsAsync(Result.Success(categoryDto));

        var request = new CreateCategoryRequest("Intel CPUs", ComponentType.Cpu, "Intel chips", parentId);
        var actionResult = await _controller.Create(request, cts.Token);

        var createdResult = actionResult as CreatedAtActionResult;
        createdResult.Should().NotBeNull();
        createdResult!.StatusCode.Should().Be(StatusCodes.Status201Created);
        createdResult.ActionName.Should().Be(nameof(CategoriesController.GetById));
        createdResult.RouteValues.Should().ContainKey("id").WhoseValue.Should().Be(categoryId);
        createdResult.Value.Should().Be(categoryDto);
    }

    [Fact]
    public async Task Update_PassesRouteIdAndBodyFields_ReturnsOk()
    {
        var categoryId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var categoryDto = new CategoryDto(categoryId, "Updated CPUs", "updated-cpus", ComponentType.Cpu, "New Desc", parentId, []);
        using var cts = new CancellationTokenSource();

        _senderMock.Setup(s => s.Send(
                It.Is<UpdateCategoryCommand>(c =>
                    c.Id == categoryId &&
                    c.Name == "Updated CPUs" &&
                    c.ComponentType == ComponentType.Cpu &&
                    c.Description == "New Desc" &&
                    c.ParentId == parentId),
                cts.Token))
            .ReturnsAsync(Result.Success(categoryDto));

        var request = new UpdateCategoryRequest("Updated CPUs", ComponentType.Cpu, "New Desc", parentId);
        var actionResult = await _controller.Update(categoryId, request, cts.Token);

        var okResult = actionResult as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(StatusCodes.Status200OK);
        okResult.Value.Should().Be(categoryDto);
    }

    [Fact]
    public async Task Delete_MapsId_ReturnsNoContent()
    {
        var categoryId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();

        _senderMock.Setup(s => s.Send(It.Is<DeleteCategoryCommand>(c => c.Id == categoryId), cts.Token))
            .ReturnsAsync(Result.Success(Unit.Value));

        var actionResult = await _controller.Delete(categoryId, cts.Token);

        var noContentResult = actionResult as NoContentResult;
        noContentResult.Should().NotBeNull();
        noContentResult!.StatusCode.Should().Be(StatusCodes.Status204NoContent);
    }

    #endregion

    #region Result Error Mapping Tests

    [Fact]
    public async Task GetById_WhenNotFound_ReturnsNotFoundStatus()
    {
        var categoryId = Guid.NewGuid();
        _senderMock.Setup(s => s.Send(It.Is<GetCategoryByIdQuery>(q => q.Id == categoryId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<CategoryDto>(Error.NotFound("Category.NotFound", "Category not found")));

        var actionResult = await _controller.GetById(categoryId, CancellationToken.None);

        var notFoundResult = actionResult as NotFoundObjectResult;
        notFoundResult.Should().NotBeNull();
        notFoundResult!.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task Create_WhenConflict_ReturnsConflictStatus()
    {
        _senderMock.Setup(s => s.Send(It.IsAny<CreateCategoryCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<CategoryDto>(Error.Conflict("Category.SlugConflict", "Slug already exists")));

        var request = new CreateCategoryRequest("CPUs", ComponentType.Cpu);
        var actionResult = await _controller.Create(request, CancellationToken.None);

        var conflictResult = actionResult as ConflictObjectResult;
        conflictResult.Should().NotBeNull();
        conflictResult!.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task Create_WhenValidationError_ReturnsBadRequestStatus()
    {
        _senderMock.Setup(s => s.Send(It.IsAny<CreateCategoryCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<CategoryDto>(Error.Validation("Category.Validation", "Invalid name")));

        var request = new CreateCategoryRequest("", ComponentType.Cpu);
        var actionResult = await _controller.Create(request, CancellationToken.None);

        var badRequestResult = actionResult as BadRequestObjectResult;
        badRequestResult.Should().NotBeNull();
        badRequestResult!.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task Update_WhenUnauthorized_ReturnsUnauthorizedStatus()
    {
        var categoryId = Guid.NewGuid();
        _senderMock.Setup(s => s.Send(It.IsAny<UpdateCategoryCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<CategoryDto>(Error.Unauthorized("Auth.Unauthorized", "Unauthorized")));

        var request = new UpdateCategoryRequest("CPUs", ComponentType.Cpu);
        var actionResult = await _controller.Update(categoryId, request, CancellationToken.None);

        var unauthorizedResult = actionResult as UnauthorizedObjectResult;
        unauthorizedResult.Should().NotBeNull();
        unauthorizedResult!.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task Update_WhenForbidden_ReturnsForbiddenStatus()
    {
        var categoryId = Guid.NewGuid();
        _senderMock.Setup(s => s.Send(It.IsAny<UpdateCategoryCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<CategoryDto>(Error.Forbidden("Auth.Forbidden", "Forbidden")));

        var request = new UpdateCategoryRequest("CPUs", ComponentType.Cpu);
        var actionResult = await _controller.Update(categoryId, request, CancellationToken.None);

        var forbiddenResult = actionResult as ObjectResult;
        forbiddenResult.Should().NotBeNull();
        forbiddenResult!.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Delete_WhenConflictHasProductsOrChildren_ReturnsConflictStatus_DoesNotReturnNoContent()
    {
        var categoryId = Guid.NewGuid();
        _senderMock.Setup(s => s.Send(It.Is<DeleteCategoryCommand>(c => c.Id == categoryId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Unit>(Error.Conflict("Category.CannotDeleteWithChildren", "Cannot delete category with sub-categories or products")));

        var actionResult = await _controller.Delete(categoryId, CancellationToken.None);

        actionResult.Should().NotBeOfType<NoContentResult>();
        var conflictResult = actionResult as ConflictObjectResult;
        conflictResult.Should().NotBeNull();
        conflictResult!.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Theory]
    [InlineData("Category.InvalidHierarchy", "Invalid category hierarchy structure")]
    [InlineData("Category.CycleDetected", "Cycle detected in category hierarchy")]
    public async Task GetTree_WhenInvalidHierarchyOrCycle_ReturnsBadRequestStatus_WithErrorCodeAndDescription(string errorCode, string errorDescription)
    {
        using var cts = new CancellationTokenSource();
        _senderMock.Setup(s => s.Send(It.IsAny<GetCategoryTreeQuery>(), cts.Token))
            .ReturnsAsync(Result.Failure<List<CategoryDto>>(Error.Validation(errorCode, errorDescription)));

        var actionResult = await _controller.GetTree(cts.Token);

        var badRequestResult = actionResult as BadRequestObjectResult;
        badRequestResult.Should().NotBeNull();
        badRequestResult!.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        badRequestResult.Value.Should().BeEquivalentTo(new { Code = errorCode, Description = errorDescription });

        _senderMock.Verify(s => s.Send(It.IsAny<GetCategoryTreeQuery>(), cts.Token), Times.Once);
    }

    [Fact]
    public async Task Create_WhenParentNotFound_ReturnsNotFoundStatus_DoesNotReturnCreatedAtAction()
    {
        using var cts = new CancellationTokenSource();
        var parentId = Guid.NewGuid();
        var request = new CreateCategoryRequest("Child Category", ComponentType.Cpu, "Desc", parentId);

        _senderMock.Setup(s => s.Send(It.Is<CreateCategoryCommand>(c => c.ParentId == parentId), cts.Token))
            .ReturnsAsync(Result.Failure<CategoryDto>(Error.NotFound("Category.ParentNotFound", "Parent category not found")));

        var actionResult = await _controller.Create(request, cts.Token);

        actionResult.Should().NotBeOfType<CreatedAtActionResult>();
        var notFoundResult = actionResult as NotFoundObjectResult;
        notFoundResult.Should().NotBeNull();
        notFoundResult!.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        notFoundResult.Value.Should().BeEquivalentTo(new { Code = "Category.ParentNotFound", Description = "Parent category not found" });

        _senderMock.Verify(s => s.Send(It.IsAny<CreateCategoryCommand>(), cts.Token), Times.Once);
    }

    #endregion
}
