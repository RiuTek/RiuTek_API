using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Orders.Common;
using RiuTek.Core.Common;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Orders.Commands;

public record UpdateOrderStatusCommand(
    Guid OrderId,
    OrderStatus ExpectedStatus,
    OrderStatus NewStatus
) : IRequest<Result<OrderDetailDto>>;

public class UpdateOrderStatusCommandValidator : AbstractValidator<UpdateOrderStatusCommand>
{
    public UpdateOrderStatusCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.ExpectedStatus).IsInEnum();
        RuleFor(x => x.NewStatus).IsInEnum();
        RuleFor(x => x.NewStatus)
            .Must(status => status is
                OrderStatus.Processing or
                OrderStatus.Shipping or
                OrderStatus.Completed or
                OrderStatus.Cancelled)
            .WithMessage("NewStatus is not managed by the fulfillment workflow.");
    }
}

public class UpdateOrderStatusCommandHandler
    : IRequestHandler<UpdateOrderStatusCommand, Result<OrderDetailDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public UpdateOrderStatusCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<OrderDetailDto>> Handle(
        UpdateOrderStatusCommand request,
        CancellationToken cancellationToken)
    {
        var authResult = await OrderAuthHelper.ValidateActiveContentManagerAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (authResult.IsFailure)
        {
            return Result.Failure<OrderDetailDto>(authResult.Error);
        }

        var order = await _context.Orders
            .Include(entity => entity.Items)
            .FirstOrDefaultAsync(entity => entity.Id == request.OrderId, cancellationToken);

        if (order is null)
        {
            return Result.Failure<OrderDetailDto>(Error.NotFound(
                "Order.NotFound",
                "Order was not found."));
        }

        var previousStatus = order.Status;
        var isNewCancellation = request.NewStatus == OrderStatus.Cancelled &&
                                previousStatus != OrderStatus.Cancelled;

        var transitionResult = order.ChangeFulfillmentStatus(
            request.ExpectedStatus,
            request.NewStatus);

        if (transitionResult.IsFailure)
        {
            _context.ClearTrackedChanges();
            return Result.Failure<OrderDetailDto>(transitionResult.Error);
        }

        if (isNewCancellation)
        {
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
        }

        if (previousStatus != order.Status)
        {
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
        }

        var detail = await _context.Orders
            .AsNoTracking()
            .Where(entity => entity.Id == request.OrderId)
            .Select(OrderMappingExtensions.ToDetailDto)
            .SingleAsync(cancellationToken);

        return Result.Success(detail);
    }
}
