using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Products.Queries;

public record GetProductFilterOptionsQuery(
    ComponentType? ComponentType = null,
    Guid? CategoryId = null,
    bool? IsActive = null,
    bool? InStock = null
) : IRequest<Result<ProductFilterOptionsDto>>;

public class GetProductFilterOptionsQueryValidator : AbstractValidator<GetProductFilterOptionsQuery>
{
    public GetProductFilterOptionsQueryValidator()
    {
        RuleFor(x => x.ComponentType)
            .IsInEnum()
            .When(x => x.ComponentType.HasValue)
            .WithMessage("Loại linh kiện không hợp lệ.");

        RuleFor(x => x.CategoryId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Id danh mục không hợp lệ.");
    }
}

public class GetProductFilterOptionsQueryHandler
    : IRequestHandler<GetProductFilterOptionsQuery, Result<ProductFilterOptionsDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IProductSpecificationFilterService _specFilterService;

    public GetProductFilterOptionsQueryHandler(
        IApplicationDbContext context,
        IProductSpecificationFilterService specFilterService)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _specFilterService = specFilterService ?? throw new ArgumentNullException(nameof(specFilterService));
    }

    public async Task<Result<ProductFilterOptionsDto>> Handle(
        GetProductFilterOptionsQuery request,
        CancellationToken cancellationToken)
    {
        HashSet<Guid>? categoryIds = null;

        if (request.CategoryId.HasValue)
        {
            var categoryResult = await ProductQueryExtensions.GetCategoryAndDescendantIdsAsync(
                _context,
                request.CategoryId.Value,
                cancellationToken);

            if (!categoryResult.IsSuccess)
            {
                return Result.Failure<ProductFilterOptionsDto>(categoryResult.Error);
            }

            categoryIds = categoryResult.Value;
        }

        var scope = new ProductFilterScope(
            ComponentType: request.ComponentType,
            CategoryIds: categoryIds,
            IsActive: request.IsActive,
            InStock: request.InStock
        );

        var result = await _specFilterService.GetFilterOptionsAsync(
            scope,
            cancellationToken);

        return Result.Success(result);
    }
}
