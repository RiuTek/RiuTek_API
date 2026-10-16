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

public record GetManagedOrdersQuery(
    int PageIndex = 1,
    int PageSize = 20,
    OrderStatus? Status = null,
    PaymentMethod? PaymentMethod = null,
    PaymentStatus? PaymentStatus = null,
    string? Search = null
) : IRequest<Result<PagedResult<ManagedOrderSummaryDto>>>;

public class GetManagedOrdersQueryValidator : AbstractValidator<GetManagedOrdersQuery>
{
    public GetManagedOrdersQueryValidator()
    {
        RuleFor(x => x.PageIndex).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
        RuleFor(x => x.PaymentMethod).IsInEnum().When(x => x.PaymentMethod.HasValue);
        RuleFor(x => x.PaymentStatus).IsInEnum().When(x => x.PaymentStatus.HasValue);
        RuleFor(x => x.Search).MaximumLength(150).When(x => x.Search is not null);
    }
}

public class GetManagedOrdersQueryHandler
    : IRequestHandler<GetManagedOrdersQuery, Result<PagedResult<ManagedOrderSummaryDto>>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetManagedOrdersQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<PagedResult<ManagedOrderSummaryDto>>> Handle(
        GetManagedOrdersQuery request,
        CancellationToken cancellationToken)
    {
        var authResult = await OrderAuthHelper.ValidateActiveContentManagerAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (authResult.IsFailure)
        {
            return Result.Failure<PagedResult<ManagedOrderSummaryDto>>(authResult.Error);
        }

        var query = _context.Orders.AsNoTracking();

        if (request.Status.HasValue)
        {
            query = query.Where(order => order.Status == request.Status.Value);
        }

        if (request.PaymentMethod.HasValue)
        {
            query = query.Where(order => order.PaymentMethod == request.PaymentMethod.Value);
        }

        if (request.PaymentStatus.HasValue)
        {
            query = query.Where(order => order.PaymentStatus == request.PaymentStatus.Value);
        }

        var search = request.Search?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(order =>
                order.OrderNumber.ToLower().Contains(search) ||
                order.CustomerName.ToLower().Contains(search) ||
                order.CustomerEmail.ToLower().Contains(search) ||
                order.CustomerPhone.Contains(search));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var offset = (long)(request.PageIndex - 1) * request.PageSize;
        List<ManagedOrderSummaryDto> items;

        if (totalCount == 0 || offset >= totalCount || offset > int.MaxValue)
        {
            items = [];
        }
        else
        {
            items = await query
                .OrderByDescending(order => order.CreatedAt)
                .ThenBy(order => order.Id)
                .Skip((int)offset)
                .Take(request.PageSize)
                .Select(order => new ManagedOrderSummaryDto(
                    order.Id,
                    order.OrderNumber,
                    order.Status,
                    order.PaymentMethod,
                    order.PaymentStatus,
                    order.CustomerName,
                    order.CustomerEmail,
                    order.CustomerPhone,
                    order.Currency,
                    order.FinalAmount,
                    order.Items.Count,
                    order.CreatedAt,
                    order.UpdatedAt))
                .ToListAsync(cancellationToken);
        }

        return Result.Success(PagedResult<ManagedOrderSummaryDto>.Create(
            items,
            totalCount,
            request.PageIndex,
            request.PageSize));
    }
}
