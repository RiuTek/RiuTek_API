using RiuTek.Application.DTOs;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Common.Mappings;

public static class ProductMappingExtensions
{
    public static ProductDto ToDto(this Product product) => new(
        product.Id,
        product.CategoryId,
        product.Category?.Name ?? string.Empty,
        product.Name,
        product.Slug,
        product.Sku,
        product.Brand,
        product.Price,
        product.OriginalPrice,
        product.StockQuantity,
        product.IsActive,
        product.ImageUrl,
        product.AdditionalImages,
        product.ComponentType,
        product.Specifications,
        product.CreatedAt
    );

    public static IReadOnlyList<ProductDto> ToDtoList(this IEnumerable<Product> products) =>
        products.Select(p => p.ToDto()).ToList();
}
