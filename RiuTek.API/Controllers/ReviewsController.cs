using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RiuTek.Application.Common.Models;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Reviews.Commands;
using RiuTek.Application.Features.Reviews.Queries;
using RiuTek.Core.Constants;

namespace RiuTek.API.Controllers;

public class ReviewsController : ApiControllerBase
{
    [HttpGet("/api/v1/products/{productId:guid}/reviews")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ProductReviewsResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProductReviews(
        [FromRoute] Guid productId,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] int? rating = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(
            new GetProductReviewsQuery(productId, pageIndex, pageSize, rating),
            cancellationToken);

        return HandleResult(result);
    }

    [HttpGet("mine")]
    [Authorize]
    [ProducesResponseType(typeof(PagedResult<MyReviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyReviews(
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(
            new GetMyReviewsQuery(pageIndex, pageSize),
            cancellationToken);

        return HandleResult(result);
    }

    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(MyReviewDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateReview(
        [FromBody] CreateReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(
            new CreateReviewCommand(
                request.OrderId,
                request.ProductId,
                request.Rating,
                request.Title,
                request.Content),
            cancellationToken);

        return HandleCreatedResult(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(MyReviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateReview(
        [FromRoute] Guid id,
        [FromBody] UpdateReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(
            new UpdateMyReviewCommand(
                id,
                request.Rating,
                request.Title,
                request.Content),
            cancellationToken);

        return HandleResult(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteReview(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(
            new DeleteMyReviewCommand(id),
            cancellationToken);

        return HandleNoContentResult(result);
    }

    [HttpGet("admin")]
    [Authorize(Policy = Policies.ContentManager)]
    [ProducesResponseType(typeof(PagedResult<ManagedReviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetManagedReviews(
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? productId = null,
        [FromQuery] int? rating = null,
        [FromQuery] bool? hasReply = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(
            new GetManagedReviewsQuery(
                pageIndex,
                pageSize,
                productId,
                rating,
                hasReply),
            cancellationToken);

        return HandleResult(result);
    }

    [HttpPut("{id:guid}/reply")]
    [Authorize(Policy = Policies.ContentManager)]
    [ProducesResponseType(typeof(ManagedReviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetReviewReply(
        [FromRoute] Guid id,
        [FromBody] SetReviewReplyRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(
            new SetReviewReplyCommand(id, request.Reply),
            cancellationToken);

        return HandleResult(result);
    }

    [HttpDelete("{id:guid}/reply")]
    [Authorize(Policy = Policies.ContentManager)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteReviewReply(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(
            new DeleteReviewReplyCommand(id),
            cancellationToken);

        return HandleNoContentResult(result);
    }

    [HttpDelete("admin/{id:guid}")]
    [Authorize(Policy = Policies.ContentManager)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteManagedReview(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(
            new DeleteManagedReviewCommand(id),
            cancellationToken);

        return HandleNoContentResult(result);
    }
}
