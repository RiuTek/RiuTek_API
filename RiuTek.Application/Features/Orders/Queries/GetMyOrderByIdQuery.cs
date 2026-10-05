using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Orders.Common;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Orders.Queries;

public record GetMyOrderByIdQuery(Guid Id) : IRequest<Result<OrderDetailDto>>;

public class GetMyOrderByIdQueryValidator : AbstractValidator<GetMyOrderByIdQuery>
{
    public GetMyOrderByIdQueryValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Order Id is required.");
    }
}

public class GetMyOrderByIdQueryHandler : IRequestHandler<GetMyOrderByIdQuery, Result<OrderDetailDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetMyOrderByIdQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<OrderDetailDto>> Handle(
        GetMyOrderByIdQuery request,
        CancellationToken cancellationToken)
    {
        var authResult = await OrderAuthHelper.ValidateActiveUserAsync(_currentUserService, _context, cancellationToken);
        if (authResult.IsFailure)
        {
            return Result.Failure<OrderDetailDto>(authResult.Error);
        }

        var userId = authResult.Value.Id;

        var order = await _context.Orders
            .AsNoTracking()
            .Where(o => o.Id == request.Id && o.UserId == userId)
            .Select(OrderMappingExtensions.ToDetailDto)
            .FirstOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            return Result.Failure<OrderDetailDto>(Error.NotFound(
                "Order.NotFound",
                "Order was not found."));
        }

        return Result.Success(order);
    }
}
