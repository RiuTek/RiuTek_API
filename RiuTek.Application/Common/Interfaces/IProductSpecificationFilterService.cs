using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Products.Queries;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Common.Interfaces;

public interface IProductSpecificationFilterService
{
    IQueryable<Product> Apply(IQueryable<Product> query, ProductFilterOptions options);

    Task<ProductFilterOptionsDto> GetFilterOptionsAsync(
        IQueryable<Product> scopedQuery,
        ComponentType? componentType,
        CancellationToken cancellationToken = default);
}
