using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RiuTek.API.Contracts;
using RiuTek.Application.Common.Models;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Posts.Commands;
using RiuTek.Application.Features.Posts.Queries;
using RiuTek.Core.Constants;

namespace RiuTek.API.Controllers;

public class PostsController : ApiControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PagedResult<PostSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPosts(
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? searchTerm = null,
        [FromQuery] bool? isFeaturedOnly = null,
        CancellationToken cancellationToken = default)
    {
        var query = new GetPostsQuery(
            PageIndex: pageIndex,
            PageSize: pageSize,
            SearchTerm: searchTerm,
            IsFeaturedOnly: isFeaturedOnly,
            IsPublishedOnly: true
        );

        var result = await Mediator.Send(query, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("slug/{slug}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PostDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBySlug(
        string slug,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetPostBySlugQuery(slug), cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ContentManager)]
    [ProducesResponseType(typeof(PostDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetPostByIdQuery(id), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost]
    [Authorize(Policy = Policies.ContentManager)]
    [ProducesResponseType(typeof(PostDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(
        [FromBody] CreatePostRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new CreatePostCommand(
            Title: request.Title,
            Summary: request.Summary,
            Content: request.Content,
            ThumbnailUrl: request.ThumbnailUrl,
            IsPublished: request.IsPublished,
            IsFeatured: request.IsFeatured
        );

        var result = await Mediator.Send(command, cancellationToken);
        return HandleCreatedResult(result, nameof(GetById), result.IsSuccess ? new { id = result.Value.Id } : null);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.ContentManager)]
    [ProducesResponseType(typeof(PostDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdatePostRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new UpdatePostCommand(
            Id: id,
            Title: request.Title,
            Summary: request.Summary,
            Content: request.Content,
            ThumbnailUrl: request.ThumbnailUrl,
            IsPublished: request.IsPublished,
            IsFeatured: request.IsFeatured
        );

        var result = await Mediator.Send(command, cancellationToken);
        return HandleResult(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.ContentManager)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new DeletePostCommand(id), cancellationToken);
        return HandleNoContentResult(result);
    }
}
