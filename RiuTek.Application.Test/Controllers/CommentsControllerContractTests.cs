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
using RiuTek.Application.Features.Comments.Commands;
using RiuTek.Application.Features.Comments.Queries;
using RiuTek.Core.Common;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Test.Controllers;

public class CommentsControllerContractTests
{
    private static (CommentsController controller, Mock<ISender> senderMock) CreateController()
    {
        var senderMock = new Mock<ISender>();
        var services = new ServiceCollection();
        services.AddScoped<ISender>(_ => senderMock.Object);
        var serviceProvider = services.BuildServiceProvider();

        var controller = new CommentsController
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

    #region Attribute & Authorization Reflection Tests

    [Fact]
    public void GetPostComments_HasHttpGetAndAllowAnonymousAttributes()
    {
        var method = typeof(CommentsController).GetMethod(nameof(CommentsController.GetPostComments));
        method.Should().NotBeNull();

        var httpGet = method!.GetCustomAttribute<HttpGetAttribute>();
        httpGet.Should().NotBeNull();
        httpGet!.Template.Should().Be("posts/{postId:guid}");

        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
    }

    [Fact]
    public void CreatePostComment_HasHttpPostAndAuthorizeAttributes()
    {
        var method = typeof(CommentsController).GetMethod(nameof(CommentsController.CreatePostComment));
        method.Should().NotBeNull();

        var httpPost = method!.GetCustomAttribute<HttpPostAttribute>();
        httpPost.Should().NotBeNull();
        httpPost!.Template.Should().Be("posts/{postId:guid}");

        method.GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
    }

    [Fact]
    public void DeletePostComment_HasHttpDeleteAndAuthorizeAttributes()
    {
        var method = typeof(CommentsController).GetMethod(nameof(CommentsController.DeletePostComment));
        method.Should().NotBeNull();

        var httpDelete = method!.GetCustomAttribute<HttpDeleteAttribute>();
        httpDelete.Should().NotBeNull();
        httpDelete!.Template.Should().Be("posts/{commentId:guid}");

        method.GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
    }

    [Fact]
    public void GetProductComments_HasHttpGetAndAllowAnonymousAttributes()
    {
        var method = typeof(CommentsController).GetMethod(nameof(CommentsController.GetProductComments));
        method.Should().NotBeNull();

        var httpGet = method!.GetCustomAttribute<HttpGetAttribute>();
        httpGet.Should().NotBeNull();
        httpGet!.Template.Should().Be("products/{productId:guid}");

        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
    }

    [Fact]
    public void CreateProductComment_HasHttpPostAndAuthorizeAttributes()
    {
        var method = typeof(CommentsController).GetMethod(nameof(CommentsController.CreateProductComment));
        method.Should().NotBeNull();

        var httpPost = method!.GetCustomAttribute<HttpPostAttribute>();
        httpPost.Should().NotBeNull();
        httpPost!.Template.Should().Be("products/{productId:guid}");

        method.GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
    }

    [Fact]
    public void DeleteProductComment_HasHttpDeleteAndAuthorizeAttributes()
    {
        var method = typeof(CommentsController).GetMethod(nameof(CommentsController.DeleteProductComment));
        method.Should().NotBeNull();

        var httpDelete = method!.GetCustomAttribute<HttpDeleteAttribute>();
        httpDelete.Should().NotBeNull();
        httpDelete!.Template.Should().Be("products/{commentId:guid}");

        method.GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
    }

    #endregion

    #region Action Behavior & Parameter Mapping Tests

    [Fact]
    public async Task CreatePostComment_PassesRoutePostIdBodyContentAndParentCommentId_AndForwardsCancellationToken()
    {
        var (controller, senderMock) = CreateController();
        var postId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var commentDto = new PostCommentDto(Guid.NewGuid(), postId, Guid.NewGuid(), "User", "Hello Post", parentId, DateTime.UtcNow, null, []);
        using var cts = new CancellationTokenSource();

        senderMock.Setup(s => s.Send(
                It.Is<CreatePostCommentCommand>(c => c.PostId == postId && c.Content == "Hello Post" && c.ParentCommentId == parentId),
                cts.Token))
            .ReturnsAsync(Result.Success(commentDto));

        var request = new CreateCommentRequest("Hello Post", parentId);
        var actionResult = await controller.CreatePostComment(postId, request, cts.Token);

        var createdResult = actionResult as ObjectResult;
        createdResult.Should().NotBeNull();
        createdResult!.StatusCode.Should().Be(StatusCodes.Status201Created);
        createdResult.Value.Should().Be(commentDto);
    }

    [Fact]
    public async Task DeletePostComment_CreatesCommandWithPostTargetType()
    {
        var (controller, senderMock) = CreateController();
        var commentId = Guid.NewGuid();

        senderMock.Setup(s => s.Send(
                It.Is<DeleteCommentCommand>(c => c.Id == commentId && c.TargetType == CommentTargetType.Post),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(Unit.Value));

        var actionResult = await controller.DeletePostComment(commentId, CancellationToken.None);

        var noContentResult = actionResult as NoContentResult;
        noContentResult.Should().NotBeNull();
        noContentResult!.StatusCode.Should().Be(StatusCodes.Status204NoContent);
    }

    [Fact]
    public async Task CreateProductComment_PassesRouteProductIdBodyContentAndParentCommentId()
    {
        var (controller, senderMock) = CreateController();
        var productId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var commentDto = new ProductCommentDto(Guid.NewGuid(), productId, Guid.NewGuid(), "User", "Hello Product", parentId, false, DateTime.UtcNow, []);

        senderMock.Setup(s => s.Send(
                It.Is<CreateProductCommentCommand>(c => c.ProductId == productId && c.Content == "Hello Product" && c.ParentCommentId == parentId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(commentDto));

        var request = new CreateCommentRequest("Hello Product", parentId);
        var actionResult = await controller.CreateProductComment(productId, request, CancellationToken.None);

        var createdResult = actionResult as ObjectResult;
        createdResult.Should().NotBeNull();
        createdResult!.StatusCode.Should().Be(StatusCodes.Status201Created);
        createdResult.Value.Should().Be(commentDto);
    }

    [Fact]
    public async Task DeleteProductComment_CreatesCommandWithProductTargetType()
    {
        var (controller, senderMock) = CreateController();
        var commentId = Guid.NewGuid();

        senderMock.Setup(s => s.Send(
                It.Is<DeleteCommentCommand>(c => c.Id == commentId && c.TargetType == CommentTargetType.Product),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(Unit.Value));

        var actionResult = await controller.DeleteProductComment(commentId, CancellationToken.None);

        var noContentResult = actionResult as NoContentResult;
        noContentResult.Should().NotBeNull();
        noContentResult!.StatusCode.Should().Be(StatusCodes.Status204NoContent);
    }

    [Fact]
    public async Task GetPostComments_WhenNotFound_ReturnsNotFoundStatus()
    {
        var (controller, senderMock) = CreateController();
        var postId = Guid.NewGuid();

        senderMock.Setup(s => s.Send(
                It.Is<GetPostCommentsQuery>(q => q.PostId == postId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<List<PostCommentDto>>(Error.NotFound("Post.NotFound", "Post not found")));

        var actionResult = await controller.GetPostComments(postId, CancellationToken.None);

        var notFoundResult = actionResult as NotFoundObjectResult;
        notFoundResult.Should().NotBeNull();
        notFoundResult!.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task DeletePostComment_WhenForbidden_ReturnsForbiddenStatus()
    {
        var (controller, senderMock) = CreateController();
        var commentId = Guid.NewGuid();

        senderMock.Setup(s => s.Send(
                It.Is<DeleteCommentCommand>(c => c.Id == commentId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Unit>(Error.Forbidden("Comment.Forbidden", "Not your comment")));

        var actionResult = await controller.DeletePostComment(commentId, CancellationToken.None);

        var forbiddenResult = actionResult as ObjectResult;
        forbiddenResult.Should().NotBeNull();
        forbiddenResult!.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task CreatePostComment_WhenValidationError_ReturnsBadRequestStatus()
    {
        var (controller, senderMock) = CreateController();
        var postId = Guid.NewGuid();

        senderMock.Setup(s => s.Send(
                It.IsAny<CreatePostCommentCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<PostCommentDto>(Error.Validation("Comment.Validation", "Empty content")));

        var request = new CreateCommentRequest("");
        var actionResult = await controller.CreatePostComment(postId, request, CancellationToken.None);

        var badRequestResult = actionResult as BadRequestObjectResult;
        badRequestResult.Should().NotBeNull();
        badRequestResult!.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    #endregion
}
