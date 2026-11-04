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

    public CpuSocket? Socket { get; set; }
    public RamType? MemoryType { get; set; }
    public MotherboardFormFactor? MotherboardFormFactor { get; set; }
    public RamFormFactor? RamFormFactor { get; set; }
    public StorageType? StorageType { get; set; }
    public StorageInterface? StorageInterface { get; set; }
    public PsuEfficiency? PsuEfficiency { get; set; }
    public PsuModularity? PsuModularity { get; set; }
    public PsuFormFactor? PsuFormFactor { get; set; }
    public CoolerType? CoolerType { get; set; }
    public bool? HasIntegratedGpu { get; set; }
    public bool? HasWifi { get; set; }
    public bool? HasRgb { get; set; }
    public bool? Has12VHPWR { get; set; }
    public bool? Requires12VHPWR { get; set; }

    public List<string>? Brands { get; set; }
    public List<CpuSocket>? Sockets { get; set; }
    public List<RamType>? MemoryTypes { get; set; }
    public List<MotherboardFormFactor>? MotherboardFormFactors { get; set; }
    public List<RamFormFactor>? RamFormFactors { get; set; }
    public List<StorageType>? StorageTypes { get; set; }
    public List<StorageInterface>? StorageInterfaces { get; set; }
    public List<PsuEfficiency>? PsuEfficiencies { get; set; }
    public List<PsuModularity>? PsuModularities { get; set; }
    public List<PsuFormFactor>? PsuFormFactors { get; set; }
    public List<CoolerType>? CoolerTypes { get; set; }
    public List<int>? CoreCounts { get; set; }
    public List<int>? VramGbs { get; set; }
    public List<int>? GpuLengthsMm { get; set; }
    public List<int>? RamCapacitiesGb { get; set; }
    public List<int>? RamSpeedsMhz { get; set; }
    public List<int>? StorageCapacitiesGb { get; set; }
    public List<int>? PsuWattages { get; set; }

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
        SortBy: SortBy,
        Socket: Socket,
        MemoryType: MemoryType,
        MotherboardFormFactor: MotherboardFormFactor,
        RamFormFactor: RamFormFactor,
        StorageType: StorageType,
        StorageInterface: StorageInterface,
        PsuEfficiency: PsuEfficiency,
        PsuModularity: PsuModularity,
        PsuFormFactor: PsuFormFactor,
        CoolerType: CoolerType,
        HasIntegratedGpu: HasIntegratedGpu,
        HasWifi: HasWifi,
        HasRgb: HasRgb,
        Has12VHPWR: Has12VHPWR,
        Requires12VHPWR: Requires12VHPWR,
        Brands: Brands,
        Sockets: Sockets,
        MemoryTypes: MemoryTypes,
        MotherboardFormFactors: MotherboardFormFactors,
        RamFormFactors: RamFormFactors,
        StorageTypes: StorageTypes,
        StorageInterfaces: StorageInterfaces,
        PsuEfficiencies: PsuEfficiencies,
        PsuModularities: PsuModularities,
        PsuFormFactors: PsuFormFactors,
        CoolerTypes: CoolerTypes,
        CoreCounts: CoreCounts,
        VramGbs: VramGbs,
        GpuLengthsMm: GpuLengthsMm,
        RamCapacitiesGb: RamCapacitiesGb,
        RamSpeedsMhz: RamSpeedsMhz,
        StorageCapacitiesGb: StorageCapacitiesGb,
        PsuWattages: PsuWattages
    );
}

public class ProductFilterOptionsRequest
{
    public ComponentType? ComponentType { get; set; }
    public Guid? CategoryId { get; set; }
    public bool? IsActive { get; set; }
    public bool? InStock { get; set; }
}
