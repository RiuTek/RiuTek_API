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
    public async Task CreatePostComment_PassesRoutePostIdAndBodyContent()
    {
        var (controller, senderMock) = CreateController();
        var postId = Guid.NewGuid();
        var commentDto = new PostCommentDto(Guid.NewGuid(), postId, Guid.NewGuid(), "User", "Hello Post", null, DateTime.UtcNow, null, []);

        senderMock.Setup(s => s.Send(
                It.Is<CreatePostCommentCommand>(c => c.PostId == postId && c.Content == "Hello Post"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(commentDto));

        var request = new CreateCommentRequest("Hello Post");
        var actionResult = await controller.CreatePostComment(postId, request, CancellationToken.None);

        var createdResult = actionResult as ObjectResult;
        createdResult.Should().NotBeNull();
        createdResult!.StatusCode.Should().Be(StatusCodes.Status201Created);
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
    public async Task CreateProductComment_PassesRouteProductIdAndBodyContent()
    {
        var (controller, senderMock) = CreateController();
        var productId = Guid.NewGuid();
        var commentDto = new ProductCommentDto(Guid.NewGuid(), productId, Guid.NewGuid(), "User", "Hello Product", null, false, DateTime.UtcNow, []);

        senderMock.Setup(s => s.Send(
                It.Is<CreateProductCommentCommand>(c => c.ProductId == productId && c.Content == "Hello Product"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(commentDto));

        var request = new CreateCommentRequest("Hello Product");
        var actionResult = await controller.CreateProductComment(productId, request, CancellationToken.None);

        var createdResult = actionResult as ObjectResult;
        createdResult.Should().NotBeNull();
        createdResult!.StatusCode.Should().Be(StatusCodes.Status201Created);
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

    #endregion
}
