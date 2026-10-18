using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Models;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Products.Common;
using RiuTek.Core.Common;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Products.Queries;

public record GetStockMovementsQuery(
    Guid ProductId,
    int PageIndex = 1,
    int PageSize = 20,
    StockMovementReason? Reason = null
) : IRequest<Result<PagedResult<StockMovementDto>>>;

public class GetStockMovementsQueryValidator : AbstractValidator<GetStockMovementsQuery>
{
    public GetStockMovementsQueryValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("Mã sản phẩm không được để trống.");

        RuleFor(x => x.PageIndex)
            .GreaterThanOrEqualTo(1).WithMessage("Chỉ số trang phải lớn hơn hoặc bằng 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("Kích thước trang phải từ 1 đến 100.");

        RuleFor(x => x.Reason)
            .IsInEnum().When(x => x.Reason.HasValue).WithMessage("Lý do biến động kho không hợp lệ.");
    }
}

public class GetStockMovementsQueryHandler : IRequestHandler<GetStockMovementsQuery, Result<PagedResult<StockMovementDto>>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetStockMovementsQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<PagedResult<StockMovementDto>>> Handle(
        GetStockMovementsQuery request,
        CancellationToken cancellationToken)
    {
        var authResult = await StockAuthHelper.ValidateActiveContentManagerAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (authResult.IsFailure)
        {
            return Result.Failure<PagedResult<StockMovementDto>>(authResult.Error);
        }

        var product = await _context.Products
            .AsNoTracking()
            .Select(p => new { p.Id, p.Sku })
            .FirstOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken);

        if (product == null)
        {
            return Result.Failure<PagedResult<StockMovementDto>>(Error.NotFound(
                "Product.NotFound",
                "Không tìm thấy sản phẩm."));
        }

        var query = _context.StockMovements
            .AsNoTracking()
            .Where(m => m.ProductId == request.ProductId);

        if (request.Reason.HasValue)
        {
            query = query.Where(m => m.Reason == request.Reason.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Skip((request.PageIndex - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(m => new StockMovementDto(
                m.Id,
                m.ProductId,
                product.Sku,
                m.QuantityDelta,
                m.StockBefore,
                m.StockAfter,
                m.Reason,
                m.Note,
                m.ActorUserId,
                m.ActorUser != null ? m.ActorUser.FullName : null,
                m.OrderId,
                m.CreatedAt
            ))
            .ToListAsync(cancellationToken);

        var pagedResult = PagedResult<StockMovementDto>.Create(items, totalCount, request.PageIndex, request.PageSize);
        return Result.Success(pagedResult);
    }
}
