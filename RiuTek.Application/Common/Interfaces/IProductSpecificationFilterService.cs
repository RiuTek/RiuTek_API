using RiuTek.Application.Features.Products.Queries;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Common.Interfaces;

public interface IProductSpecificationFilterService
{
    IQueryable<Product> Apply(IQueryable<Product> query, ProductFilterOptions options);
}
