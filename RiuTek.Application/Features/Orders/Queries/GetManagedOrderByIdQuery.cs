using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Orders.Common;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Orders.Queries;

public record GetManagedOrderByIdQuery(Guid Id) : IRequest<Result<OrderDetailDto>>;

public class GetManagedOrderByIdQueryValidator : AbstractValidator<GetManagedOrderByIdQuery>
{
    public GetManagedOrderByIdQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public class GetManagedOrderByIdQueryHandler
    : IRequestHandler<GetManagedOrderByIdQuery, Result<OrderDetailDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetManagedOrderByIdQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<OrderDetailDto>> Handle(
        GetManagedOrderByIdQuery request,
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
            .AsNoTracking()
            .Where(entity => entity.Id == request.Id)
            .Select(OrderMappingExtensions.ToDetailDto)
            .FirstOrDefaultAsync(cancellationToken);

        return order is null
            ? Result.Failure<OrderDetailDto>(Error.NotFound(
                "Order.NotFound",
                "Order was not found."))
            : Result.Success(order);
    }
}
