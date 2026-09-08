using FluentValidation;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Products.Queries;

public enum ProductSortOption
{
    Newest = 1,
    PriceLowToHigh = 2,
    PriceHighToLow = 3,
    NameAToZ = 4,
    NameZToA = 5
}

public record ProductFilterOptions(
    int PageIndex = 1,
    int PageSize = 20,
    string? SearchTerm = null,
    Guid? CategoryId = null,
    ComponentType? ComponentType = null,
    string? Brand = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    bool? InStock = null,
    bool? IsActive = null,
    ProductSortOption SortBy = ProductSortOption.Newest
);

public class ProductFilterOptionsValidator : AbstractValidator<ProductFilterOptions>
{
    public ProductFilterOptionsValidator()
    {
        RuleFor(x => x.PageIndex)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Trang hiện tại phải lớn hơn hoặc bằng 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 50)
            .WithMessage("Số lượng phần tử mỗi trang phải từ 1 đến 50.");

        When(x => x.SearchTerm != null, () =>
        {
            RuleFor(x => x.SearchTerm)
                .MaximumLength(100)
                .WithMessage("Từ khóa tìm kiếm không được vượt quá 100 ký tự.");
        });

        RuleFor(x => x.CategoryId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Id danh mục không hợp lệ.");

        RuleFor(x => x.ComponentType)
            .IsInEnum()
            .When(x => x.ComponentType.HasValue)
            .WithMessage("Loại linh kiện không hợp lệ.");

        When(x => x.Brand != null, () =>
        {
            RuleFor(x => x.Brand)
                .MaximumLength(100)
                .WithMessage("Tên thương hiệu không được vượt quá 100 ký tự.");
        });

        RuleFor(x => x.MinPrice)
            .GreaterThanOrEqualTo(0)
            .When(x => x.MinPrice.HasValue)
            .WithMessage("Giá tối thiểu không được âm.");

        RuleFor(x => x.MaxPrice)
            .GreaterThanOrEqualTo(0)
            .When(x => x.MaxPrice.HasValue)
            .WithMessage("Giá tối đa không được âm.");

        RuleFor(x => x.MaxPrice)
            .GreaterThanOrEqualTo(x => x.MinPrice!.Value)
            .When(x => x.MinPrice.HasValue && x.MaxPrice.HasValue)
            .WithMessage("Giá tối đa phải lớn hơn hoặc bằng giá tối thiểu.");

        RuleFor(x => x.SortBy)
            .IsInEnum()
            .WithMessage("Tùy chọn sắp xếp không hợp lệ.");
    }
}
