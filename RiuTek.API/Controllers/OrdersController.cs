using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RiuTek.API.Contracts;
using RiuTek.Application.Common.Models;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Orders.Commands;
using RiuTek.Application.Features.Orders.Queries;
using RiuTek.Core.Constants;
using RiuTek.Core.Enums;

namespace RiuTek.API.Controllers;

[Authorize]
public class OrdersController : ApiControllerBase
{
    [HttpGet("admin")]
    [Authorize(Policy = Policies.ContentManager)]
    [ProducesResponseType(typeof(PagedResult<ManagedOrderSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetManagedOrders(
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] OrderStatus? status = null,
        [FromQuery] PaymentMethod? paymentMethod = null,
        [FromQuery] PaymentStatus? paymentStatus = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetManagedOrdersQuery(
            pageIndex,
            pageSize,
            status,
            paymentMethod,
            paymentStatus,
            search), cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("admin/{id:guid}")]
    [Authorize(Policy = Policies.ContentManager)]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetManagedOrderById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetManagedOrderByIdQuery(id), cancellationToken);
        return HandleResult(result);
    }

    [HttpPatch("admin/{id:guid}/status")]
    [Authorize(Policy = Policies.ContentManager)]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateManagedOrderStatus(
        [FromRoute] Guid id,
        [FromBody] UpdateOrderStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new UpdateOrderStatusCommand(
            id,
            request.ExpectedStatus,
            request.NewStatus), cancellationToken);
        return HandleResult(result);
    }

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

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CancelMyOrder(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new CancelMyOrderCommand(id), cancellationToken);
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
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
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

    [HttpPost("buy-now/product/{productId:guid}")]
    [ProducesResponseType(typeof(CheckoutOrderDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> BuyNowProduct(
        [FromRoute] Guid productId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] BuyNowProductRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new BuyNowProductCommand(
            ProductId: productId,
            AddressId: request.AddressId,
            Quantity: request.Quantity,
            PaymentMethod: request.PaymentMethod,
            Notes: request.Notes,
            IdempotencyKey: idempotencyKey ?? string.Empty
        ), cancellationToken);

        return HandleCreatedResult(result);
    }

    [HttpPost("buy-now/pc-build/{buildId:guid}")]
    [ProducesResponseType(typeof(CheckoutOrderDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> BuyNowPCBuild(
        [FromRoute] Guid buildId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] BuyNowPCBuildRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new BuyNowPCBuildCommand(
            PCBuildId: buildId,
            AddressId: request.AddressId,
            PaymentMethod: request.PaymentMethod,
            Notes: request.Notes,
            IdempotencyKey: idempotencyKey ?? string.Empty
        ), cancellationToken);

        return HandleCreatedResult(result);
    }
}
