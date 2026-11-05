using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RiuTek.API.Contracts;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Banners.Commands;
using RiuTek.Application.Features.Banners.Queries;
using RiuTek.Core.Constants;

namespace RiuTek.API.Controllers;

[Route("api/v1/banners")]
public class BannersController : ApiControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<PublicBannerDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPublicBanners(CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetPublicBannersQuery(), cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("managed")]
    [Authorize(Policy = Policies.ContentManager)]
    [ProducesResponseType(typeof(List<ManagedBannerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetManagedBanners(CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetManagedBannersQuery(), cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ContentManager)]
    [ProducesResponseType(typeof(ManagedBannerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetManagedBannerByIdQuery(id), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost]
    [Authorize(Policy = Policies.ContentManager)]
    [ProducesResponseType(typeof(ManagedBannerDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Create(
        [FromBody] CreateBannerRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new CreateBannerCommand(
            request.Title,
            request.ImageObjectKey,
            request.TargetUrl,
            request.DisplayOrder,
            request.IsActive);

        var result = await Mediator.Send(command, cancellationToken);
        return HandleCreatedResult(result, nameof(GetById), result.IsSuccess ? new { id = result.Value.Id } : null);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.ContentManager)]
    [ProducesResponseType(typeof(ManagedBannerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateBannerRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new UpdateBannerCommand(
            id,
            request.Title,
            request.ImageObjectKey,
            request.TargetUrl,
            request.DisplayOrder,
            request.IsActive);

        var result = await Mediator.Send(command, cancellationToken);
        return HandleResult(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.ContentManager)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new DeleteBannerCommand(id), cancellationToken);
        return HandleNoContentResult(result);
    }
}
