using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Mappings;
using RiuTek.Application.Common.Models;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Products.Queries;

public record GetProductsQuery(ProductFilterOptions Options)
    : IRequest<Result<PagedResult<ProductSummaryDto>>>;

public class GetProductsQueryValidator : AbstractValidator<GetProductsQuery>
{
    public GetProductsQueryValidator()
    {
        RuleFor(x => x.Options)
            .NotNull()
            .WithMessage("Tùy chọn lọc không được để trống.")
            .SetValidator(new ProductFilterOptionsValidator()!);
    }
}

public class GetProductsQueryHandler : IRequestHandler<GetProductsQuery, Result<PagedResult<ProductSummaryDto>>>
{
    private readonly IApplicationDbContext _context;

    public GetProductsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<PagedResult<ProductSummaryDto>>> Handle(
        GetProductsQuery request,
        CancellationToken cancellationToken)
    {
        HashSet<Guid>? categoryIds = null;

        if (request.Options.CategoryId.HasValue)
        {
            var categoryResult = await ProductQueryExtensions.GetCategoryAndDescendantIdsAsync(
                _context,
                request.Options.CategoryId.Value,
                cancellationToken);

            if (!categoryResult.IsSuccess)
            {
                return Result.Failure<PagedResult<ProductSummaryDto>>(categoryResult.Error);
            }

            categoryIds = categoryResult.Value;
        }

        var query = _context.Products
            .AsNoTracking()
            .ApplyFilters(request.Options, categoryIds);

        var totalCount = await query.CountAsync(cancellationToken);

        long offset = ((long)request.Options.PageIndex - 1) * request.Options.PageSize;

        List<ProductSummaryDto> items;
        if (offset >= totalCount)
        {
            items = [];
        }
        else
        {
            items = await query
                .ApplySorting(request.Options.SortBy)
                .Skip((int)offset)
                .Take(request.Options.PageSize)
                .Select(ProductMappingExtensions.ToSummaryDtoProjection)
                .ToListAsync(cancellationToken);
        }

        var pagedResult = new PagedResult<ProductSummaryDto>(
            items,
            totalCount,
            request.Options.PageIndex,
            request.Options.PageSize);

        return Result.Success(pagedResult);
    }
}
