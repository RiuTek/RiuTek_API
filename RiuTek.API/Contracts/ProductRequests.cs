using System.Text.Json.Serialization;
using RiuTek.Application.Features.Products.Queries;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;

namespace RiuTek.API.Contracts;

public record CreateProductRequest(
    Guid CategoryId,
    string Name,
    string Sku,
    string Brand,
    decimal Price,
    decimal? OriginalPrice,
    int StockQuantity,
    string ImageUrl,
    IReadOnlyList<string>? AdditionalImages,
    ComponentType ComponentType,
    ComponentSpecification Specifications
);

public record UpdateProductRequest(
    Guid CategoryId,
    string Name,
    string Sku,
    string Brand,
    decimal Price,
    decimal? OriginalPrice,
    int StockQuantity,
    [property: JsonRequired] bool IsActive,
    string ImageUrl,
    IReadOnlyList<string>? AdditionalImages,
    ComponentType ComponentType,
    ComponentSpecification Specifications
);

public class ProductListRequest
{
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SearchTerm { get; set; }
    public Guid? CategoryId { get; set; }
    public ComponentType? ComponentType { get; set; }
    public string? Brand { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public bool? InStock { get; set; }
    public bool? IsActive { get; set; }
    public ProductSortOption SortBy { get; set; } = ProductSortOption.Newest;

    public ProductFilterOptions ToFilterOptions() => new(
        PageIndex: PageIndex,
        PageSize: PageSize,
        SearchTerm: SearchTerm,
        CategoryId: CategoryId,
        ComponentType: ComponentType,
        Brand: Brand,
        MinPrice: MinPrice,
        MaxPrice: MaxPrice,
        InStock: InStock,
        IsActive: IsActive,
        SortBy: SortBy
    );
}
