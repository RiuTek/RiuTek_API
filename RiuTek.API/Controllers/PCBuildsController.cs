using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RiuTek.API.Contracts;
using RiuTek.Application.Common.Models;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.PCBuilds.Commands;
using RiuTek.Application.Features.PCBuilds.Queries;

namespace RiuTek.API.Controllers;

[Route("api/v1/pc-builds")]
public class PCBuildsController : ApiControllerBase
{
    [HttpPost("preview")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PCBuildPreviewResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PreviewBuild(
        [FromBody] PreviewPCBuildRequest request,
        CancellationToken cancellationToken = default)
    {
        var items = request.Items?
            .Select(i => new PCBuildItemInputDto(i.ProductId, i.Quantity))
            .ToList() ?? [];

        var result = await Mediator.Send(
            new PreviewPCBuildCommand(items),
            cancellationToken);

        return HandleResult(result);
    }

    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(MyPCBuildDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateBuild(
        [FromBody] CreatePCBuildRequest request,
        CancellationToken cancellationToken = default)
    {
        var items = request.Items?
            .Select(i => new PCBuildItemInputDto(i.ProductId, i.Quantity))
            .ToList() ?? [];

        var result = await Mediator.Send(
            new CreatePCBuildCommand(request.Name, request.Description, items),
            cancellationToken);

        return HandleCreatedResult(
            result,
            nameof(GetMyBuildById),
            result.IsSuccess ? new { id = result.Value.Id } : null);
    }

    [HttpGet("mine")]
    [Authorize]
    [ProducesResponseType(typeof(PagedResult<MyPCBuildSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMyBuilds(
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(
            new GetMyPCBuildsQuery(pageIndex, pageSize),
            cancellationToken);

        return HandleResult(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    [ActionName(nameof(GetMyBuildById))]
    [ProducesResponseType(typeof(MyPCBuildDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyBuildById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(
            new GetMyPCBuildByIdQuery(id),
            cancellationToken);

        return HandleResult(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(MyPCBuildDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateMyBuild(
        [FromRoute] Guid id,
        [FromBody] UpdatePCBuildRequest request,
        CancellationToken cancellationToken = default)
    {
        var items = request.Items?
            .Select(i => new PCBuildItemInputDto(i.ProductId, i.Quantity))
            .ToList() ?? [];

        var result = await Mediator.Send(
            new UpdateMyPCBuildCommand(id, request.Name, request.Description, items),
            cancellationToken);

        return HandleResult(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteMyBuild(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(
            new DeleteMyPCBuildCommand(id),
            cancellationToken);

        return HandleNoContentResult(result);
    }
}
