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
    bool? Requires12VHPWR = null,
    IReadOnlyList<string>? Brands = null,
    IReadOnlyList<CpuSocket>? Sockets = null,
    IReadOnlyList<RamType>? MemoryTypes = null,
    IReadOnlyList<MotherboardFormFactor>? MotherboardFormFactors = null,
    IReadOnlyList<RamFormFactor>? RamFormFactors = null,
    IReadOnlyList<StorageType>? StorageTypes = null,
    IReadOnlyList<StorageInterface>? StorageInterfaces = null,
    IReadOnlyList<PsuEfficiency>? PsuEfficiencies = null,
    IReadOnlyList<PsuModularity>? PsuModularities = null,
    IReadOnlyList<PsuFormFactor>? PsuFormFactors = null,
    IReadOnlyList<CoolerType>? CoolerTypes = null,
    IReadOnlyList<int>? CoreCounts = null,
    IReadOnlyList<int>? VramGbs = null,
    IReadOnlyList<int>? GpuLengthsMm = null,
    IReadOnlyList<int>? RamCapacitiesGb = null,
    IReadOnlyList<int>? RamSpeedsMhz = null,
    IReadOnlyList<int>? StorageCapacitiesGb = null,
    IReadOnlyList<int>? PsuWattages = null
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

        // Technical enum filters validation (scalar)
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

        // Brand list validation
        RuleFor(x => x.Brands)
            .Must(l => l == null || l.Count <= 20)
            .WithMessage("Danh sách thương hiệu không được vượt quá 20 phần tử.");

        RuleFor(x => x.Brands)
            .Must(l => l == null || l.All(b => !string.IsNullOrWhiteSpace(b)))
            .WithMessage("Tên thương hiệu trong danh sách không được để trống.");

        RuleFor(x => x.Brands)
            .Must(l => l == null || l.All(b => b != null && b.Length <= 100))
            .WithMessage("Tên thương hiệu trong danh sách không được vượt quá 100 ký tự.");

        // Technical list filters max 20 elements validation
        RuleFor(x => x.Sockets)
            .Must(l => l == null || l.Count <= 20)
            .WithMessage("Danh sách socket không được vượt quá 20 phần tử.");

        RuleFor(x => x.MemoryTypes)
            .Must(l => l == null || l.Count <= 20)
            .WithMessage("Danh sách loại bộ nhớ không được vượt quá 20 phần tử.");

        RuleFor(x => x.MotherboardFormFactors)
            .Must(l => l == null || l.Count <= 20)
            .WithMessage("Danh sách kích thước bo mạch chủ không được vượt quá 20 phần tử.");

        RuleFor(x => x.RamFormFactors)
            .Must(l => l == null || l.Count <= 20)
            .WithMessage("Danh sách kích thước RAM không được vượt quá 20 phần tử.");

        RuleFor(x => x.StorageTypes)
            .Must(l => l == null || l.Count <= 20)
            .WithMessage("Danh sách loại ổ cứng không được vượt quá 20 phần tử.");

        RuleFor(x => x.StorageInterfaces)
            .Must(l => l == null || l.Count <= 20)
            .WithMessage("Danh sách chuẩn giao tiếp không được vượt quá 20 phần tử.");

        RuleFor(x => x.PsuEfficiencies)
            .Must(l => l == null || l.Count <= 20)
            .WithMessage("Danh sách chuẩn hiệu suất không được vượt quá 20 phần tử.");

        RuleFor(x => x.PsuModularities)
            .Must(l => l == null || l.Count <= 20)
            .WithMessage("Danh sách loại dây nguồn không được vượt quá 20 phần tử.");

        RuleFor(x => x.PsuFormFactors)
            .Must(l => l == null || l.Count <= 20)
            .WithMessage("Danh sách kích thước nguồn không được vượt quá 20 phần tử.");

        RuleFor(x => x.CoolerTypes)
            .Must(l => l == null || l.Count <= 20)
            .WithMessage("Danh sách loại tản nhiệt không được vượt quá 20 phần tử.");

        RuleFor(x => x.CoreCounts)
            .Must(l => l == null || l.Count <= 20)
            .WithMessage("Danh sách số nhân không được vượt quá 20 phần tử.");

        RuleFor(x => x.VramGbs)
            .Must(l => l == null || l.Count <= 20)
            .WithMessage("Danh sách dung lượng VRAM không được vượt quá 20 phần tử.");

        RuleFor(x => x.GpuLengthsMm)
            .Must(l => l == null || l.Count <= 20)
            .WithMessage("Danh sách chiều dài card đồ họa không được vượt quá 20 phần tử.");

        RuleFor(x => x.RamCapacitiesGb)
            .Must(l => l == null || l.Count <= 20)
            .WithMessage("Danh sách dung lượng RAM không được vượt quá 20 phần tử.");

        RuleFor(x => x.RamSpeedsMhz)
            .Must(l => l == null || l.Count <= 20)
            .WithMessage("Danh sách tốc độ RAM không được vượt quá 20 phần tử.");

        RuleFor(x => x.StorageCapacitiesGb)
            .Must(l => l == null || l.Count <= 20)
            .WithMessage("Danh sách dung lượng ổ cứng không được vượt quá 20 phần tử.");

        RuleFor(x => x.PsuWattages)
            .Must(l => l == null || l.Count <= 20)
            .WithMessage("Danh sách công suất nguồn không được vượt quá 20 phần tử.");

        // Technical enum list element validation
        RuleFor(x => x.Sockets)
            .Must(l => l == null || l.All(e => Enum.IsDefined(typeof(CpuSocket), e)))
            .WithMessage("Loại socket không hợp lệ.");

        RuleFor(x => x.MemoryTypes)
            .Must(l => l == null || l.All(e => Enum.IsDefined(typeof(RamType), e)))
            .WithMessage("Loại bộ nhớ (RAM) không hợp lệ.");

        RuleFor(x => x.MotherboardFormFactors)
            .Must(l => l == null || l.All(e => Enum.IsDefined(typeof(MotherboardFormFactor), e)))
            .WithMessage("Kích thước bo mạch chủ không hợp lệ.");

        RuleFor(x => x.RamFormFactors)
            .Must(l => l == null || l.All(e => Enum.IsDefined(typeof(RamFormFactor), e)))
            .WithMessage("Kích thước RAM không hợp lệ.");

        RuleFor(x => x.StorageTypes)
            .Must(l => l == null || l.All(e => Enum.IsDefined(typeof(StorageType), e)))
            .WithMessage("Loại ổ cứng không hợp lệ.");

        RuleFor(x => x.StorageInterfaces)
            .Must(l => l == null || l.All(e => Enum.IsDefined(typeof(StorageInterface), e)))
            .WithMessage("Chuẩn giao tiếp ổ cứng không hợp lệ.");

        RuleFor(x => x.PsuEfficiencies)
            .Must(l => l == null || l.All(e => Enum.IsDefined(typeof(PsuEfficiency), e)))
            .WithMessage("Chuẩn hiệu suất nguồn không hợp lệ.");

        RuleFor(x => x.PsuModularities)
            .Must(l => l == null || l.All(e => Enum.IsDefined(typeof(PsuModularity), e)))
            .WithMessage("Loại dây nguồn không hợp lệ.");

        RuleFor(x => x.PsuFormFactors)
            .Must(l => l == null || l.All(e => Enum.IsDefined(typeof(PsuFormFactor), e)))
            .WithMessage("Kích thước nguồn không hợp lệ.");

        RuleFor(x => x.CoolerTypes)
            .Must(l => l == null || l.All(e => Enum.IsDefined(typeof(CoolerType), e)))
            .WithMessage("Loại tản nhiệt không hợp lệ.");

        // Technical numeric list element validation (> 0)
        RuleFor(x => x.CoreCounts)
            .Must(l => l == null || l.All(v => v > 0))
            .WithMessage("Số nhân CPU phải lớn hơn 0.");

        RuleFor(x => x.VramGbs)
            .Must(l => l == null || l.All(v => v > 0))
            .WithMessage("Dung lượng VRAM phải lớn hơn 0.");

        RuleFor(x => x.GpuLengthsMm)
            .Must(l => l == null || l.All(v => v > 0))
            .WithMessage("Chiều dài card đồ họa phải lớn hơn 0.");

        RuleFor(x => x.RamCapacitiesGb)
            .Must(l => l == null || l.All(v => v > 0))
            .WithMessage("Dung lượng RAM phải lớn hơn 0.");

        RuleFor(x => x.RamSpeedsMhz)
            .Must(l => l == null || l.All(v => v > 0))
            .WithMessage("Tốc độ RAM phải lớn hơn 0.");

        RuleFor(x => x.StorageCapacitiesGb)
            .Must(l => l == null || l.All(v => v > 0))
            .WithMessage("Dung lượng ổ cứng phải lớn hơn 0.");

        RuleFor(x => x.PsuWattages)
            .Must(l => l == null || l.All(v => v > 0))
            .WithMessage("Công suất nguồn phải lớn hơn 0.");

        // If any technical filter is present, ComponentType is required
        RuleFor(x => x.ComponentType)
            .NotNull()
            .When(HasAnyTechnicalFilter)
            .WithMessage("Loại linh kiện là bắt buộc khi sử dụng bộ lọc thông số kỹ thuật.");

        // Applicability matrix validation (scalar)
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

        // Applicability matrix validation (lists)
        RuleFor(x => x.Sockets)
            .Must((options, _) => options.ComponentType is ComponentType.Cpu or ComponentType.Motherboard or ComponentType.Cooler)
            .When(x => x.ComponentType.HasValue && x.Sockets != null && x.Sockets.Count > 0)
            .WithMessage("Bộ lọc socket chỉ áp dụng cho CPU, Bo mạch chủ hoặc Tản nhiệt.");

        RuleFor(x => x.MemoryTypes)
            .Must((options, _) => options.ComponentType is ComponentType.Cpu or ComponentType.Motherboard or ComponentType.Ram)
            .When(x => x.ComponentType.HasValue && x.MemoryTypes != null && x.MemoryTypes.Count > 0)
            .WithMessage("Bộ lọc loại bộ nhớ chỉ áp dụng cho CPU, Bo mạch chủ hoặc RAM.");

        RuleFor(x => x.MotherboardFormFactors)
            .Must((options, _) => options.ComponentType is ComponentType.Motherboard or ComponentType.Case)
            .When(x => x.ComponentType.HasValue && x.MotherboardFormFactors != null && x.MotherboardFormFactors.Count > 0)
            .WithMessage("Bộ lọc kích thước bo mạch chủ chỉ áp dụng cho Bo mạch chủ hoặc Vỏ case.");

        RuleFor(x => x.RamFormFactors)
            .Must((options, _) => options.ComponentType is ComponentType.Ram)
            .When(x => x.ComponentType.HasValue && x.RamFormFactors != null && x.RamFormFactors.Count > 0)
            .WithMessage("Bộ lọc kích thước RAM chỉ áp dụng cho RAM.");

        RuleFor(x => x.StorageTypes)
            .Must((options, _) => options.ComponentType is ComponentType.Storage)
            .When(x => x.ComponentType.HasValue && x.StorageTypes != null && x.StorageTypes.Count > 0)
            .WithMessage("Bộ lọc loại ổ cứng chỉ áp dụng cho Ổ cứng lưu trữ.");

        RuleFor(x => x.StorageInterfaces)
            .Must((options, _) => options.ComponentType is ComponentType.Storage)
            .When(x => x.ComponentType.HasValue && x.StorageInterfaces != null && x.StorageInterfaces.Count > 0)
            .WithMessage("Bộ lọc chuẩn giao tiếp chỉ áp dụng cho Ổ cứng lưu trữ.");

        RuleFor(x => x.PsuEfficiencies)
            .Must((options, _) => options.ComponentType is ComponentType.Psu)
            .When(x => x.ComponentType.HasValue && x.PsuEfficiencies != null && x.PsuEfficiencies.Count > 0)
            .WithMessage("Bộ lọc chuẩn hiệu suất chỉ áp dụng cho Nguồn máy tính (PSU).");

        RuleFor(x => x.PsuModularities)
            .Must((options, _) => options.ComponentType is ComponentType.Psu)
            .When(x => x.ComponentType.HasValue && x.PsuModularities != null && x.PsuModularities.Count > 0)
            .WithMessage("Bộ lọc loại dây nguồn chỉ áp dụng cho Nguồn máy tính (PSU).");

        RuleFor(x => x.PsuFormFactors)
            .Must((options, _) => options.ComponentType is ComponentType.Psu)
            .When(x => x.ComponentType.HasValue && x.PsuFormFactors != null && x.PsuFormFactors.Count > 0)
            .WithMessage("Bộ lọc kích thước nguồn chỉ áp dụng cho Nguồn máy tính (PSU).");

        RuleFor(x => x.CoolerTypes)
            .Must((options, _) => options.ComponentType is ComponentType.Cooler)
            .When(x => x.ComponentType.HasValue && x.CoolerTypes != null && x.CoolerTypes.Count > 0)
            .WithMessage("Bộ lọc loại tản nhiệt chỉ áp dụng cho Tản nhiệt.");

        RuleFor(x => x.CoreCounts)
            .Must((options, _) => options.ComponentType is ComponentType.Cpu)
            .When(x => x.ComponentType.HasValue && x.CoreCounts != null && x.CoreCounts.Count > 0)
            .WithMessage("Bộ lọc số nhân chỉ áp dụng cho CPU.");

        RuleFor(x => x.VramGbs)
            .Must((options, _) => options.ComponentType is ComponentType.Gpu)
            .When(x => x.ComponentType.HasValue && x.VramGbs != null && x.VramGbs.Count > 0)
            .WithMessage("Bộ lọc dung lượng VRAM chỉ áp dụng cho Card đồ họa (GPU).");

        RuleFor(x => x.GpuLengthsMm)
            .Must((options, _) => options.ComponentType is ComponentType.Gpu)
            .When(x => x.ComponentType.HasValue && x.GpuLengthsMm != null && x.GpuLengthsMm.Count > 0)
            .WithMessage("Bộ lọc chiều dài card đồ họa chỉ áp dụng cho Card đồ họa (GPU).");

        RuleFor(x => x.RamCapacitiesGb)
            .Must((options, _) => options.ComponentType is ComponentType.Ram)
            .When(x => x.ComponentType.HasValue && x.RamCapacitiesGb != null && x.RamCapacitiesGb.Count > 0)
            .WithMessage("Bộ lọc dung lượng RAM chỉ áp dụng cho RAM.");

        RuleFor(x => x.RamSpeedsMhz)
            .Must((options, _) => options.ComponentType is ComponentType.Ram)
            .When(x => x.ComponentType.HasValue && x.RamSpeedsMhz != null && x.RamSpeedsMhz.Count > 0)
            .WithMessage("Bộ lọc tốc độ RAM chỉ áp dụng cho RAM.");

        RuleFor(x => x.StorageCapacitiesGb)
            .Must((options, _) => options.ComponentType is ComponentType.Storage)
            .When(x => x.ComponentType.HasValue && x.StorageCapacitiesGb != null && x.StorageCapacitiesGb.Count > 0)
            .WithMessage("Bộ lọc dung lượng ổ cứng chỉ áp dụng cho Ổ cứng lưu trữ.");

        RuleFor(x => x.PsuWattages)
            .Must((options, _) => options.ComponentType is ComponentType.Psu)
            .When(x => x.ComponentType.HasValue && x.PsuWattages != null && x.PsuWattages.Count > 0)
            .WithMessage("Bộ lọc công suất nguồn chỉ áp dụng cho Nguồn máy tính (PSU).");
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
        x.Requires12VHPWR.HasValue ||
        (x.Sockets != null && x.Sockets.Count > 0) ||
        (x.MemoryTypes != null && x.MemoryTypes.Count > 0) ||
        (x.MotherboardFormFactors != null && x.MotherboardFormFactors.Count > 0) ||
        (x.RamFormFactors != null && x.RamFormFactors.Count > 0) ||
        (x.StorageTypes != null && x.StorageTypes.Count > 0) ||
        (x.StorageInterfaces != null && x.StorageInterfaces.Count > 0) ||
        (x.PsuEfficiencies != null && x.PsuEfficiencies.Count > 0) ||
        (x.PsuModularities != null && x.PsuModularities.Count > 0) ||
        (x.PsuFormFactors != null && x.PsuFormFactors.Count > 0) ||
        (x.CoolerTypes != null && x.CoolerTypes.Count > 0) ||
        (x.CoreCounts != null && x.CoreCounts.Count > 0) ||
        (x.VramGbs != null && x.VramGbs.Count > 0) ||
        (x.GpuLengthsMm != null && x.GpuLengthsMm.Count > 0) ||
        (x.RamCapacitiesGb != null && x.RamCapacitiesGb.Count > 0) ||
        (x.RamSpeedsMhz != null && x.RamSpeedsMhz.Count > 0) ||
        (x.StorageCapacitiesGb != null && x.StorageCapacitiesGb.Count > 0) ||
        (x.PsuWattages != null && x.PsuWattages.Count > 0);
}
