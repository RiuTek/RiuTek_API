using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RiuTek.API.Contracts;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Comments.Commands;
using RiuTek.Application.Features.Comments.Queries;

namespace RiuTek.API.Controllers;

public class CommentsController : ApiControllerBase
{
    [HttpGet("posts/{postId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<PostCommentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPostComments(
        Guid postId,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetPostCommentsQuery(postId), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("posts/{postId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(PostCommentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreatePostComment(
        Guid postId,
        [FromBody] CreateCommentRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new CreatePostCommentCommand(
            PostId: postId,
            Content: request.Content,
            ParentCommentId: request.ParentCommentId
        );

        var result = await Mediator.Send(command, cancellationToken);
        return HandleCreatedResult(result);
    }

    [HttpDelete("posts/{commentId:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeletePostComment(
        Guid commentId,
        CancellationToken cancellationToken = default)
    {
        var command = new DeleteCommentCommand(
            Id: commentId,
            TargetType: CommentTargetType.Post
        );

        var result = await Mediator.Send(command, cancellationToken);
        return HandleNoContentResult(result);
    }

    [HttpGet("products/{productId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<ProductCommentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProductComments(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetProductCommentsQuery(productId), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("products/{productId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(ProductCommentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateProductComment(
        Guid productId,
        [FromBody] CreateCommentRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new CreateProductCommentCommand(
            ProductId: productId,
            Content: request.Content,
            ParentCommentId: request.ParentCommentId
        );

        var result = await Mediator.Send(command, cancellationToken);
        return HandleCreatedResult(result);
    }

    [HttpDelete("products/{commentId:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteProductComment(
        Guid commentId,
        CancellationToken cancellationToken = default)
    {
        var command = new DeleteCommentCommand(
            Id: commentId,
            TargetType: CommentTargetType.Product
        );

        var result = await Mediator.Send(command, cancellationToken);
        return HandleNoContentResult(result);
    }
}
