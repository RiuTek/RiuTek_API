using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RiuTek.API.Contracts;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Carts.Commands;
using RiuTek.Application.Features.Carts.Queries;

namespace RiuTek.API.Controllers;

[Authorize]
public class CartsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCart(CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetCartQuery(), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("items")]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddItem(
        [FromBody] AddCartItemRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new AddCartItemCommand(request.ProductId, request.Quantity), cancellationToken);
        return HandleResult(result);
    }

    [HttpPut("items/{productId:guid}")]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetItemQuantity(
        [FromRoute] Guid productId,
        [FromBody] SetCartItemQuantityRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new SetCartItemQuantityCommand(productId, request.Quantity), cancellationToken);
        return HandleResult(result);
    }

    [HttpDelete("items/{productId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveItem(
        [FromRoute] Guid productId,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new RemoveCartItemCommand(productId), cancellationToken);
        return HandleNoContentResult(result);
    }

    [HttpDelete("items")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ClearCart(CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new ClearCartCommand(), cancellationToken);
        return HandleNoContentResult(result);
    }
}
