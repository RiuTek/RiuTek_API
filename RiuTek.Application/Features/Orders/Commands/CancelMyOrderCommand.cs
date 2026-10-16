using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Orders.Common;
using RiuTek.Core.Common;
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

    public CancelMyOrderCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
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

        if (order.Status != OrderStatus.Confirmed || order.PaymentMethod != PaymentMethod.COD)
        {
            return Result.Failure<OrderDetailDto>(Error.Conflict(
                "Order.CustomerCancellationNotAllowed",
                "Only a confirmed COD order can be cancelled by the customer."));
        }

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
            return Result.Failure<OrderDetailDto>(Error.Conflict(
                "Order.ConcurrencyConflict",
                "The order or its stock changed concurrently. Reload the order and try again."));
        }

        return await LoadDetailAsync(order.Id, cancellationToken);
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
