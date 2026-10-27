using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Orders.Common;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Orders.Commands;

public record CancelMyOrderCommand(Guid OrderId) : IRequest<Result<OrderDetailDto>>;

public class CancelMyOrderCommandValidator : AbstractValidator<CancelMyOrderCommand>
{
    public CancelMyOrderCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
    }
}

public class CancelMyOrderCommandHandler
    : IRequestHandler<CancelMyOrderCommand, Result<OrderDetailDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IStripePaymentGateway _stripeGateway;

    public CancelMyOrderCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IStripePaymentGateway stripeGateway)
    {
        _context = context;
        _currentUserService = currentUserService;
        _stripeGateway = stripeGateway;
    }

    public async Task<Result<OrderDetailDto>> Handle(
        CancelMyOrderCommand request,
        CancellationToken cancellationToken)
    {
        var authResult = await OrderAuthHelper.ValidateActiveUserAsync(
            _currentUserService,
            _context,
            cancellationToken);
        if (authResult.IsFailure)
        {
            return Result.Failure<OrderDetailDto>(authResult.Error);
        }

        var order = await _context.Orders
            .Include(entity => entity.Items)
            .Include(entity => entity.PaymentAttempts)
            .FirstOrDefaultAsync(
                entity => entity.Id == request.OrderId && entity.UserId == authResult.Value.Id,
                cancellationToken);

        if (order is null)
        {
            return Result.Failure<OrderDetailDto>(Error.NotFound(
                "Order.NotFound",
                "Order was not found."));
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return await LoadDetailAsync(order.Id, cancellationToken);
        }

        if (order.PaymentMethod == PaymentMethod.COD && order.Status == OrderStatus.Confirmed)
        {
            return await CancelConfirmedCodOrderAsync(order, cancellationToken);
        }

        if (order.PaymentMethod == PaymentMethod.Stripe && order.Status == OrderStatus.PendingPayment)
        {
            return await CancelPendingStripeOrderAsync(order, cancellationToken);
        }

        return Result.Failure<OrderDetailDto>(Error.Conflict(
            "Order.CustomerCancellationNotAllowed",
            "Only a confirmed COD order or a pending Stripe order can be cancelled by the customer."));
    }

    private async Task<Result<OrderDetailDto>> CancelConfirmedCodOrderAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        var transitionResult = order.ChangeFulfillmentStatus(
            OrderStatus.Confirmed,
            OrderStatus.Cancelled);
        if (transitionResult.IsFailure)
        {
            _context.ClearTrackedChanges();
            return Result.Failure<OrderDetailDto>(transitionResult.Error);
        }

        var restoreResult = await OrderStockHelper.RestoreCancelledOrderStockAsync(
            order,
            _context,
            _currentUserService.UserId,
            StockMovementReason.OrderCancellation,
            cancellationToken);
        if (restoreResult.IsFailure)
        {
            _context.ClearTrackedChanges();
            return Result.Failure<OrderDetailDto>(restoreResult.Error);
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _context.ClearTrackedChanges();
            return await HandleConcurrencyReloadAsync(order.Id, order.UserId, cancellationToken);
        }

        return await LoadDetailAsync(order.Id, cancellationToken);
    }

    private async Task<Result<OrderDetailDto>> CancelPendingStripeOrderAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        var pendingAttempts = order.PaymentAttempts
            .Where(a => a.Status == PaymentAttemptStatus.Pending)
            .ToList();

        if (pendingAttempts.Count != 1 || string.IsNullOrWhiteSpace(pendingAttempts[0].ProviderReference))
        {
            return Result.Failure<OrderDetailDto>(Error.Conflict(
                "Order.PendingPaymentAttemptMissing",
                "No valid pending Stripe payment attempt found to cancel."));
        }

        var attempt = pendingAttempts[0];

        // 1. Expire session on Stripe first before local mutation
        var expireResult = await _stripeGateway.ExpireCheckoutSessionAsync(
            attempt.ProviderReference!,
            cancellationToken);

        if (expireResult.IsFailure)
        {
            _context.ClearTrackedChanges();
            return Result.Failure<OrderDetailDto>(expireResult.Error);
        }

        // 2. Perform domain transition
        var nowUtc = DateTime.UtcNow;
        var cancelResult = order.CancelPendingStripePayment(attempt.Id, nowUtc);
        if (cancelResult.IsFailure)
        {
            _context.ClearTrackedChanges();
            return Result.Failure<OrderDetailDto>(cancelResult.Error);
        }

        // 3. Restore product stock with OrderCancellation movement
        var restoreResult = await OrderStockHelper.RestoreCancelledOrderStockAsync(
            order,
            _context,
            _currentUserService.UserId,
            StockMovementReason.OrderCancellation,
            cancellationToken);
        if (restoreResult.IsFailure)
        {
            _context.ClearTrackedChanges();
            return Result.Failure<OrderDetailDto>(restoreResult.Error);
        }

        // 4. Save changes atomically
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _context.ClearTrackedChanges();
            return await HandleConcurrencyReloadAsync(order.Id, order.UserId, cancellationToken);
        }

        return await LoadDetailAsync(order.Id, cancellationToken);
    }

    private async Task<Result<OrderDetailDto>> HandleConcurrencyReloadAsync(
        Guid orderId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var reloadedOrder = await _context.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId, cancellationToken);

        if (reloadedOrder is not null && reloadedOrder.Status == OrderStatus.Cancelled)
        {
            return await LoadDetailAsync(orderId, cancellationToken);
        }

        return Result.Failure<OrderDetailDto>(Error.Conflict(
            "Order.ConcurrencyConflict",
            "The order or its stock changed concurrently. Reload the order and try again."));
    }

    private async Task<Result<OrderDetailDto>> LoadDetailAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var detail = await _context.Orders
            .AsNoTracking()
            .Where(entity => entity.Id == orderId)
            .Select(OrderMappingExtensions.ToDetailDto)
            .SingleAsync(cancellationToken);
        return Result.Success(detail);
    }
}
