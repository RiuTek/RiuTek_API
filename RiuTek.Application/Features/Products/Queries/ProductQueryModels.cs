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
    ProductSortOption SortBy = ProductSortOption.Newest,
    CpuSocket? Socket = null,
    RamType? MemoryType = null,
    MotherboardFormFactor? MotherboardFormFactor = null,
    RamFormFactor? RamFormFactor = null,
    StorageType? StorageType = null,
    StorageInterface? StorageInterface = null,
    PsuEfficiency? PsuEfficiency = null,
    PsuModularity? PsuModularity = null,
    PsuFormFactor? PsuFormFactor = null,
    CoolerType? CoolerType = null,
    bool? HasIntegratedGpu = null,
    bool? HasWifi = null,
    bool? HasRgb = null,
    bool? Has12VHPWR = null,
    bool? Requires12VHPWR = null
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

        // Technical enum filters validation
        RuleFor(x => x.Socket)
            .IsInEnum()
            .When(x => x.Socket.HasValue)
            .WithMessage("Loại socket không hợp lệ.");

        RuleFor(x => x.MemoryType)
            .IsInEnum()
            .When(x => x.MemoryType.HasValue)
            .WithMessage("Loại bộ nhớ (RAM) không hợp lệ.");

        RuleFor(x => x.MotherboardFormFactor)
            .IsInEnum()
            .When(x => x.MotherboardFormFactor.HasValue)
            .WithMessage("Kích thước bo mạch chủ không hợp lệ.");

        RuleFor(x => x.RamFormFactor)
            .IsInEnum()
            .When(x => x.RamFormFactor.HasValue)
            .WithMessage("Kích thước RAM không hợp lệ.");

        RuleFor(x => x.StorageType)
            .IsInEnum()
            .When(x => x.StorageType.HasValue)
            .WithMessage("Loại ổ cứng không hợp lệ.");

        RuleFor(x => x.StorageInterface)
            .IsInEnum()
            .When(x => x.StorageInterface.HasValue)
            .WithMessage("Chuẩn giao tiếp ổ cứng không hợp lệ.");

        RuleFor(x => x.PsuEfficiency)
            .IsInEnum()
            .When(x => x.PsuEfficiency.HasValue)
            .WithMessage("Chuẩn hiệu suất nguồn không hợp lệ.");

        RuleFor(x => x.PsuModularity)
            .IsInEnum()
            .When(x => x.PsuModularity.HasValue)
            .WithMessage("Loại dây nguồn không hợp lệ.");

        RuleFor(x => x.PsuFormFactor)
            .IsInEnum()
            .When(x => x.PsuFormFactor.HasValue)
            .WithMessage("Kích thước nguồn không hợp lệ.");

        RuleFor(x => x.CoolerType)
            .IsInEnum()
            .When(x => x.CoolerType.HasValue)
            .WithMessage("Loại tản nhiệt không hợp lệ.");

        // If any technical filter is present, ComponentType is required
        RuleFor(x => x.ComponentType)
            .NotNull()
            .When(HasAnyTechnicalFilter)
            .WithMessage("Loại linh kiện là bắt buộc khi sử dụng bộ lọc thông số kỹ thuật.");

        // Applicability matrix validation
        RuleFor(x => x.Socket)
            .Must((options, _) => options.ComponentType is ComponentType.Cpu or ComponentType.Motherboard or ComponentType.Cooler)
            .When(x => x.ComponentType.HasValue && x.Socket.HasValue)
            .WithMessage("Bộ lọc socket chỉ áp dụng cho CPU, Bo mạch chủ hoặc Tản nhiệt.");

        RuleFor(x => x.MemoryType)
            .Must((options, _) => options.ComponentType is ComponentType.Cpu or ComponentType.Motherboard or ComponentType.Ram)
            .When(x => x.ComponentType.HasValue && x.MemoryType.HasValue)
            .WithMessage("Bộ lọc loại bộ nhớ chỉ áp dụng cho CPU, Bo mạch chủ hoặc RAM.");

        RuleFor(x => x.MotherboardFormFactor)
            .Must((options, _) => options.ComponentType is ComponentType.Motherboard or ComponentType.Case)
            .When(x => x.ComponentType.HasValue && x.MotherboardFormFactor.HasValue)
            .WithMessage("Bộ lọc kích thước bo mạch chủ chỉ áp dụng cho Bo mạch chủ hoặc Vỏ case.");

        RuleFor(x => x.RamFormFactor)
            .Must((options, _) => options.ComponentType is ComponentType.Ram)
            .When(x => x.ComponentType.HasValue && x.RamFormFactor.HasValue)
            .WithMessage("Bộ lọc kích thước RAM chỉ áp dụng cho RAM.");

        RuleFor(x => x.StorageType)
            .Must((options, _) => options.ComponentType is ComponentType.Storage)
            .When(x => x.ComponentType.HasValue && x.StorageType.HasValue)
            .WithMessage("Bộ lọc loại ổ cứng chỉ áp dụng cho Ổ cứng lưu trữ.");

        RuleFor(x => x.StorageInterface)
            .Must((options, _) => options.ComponentType is ComponentType.Storage)
            .When(x => x.ComponentType.HasValue && x.StorageInterface.HasValue)
            .WithMessage("Bộ lọc chuẩn giao tiếp chỉ áp dụng cho Ổ cứng lưu trữ.");

        RuleFor(x => x.PsuEfficiency)
            .Must((options, _) => options.ComponentType is ComponentType.Psu)
            .When(x => x.ComponentType.HasValue && x.PsuEfficiency.HasValue)
            .WithMessage("Bộ lọc chuẩn hiệu suất chỉ áp dụng cho Nguồn máy tính (PSU).");

        RuleFor(x => x.PsuModularity)
            .Must((options, _) => options.ComponentType is ComponentType.Psu)
            .When(x => x.ComponentType.HasValue && x.PsuModularity.HasValue)
            .WithMessage("Bộ lọc loại dây nguồn chỉ áp dụng cho Nguồn máy tính (PSU).");

        RuleFor(x => x.PsuFormFactor)
            .Must((options, _) => options.ComponentType is ComponentType.Psu)
            .When(x => x.ComponentType.HasValue && x.PsuFormFactor.HasValue)
            .WithMessage("Bộ lọc kích thước nguồn chỉ áp dụng cho Nguồn máy tính (PSU).");

        RuleFor(x => x.CoolerType)
            .Must((options, _) => options.ComponentType is ComponentType.Cooler)
            .When(x => x.ComponentType.HasValue && x.CoolerType.HasValue)
            .WithMessage("Bộ lọc loại tản nhiệt chỉ áp dụng cho Tản nhiệt.");

        RuleFor(x => x.HasIntegratedGpu)
            .Must((options, _) => options.ComponentType is ComponentType.Cpu)
            .When(x => x.ComponentType.HasValue && x.HasIntegratedGpu.HasValue)
            .WithMessage("Bộ lọc GPU tích hợp chỉ áp dụng cho CPU.");

        RuleFor(x => x.HasWifi)
            .Must((options, _) => options.ComponentType is ComponentType.Motherboard)
            .When(x => x.ComponentType.HasValue && x.HasWifi.HasValue)
            .WithMessage("Bộ lọc kết nối Wi-Fi chỉ áp dụng cho Bo mạch chủ.");

        RuleFor(x => x.HasRgb)
            .Must((options, _) => options.ComponentType is ComponentType.Ram or ComponentType.Cooler)
            .When(x => x.ComponentType.HasValue && x.HasRgb.HasValue)
            .WithMessage("Bộ lọc đèn RGB chỉ áp dụng cho RAM hoặc Tản nhiệt.");

        RuleFor(x => x.Has12VHPWR)
            .Must((options, _) => options.ComponentType is ComponentType.Psu)
            .When(x => x.ComponentType.HasValue && x.Has12VHPWR.HasValue)
            .WithMessage("Bộ lọc cổng 12VHPWR chỉ áp dụng cho Nguồn máy tính (PSU).");

        RuleFor(x => x.Requires12VHPWR)
            .Must((options, _) => options.ComponentType is ComponentType.Gpu)
            .When(x => x.ComponentType.HasValue && x.Requires12VHPWR.HasValue)
            .WithMessage("Bộ lọc yêu cầu cổng 12VHPWR chỉ áp dụng cho Card đồ họa (GPU).");
    }

    public static bool HasAnyTechnicalFilter(ProductFilterOptions x) =>
        x.Socket.HasValue ||
        x.MemoryType.HasValue ||
        x.MotherboardFormFactor.HasValue ||
        x.RamFormFactor.HasValue ||
        x.StorageType.HasValue ||
        x.StorageInterface.HasValue ||
        x.PsuEfficiency.HasValue ||
        x.PsuModularity.HasValue ||
        x.PsuFormFactor.HasValue ||
        x.CoolerType.HasValue ||
        x.HasIntegratedGpu.HasValue ||
        x.HasWifi.HasValue ||
        x.HasRgb.HasValue ||
        x.Has12VHPWR.HasValue ||
        x.Requires12VHPWR.HasValue;
}
