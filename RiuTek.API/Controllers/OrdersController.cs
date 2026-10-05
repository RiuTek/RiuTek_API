using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RiuTek.API.Contracts;
using RiuTek.Application.Common.Models;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Orders.Commands;
using RiuTek.Application.Features.Orders.Queries;
using RiuTek.Core.Enums;

namespace RiuTek.API.Controllers;

[Authorize]
public class OrdersController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<OrderSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyOrders(
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] OrderStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetMyOrdersQuery(pageIndex, pageSize, status), cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyOrderById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetMyOrderByIdQuery(id), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("checkout/quote")]
    [ProducesResponseType(typeof(CheckoutQuoteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCheckoutQuote(
        [FromBody] CheckoutQuoteRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetCheckoutQuoteQuery(request.AddressId), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("checkout")]
    [ProducesResponseType(typeof(CheckoutOrderDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Checkout(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] CheckoutCartRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new CheckoutCartCommand(
            AddressId: request.AddressId,
            ExpectedCartVersion: request.ExpectedCartVersion,
            PaymentMethod: request.PaymentMethod,
            Notes: request.Notes,
            IdempotencyKey: idempotencyKey ?? string.Empty
        ), cancellationToken);

        return HandleCreatedResult(result);
    }
}
