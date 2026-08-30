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
using RiuTek.Application.Features.Posts.Commands;
using RiuTek.Application.Features.Posts.Queries;
using RiuTek.Core.Common;
using RiuTek.Core.Constants;

namespace RiuTek.Application.Test.Controllers;

public class PostsControllerContractTests
{
    private static (PostsController controller, Mock<ISender> senderMock) CreateController()
    {
        var senderMock = new Mock<ISender>();
        var services = new ServiceCollection();
        services.AddScoped<ISender>(_ => senderMock.Object);
        var serviceProvider = services.BuildServiceProvider();

        var controller = new PostsController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = serviceProvider
                }
            }
        };

        return (controller, senderMock);
    }

    #region Attribute & Routing Reflection Tests

    [Fact]
    public void GetPosts_HasHttpGetAndAllowAnonymousAttributes()
    {
        var method = typeof(PostsController).GetMethod(nameof(PostsController.GetPosts));
        method.Should().NotBeNull();

        method!.GetCustomAttribute<HttpGetAttribute>().Should().NotBeNull();
        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
    }

    [Fact]
    public void GetBySlug_HasHttpGetWithSlugTemplateAndAllowAnonymousAttributes()
    {
        var method = typeof(PostsController).GetMethod(nameof(PostsController.GetBySlug));
        method.Should().NotBeNull();

        var httpGet = method!.GetCustomAttribute<HttpGetAttribute>();
        httpGet.Should().NotBeNull();
        httpGet!.Template.Should().Be("slug/{slug}");

        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
    }

    [Fact]
    public void GetById_HasHttpGetAndAuthorizeWithContentManagerPolicy()
    {
        var method = typeof(PostsController).GetMethod(nameof(PostsController.GetById));
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
        var method = typeof(PostsController).GetMethod(nameof(PostsController.Create));
        method.Should().NotBeNull();

        method!.GetCustomAttribute<HttpPostAttribute>().Should().NotBeNull();

        var auth = method.GetCustomAttribute<AuthorizeAttribute>();
        auth.Should().NotBeNull();
        auth!.Policy.Should().Be(Policies.ContentManager);
    }

    [Fact]
    public void Update_HasHttpPutWithIdTemplateAndAuthorizeWithContentManagerPolicy()
    {
        var method = typeof(PostsController).GetMethod(nameof(PostsController.Update));
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
        var method = typeof(PostsController).GetMethod(nameof(PostsController.Delete));
        method.Should().NotBeNull();

        var httpDelete = method!.GetCustomAttribute<HttpDeleteAttribute>();
        httpDelete.Should().NotBeNull();
        httpDelete!.Template.Should().Be("{id:guid}");

        var auth = method.GetCustomAttribute<AuthorizeAttribute>();
        auth.Should().NotBeNull();
        auth!.Policy.Should().Be(Policies.ContentManager);
    }

    #endregion

    #region Action Behavior & Parameter Mapping Tests

    [Fact]
    public async Task GetPosts_AlwaysSendsIsPublishedOnlyTrue_AndForwardsCancellationToken()
    {
        var (controller, senderMock) = CreateController();
        var pagedResult = PagedResult<PostSummaryDto>.Create([], 0, 1, 10);
        using var cts = new CancellationTokenSource();

        senderMock.Setup(s => s.Send(
                It.Is<GetPostsQuery>(q => q.IsPublishedOnly == true && q.PageIndex == 2 && q.PageSize == 15 && q.SearchTerm == "tech" && q.IsFeaturedOnly == true),
                cts.Token))
            .ReturnsAsync(Result.Success(pagedResult));

        var actionResult = await controller.GetPosts(2, 15, "tech", true, cts.Token);

        var okResult = actionResult as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task Create_MapsBodyAndReturnsCreatedAtAction_WithCorrectMetadataAndPayload()
    {
        var (controller, senderMock) = CreateController();
        var postId = Guid.NewGuid();
        var postDto = new PostDto(postId, "Title", "slug", "Summ", "Cont", null, Guid.NewGuid(), "Author", 0, true, false, DateTime.UtcNow, DateTime.UtcNow, null);

        senderMock.Setup(s => s.Send(
                It.Is<CreatePostCommand>(c => c.Title == "Title" && c.Summary == "Summ" && c.Content == "Cont" && c.IsPublished == true),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(postDto));

        var request = new CreatePostRequest("Title", "Summ", "Cont", null, true, false);
        var actionResult = await controller.Create(request, CancellationToken.None);

        var createdResult = actionResult as CreatedAtActionResult;
        createdResult.Should().NotBeNull();
        createdResult!.StatusCode.Should().Be(StatusCodes.Status201Created);
        createdResult.ActionName.Should().Be(nameof(PostsController.GetById));
        createdResult.RouteValues.Should().ContainKey("id").WhoseValue.Should().Be(postId);
        createdResult.Value.Should().Be(postDto);
    }

    [Fact]
    public async Task Update_PassesRouteIdAndBodyFields()
    {
        var (controller, senderMock) = CreateController();
        var postId = Guid.NewGuid();
        var postDto = new PostDto(postId, "Updated", "slug", "Summ", "Cont", null, Guid.NewGuid(), "Author", 0, true, false, DateTime.UtcNow, DateTime.UtcNow, null);

        senderMock.Setup(s => s.Send(
                It.Is<UpdatePostCommand>(c => c.Id == postId && c.Title == "Updated"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(postDto));

        var request = new UpdatePostRequest("Updated", "Summ", "Cont", null, true, false);
        var actionResult = await controller.Update(postId, request, CancellationToken.None);

        var okResult = actionResult as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent()
    {
        var (controller, senderMock) = CreateController();
        var postId = Guid.NewGuid();

        senderMock.Setup(s => s.Send(
                It.Is<DeletePostCommand>(c => c.Id == postId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(Unit.Value));

        var actionResult = await controller.Delete(postId, CancellationToken.None);

        var noContentResult = actionResult as NoContentResult;
        noContentResult.Should().NotBeNull();
        noContentResult!.StatusCode.Should().Be(StatusCodes.Status204NoContent);
    }

    [Fact]
    public async Task GetBySlug_WhenNotFound_ReturnsNotFoundStatus()
    {
        var (controller, senderMock) = CreateController();
        senderMock.Setup(s => s.Send(
                It.Is<GetPostBySlugQuery>(q => q.Slug == "missing-slug"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<PostDto>(Error.NotFound("Post.NotFound", "Not found")));

        var actionResult = await controller.GetBySlug("missing-slug", CancellationToken.None);

        var notFoundResult = actionResult as NotFoundObjectResult;
        notFoundResult.Should().NotBeNull();
        notFoundResult!.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task Create_WhenForbidden_ReturnsForbiddenStatus()
    {
        var (controller, senderMock) = CreateController();
        senderMock.Setup(s => s.Send(
                It.IsAny<CreatePostCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<PostDto>(Error.Forbidden("Post.Forbidden", "Forbidden")));

        var request = new CreatePostRequest("Title", "Summ", "Cont", null, true, false);
        var actionResult = await controller.Create(request, CancellationToken.None);

        var forbiddenResult = actionResult as ObjectResult;
        forbiddenResult.Should().NotBeNull();
        forbiddenResult!.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Create_WhenValidationError_ReturnsBadRequestStatus()
    {
        var (controller, senderMock) = CreateController();
        senderMock.Setup(s => s.Send(
                It.IsAny<CreatePostCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<PostDto>(Error.Validation("Post.Validation", "Invalid title")));

        var request = new CreatePostRequest("", "Summ", "Cont", null, true, false);
        var actionResult = await controller.Create(request, CancellationToken.None);

        var badRequestResult = actionResult as BadRequestObjectResult;
        badRequestResult.Should().NotBeNull();
        badRequestResult!.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    #endregion
}
