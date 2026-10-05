using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Models;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Orders.Common;
using RiuTek.Core.Common;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Orders.Queries;

public record GetMyOrdersQuery(
    int PageIndex = 1,
    int PageSize = 10,
    OrderStatus? Status = null
) : IRequest<Result<PagedResult<OrderSummaryDto>>>;

public class GetMyOrdersQueryValidator : AbstractValidator<GetMyOrdersQuery>
{
    public GetMyOrdersQueryValidator()
    {
        RuleFor(x => x.PageIndex)
            .GreaterThanOrEqualTo(1)
            .WithMessage("PageIndex must be greater than or equal to 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 50)
            .WithMessage("PageSize must be between 1 and 50.");

        When(x => x.Status.HasValue, () =>
        {
            RuleFor(x => x.Status)
                .IsInEnum()
                .WithMessage("Status is invalid.");
        });
    }
}

public class GetMyOrdersQueryHandler : IRequestHandler<GetMyOrdersQuery, Result<PagedResult<OrderSummaryDto>>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetMyOrdersQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<PagedResult<OrderSummaryDto>>> Handle(
        GetMyOrdersQuery request,
        CancellationToken cancellationToken)
    {
        var authResult = await OrderAuthHelper.ValidateActiveUserAsync(_currentUserService, _context, cancellationToken);
        if (authResult.IsFailure)
        {
            return Result.Failure<PagedResult<OrderSummaryDto>>(authResult.Error);
        }

        var userId = authResult.Value.Id;

        var query = _context.Orders
            .AsNoTracking()
            .Where(o => o.UserId == userId);

        if (request.Status.HasValue)
        {
            query = query.Where(o => o.Status == request.Status.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        long offset = (long)(request.PageIndex - 1) * request.PageSize;
        List<OrderSummaryDto> items;

        if (totalCount == 0 || offset >= totalCount || offset > int.MaxValue)
        {
            items = [];
        }
        else
        {
            items = await query
                .OrderByDescending(o => o.CreatedAt)
                .ThenBy(o => o.Id)
                .Skip((int)offset)
                .Take(request.PageSize)
                .Select(OrderMappingExtensions.ToSummaryDto)
                .ToListAsync(cancellationToken);
        }

        var pagedResult = PagedResult<OrderSummaryDto>.Create(items, totalCount, request.PageIndex, request.PageSize);
        return Result.Success(pagedResult);
    }
}
