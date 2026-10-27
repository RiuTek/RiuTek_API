using FluentValidation.TestHelper;
using RiuTek.Application.Features.Products.Queries;
using RiuTek.Core.Enums;
using Xunit;

namespace RiuTek.Application.Test.Features.Products;

public class ProductFilterOptionsValidatorTests
{
    private readonly ProductFilterOptionsValidator _validator = new();

    #region Enum Validity Tests

    [Fact]
    public void Validator_WhenSocketIsInvalidEnum_FailsValidation()
    {
        var options = new ProductFilterOptions(ComponentType: ComponentType.Cpu, Socket: (CpuSocket)9999);
        var result = _validator.TestValidate(options);
        result.ShouldHaveValidationErrorFor(x => x.Socket);
    }

    [Fact]
    public void Validator_WhenMemoryTypeIsInvalidEnum_FailsValidation()
    {
        var options = new ProductFilterOptions(ComponentType: ComponentType.Ram, MemoryType: (RamType)9999);
        var result = _validator.TestValidate(options);
        result.ShouldHaveValidationErrorFor(x => x.MemoryType);
    }

    [Fact]
    public void Validator_WhenMotherboardFormFactorIsInvalidEnum_FailsValidation()
    {
        var options = new ProductFilterOptions(ComponentType: ComponentType.Motherboard, MotherboardFormFactor: (MotherboardFormFactor)9999);
        var result = _validator.TestValidate(options);
        result.ShouldHaveValidationErrorFor(x => x.MotherboardFormFactor);
    }

    [Fact]
    public void Validator_WhenRamFormFactorIsInvalidEnum_FailsValidation()
    {
        var options = new ProductFilterOptions(ComponentType: ComponentType.Ram, RamFormFactor: (RamFormFactor)9999);
        var result = _validator.TestValidate(options);
        result.ShouldHaveValidationErrorFor(x => x.RamFormFactor);
    }

    [Fact]
    public void Validator_WhenStorageTypeIsInvalidEnum_FailsValidation()
    {
        var options = new ProductFilterOptions(ComponentType: ComponentType.Storage, StorageType: (StorageType)9999);
        var result = _validator.TestValidate(options);
        result.ShouldHaveValidationErrorFor(x => x.StorageType);
    }

    [Fact]
    public void Validator_WhenStorageInterfaceIsInvalidEnum_FailsValidation()
    {
        var options = new ProductFilterOptions(ComponentType: ComponentType.Storage, StorageInterface: (StorageInterface)9999);
        var result = _validator.TestValidate(options);
        result.ShouldHaveValidationErrorFor(x => x.StorageInterface);
    }

    [Fact]
    public void Validator_WhenPsuEfficiencyIsInvalidEnum_FailsValidation()
    {
        var options = new ProductFilterOptions(ComponentType: ComponentType.Psu, PsuEfficiency: (PsuEfficiency)9999);
        var result = _validator.TestValidate(options);
        result.ShouldHaveValidationErrorFor(x => x.PsuEfficiency);
    }

    [Fact]
    public void Validator_WhenPsuModularityIsInvalidEnum_FailsValidation()
    {
        var options = new ProductFilterOptions(ComponentType: ComponentType.Psu, PsuModularity: (PsuModularity)9999);
        var result = _validator.TestValidate(options);
        result.ShouldHaveValidationErrorFor(x => x.PsuModularity);
    }

    [Fact]
    public void Validator_WhenPsuFormFactorIsInvalidEnum_FailsValidation()
    {
        var options = new ProductFilterOptions(ComponentType: ComponentType.Psu, PsuFormFactor: (PsuFormFactor)9999);
        var result = _validator.TestValidate(options);
        result.ShouldHaveValidationErrorFor(x => x.PsuFormFactor);
    }

    [Fact]
    public void Validator_WhenCoolerTypeIsInvalidEnum_FailsValidation()
    {
        var options = new ProductFilterOptions(ComponentType: ComponentType.Cooler, CoolerType: (CoolerType)9999);
        var result = _validator.TestValidate(options);
        result.ShouldHaveValidationErrorFor(x => x.CoolerType);
    }

    #endregion

    #region Missing ComponentType Tests

    [Theory]
    [InlineData("Socket")]
    [InlineData("MemoryType")]
    [InlineData("MotherboardFormFactor")]
    [InlineData("RamFormFactor")]
    [InlineData("StorageType")]
    [InlineData("StorageInterface")]
    [InlineData("PsuEfficiency")]
    [InlineData("PsuModularity")]
    [InlineData("PsuFormFactor")]
    [InlineData("CoolerType")]
    [InlineData("HasIntegratedGpu")]
    [InlineData("HasWifi")]
    [InlineData("HasRgb")]
    [InlineData("Has12VHPWR")]
    [InlineData("Requires12VHPWR")]
    public void Validator_WhenTechnicalFilterProvidedWithoutComponentType_FailsValidation(string filterName)
    {
        var options = filterName switch
        {
            "Socket" => new ProductFilterOptions(Socket: CpuSocket.AM5),
            "MemoryType" => new ProductFilterOptions(MemoryType: RamType.DDR5),
            "MotherboardFormFactor" => new ProductFilterOptions(MotherboardFormFactor: MotherboardFormFactor.ATX),
            "RamFormFactor" => new ProductFilterOptions(RamFormFactor: RamFormFactor.DIMM),
            "StorageType" => new ProductFilterOptions(StorageType: StorageType.NVMe_M2),
            "StorageInterface" => new ProductFilterOptions(StorageInterface: StorageInterface.PCIe_4_0),
            "PsuEfficiency" => new ProductFilterOptions(PsuEfficiency: PsuEfficiency.Plus80Gold),
            "PsuModularity" => new ProductFilterOptions(PsuModularity: PsuModularity.FullModular),
            "PsuFormFactor" => new ProductFilterOptions(PsuFormFactor: PsuFormFactor.ATX),
            "CoolerType" => new ProductFilterOptions(CoolerType: CoolerType.Air),
            "HasIntegratedGpu" => new ProductFilterOptions(HasIntegratedGpu: true),
            "HasWifi" => new ProductFilterOptions(HasWifi: true),
            "HasRgb" => new ProductFilterOptions(HasRgb: true),
            "Has12VHPWR" => new ProductFilterOptions(Has12VHPWR: true),
            "Requires12VHPWR" => new ProductFilterOptions(Requires12VHPWR: true),
            _ => throw new ArgumentException($"Unknown filter {filterName}")
        };

        var result = _validator.TestValidate(options);
        result.ShouldHaveValidationErrorFor(x => x.ComponentType)
            .WithErrorMessage("Loại linh kiện là bắt buộc khi sử dụng bộ lọc thông số kỹ thuật.");
    }

    #endregion

    #region Valid Applicability Tests

    [Theory]
    [InlineData(ComponentType.Cpu)]
    [InlineData(ComponentType.Motherboard)]
    [InlineData(ComponentType.Cooler)]
    public void Validator_Socket_Passes_ForValidComponentTypes(ComponentType type)
    {
        var options = new ProductFilterOptions(ComponentType: type, Socket: CpuSocket.LGA1700);
        var result = _validator.TestValidate(options);
        result.ShouldNotHaveValidationErrorFor(x => x.Socket);
    }

    [Theory]
    [InlineData(ComponentType.Cpu)]
    [InlineData(ComponentType.Motherboard)]
    [InlineData(ComponentType.Ram)]
    public void Validator_MemoryType_Passes_ForValidComponentTypes(ComponentType type)
    {
        var options = new ProductFilterOptions(ComponentType: type, MemoryType: RamType.DDR5);
        var result = _validator.TestValidate(options);
        result.ShouldNotHaveValidationErrorFor(x => x.MemoryType);
    }

    [Theory]
    [InlineData(ComponentType.Motherboard)]
    [InlineData(ComponentType.Case)]
    public void Validator_MotherboardFormFactor_Passes_ForValidComponentTypes(ComponentType type)
    {
        var options = new ProductFilterOptions(ComponentType: type, MotherboardFormFactor: MotherboardFormFactor.ATX);
        var result = _validator.TestValidate(options);
        result.ShouldNotHaveValidationErrorFor(x => x.MotherboardFormFactor);
    }

    [Fact]
    public void Validator_RamFormFactor_Passes_ForRam()
    {
        var options = new ProductFilterOptions(ComponentType: ComponentType.Ram, RamFormFactor: RamFormFactor.DIMM);
        var result = _validator.TestValidate(options);
        result.ShouldNotHaveValidationErrorFor(x => x.RamFormFactor);
    }

    [Fact]
    public void Validator_StorageFilters_Pass_ForStorage()
    {
        var options = new ProductFilterOptions(
            ComponentType: ComponentType.Storage,
            StorageType: StorageType.NVMe_M2,
            StorageInterface: StorageInterface.PCIe_4_0);
        var result = _validator.TestValidate(options);
        result.ShouldNotHaveValidationErrorFor(x => x.StorageType);
        result.ShouldNotHaveValidationErrorFor(x => x.StorageInterface);
    }

    [Fact]
    public void Validator_PsuFilters_Pass_ForPsu()
    {
        var options = new ProductFilterOptions(
            ComponentType: ComponentType.Psu,
            PsuEfficiency: PsuEfficiency.Plus80Gold,
            PsuModularity: PsuModularity.FullModular,
            PsuFormFactor: PsuFormFactor.ATX,
            Has12VHPWR: true);
        var result = _validator.TestValidate(options);
        result.ShouldNotHaveValidationErrorFor(x => x.PsuEfficiency);
        result.ShouldNotHaveValidationErrorFor(x => x.PsuModularity);
        result.ShouldNotHaveValidationErrorFor(x => x.PsuFormFactor);
        result.ShouldNotHaveValidationErrorFor(x => x.Has12VHPWR);
    }

    [Fact]
    public void Validator_CoolerFilters_Pass_ForCooler()
    {
        var options = new ProductFilterOptions(
            ComponentType: ComponentType.Cooler,
            CoolerType: CoolerType.AIO_360,
            Socket: CpuSocket.AM5,
            HasRgb: true);
        var result = _validator.TestValidate(options);
        result.ShouldNotHaveValidationErrorFor(x => x.CoolerType);
        result.ShouldNotHaveValidationErrorFor(x => x.Socket);
        result.ShouldNotHaveValidationErrorFor(x => x.HasRgb);
    }

    [Fact]
    public void Validator_CpuFilters_Pass_ForCpu()
    {
        var options = new ProductFilterOptions(
            ComponentType: ComponentType.Cpu,
            Socket: CpuSocket.LGA1700,
            MemoryType: RamType.DDR5,
            HasIntegratedGpu: false);
        var result = _validator.TestValidate(options);
        result.ShouldNotHaveValidationErrorFor(x => x.Socket);
        result.ShouldNotHaveValidationErrorFor(x => x.MemoryType);
        result.ShouldNotHaveValidationErrorFor(x => x.HasIntegratedGpu);
    }

    [Fact]
    public void Validator_MotherboardFilters_Pass_ForMotherboard()
    {
        var options = new ProductFilterOptions(
            ComponentType: ComponentType.Motherboard,
            Socket: CpuSocket.AM5,
            MemoryType: RamType.DDR5,
            MotherboardFormFactor: MotherboardFormFactor.ATX,
            HasWifi: true);
        var result = _validator.TestValidate(options);
        result.ShouldNotHaveValidationErrorFor(x => x.Socket);
        result.ShouldNotHaveValidationErrorFor(x => x.MemoryType);
        result.ShouldNotHaveValidationErrorFor(x => x.MotherboardFormFactor);
        result.ShouldNotHaveValidationErrorFor(x => x.HasWifi);
    }

    [Fact]
    public void Validator_GpuFilters_Pass_ForGpu()
    {
        var options = new ProductFilterOptions(
            ComponentType: ComponentType.Gpu,
            Requires12VHPWR: true);
        var result = _validator.TestValidate(options);
        result.ShouldNotHaveValidationErrorFor(x => x.Requires12VHPWR);
    }

    #endregion

    #region Invalid Applicability Tests

    [Theory]
    [InlineData(ComponentType.Ram)]
    [InlineData(ComponentType.Gpu)]
    [InlineData(ComponentType.Storage)]
    [InlineData(ComponentType.Psu)]
    [InlineData(ComponentType.Case)]
    [InlineData(ComponentType.Accessory)]
    public void Validator_Socket_Fails_ForInapplicableTypes(ComponentType type)
    {
        var options = new ProductFilterOptions(ComponentType: type, Socket: CpuSocket.LGA1700);
        var result = _validator.TestValidate(options);
        result.ShouldHaveValidationErrorFor(x => x.Socket);
    }

    [Theory]
    [InlineData(ComponentType.Gpu)]
    [InlineData(ComponentType.Storage)]
    [InlineData(ComponentType.Psu)]
    [InlineData(ComponentType.Case)]
    [InlineData(ComponentType.Cooler)]
    [InlineData(ComponentType.Accessory)]
    public void Validator_MemoryType_Fails_ForInapplicableTypes(ComponentType type)
    {
        var options = new ProductFilterOptions(ComponentType: type, MemoryType: RamType.DDR4);
        var result = _validator.TestValidate(options);
        result.ShouldHaveValidationErrorFor(x => x.MemoryType);
    }

    [Fact]
    public void Validator_RamFormFactor_Fails_ForCpu()
    {
        var options = new ProductFilterOptions(ComponentType: ComponentType.Cpu, RamFormFactor: RamFormFactor.DIMM);
        var result = _validator.TestValidate(options);
        result.ShouldHaveValidationErrorFor(x => x.RamFormFactor);
    }

    [Fact]
    public void Validator_StorageType_Fails_ForCpu()
    {
        var options = new ProductFilterOptions(ComponentType: ComponentType.Cpu, StorageType: StorageType.NVMe_M2);
        var result = _validator.TestValidate(options);
        result.ShouldHaveValidationErrorFor(x => x.StorageType);
    }

    [Fact]
    public void Validator_PsuEfficiency_Fails_ForGpu()
    {
        var options = new ProductFilterOptions(ComponentType: ComponentType.Gpu, PsuEfficiency: PsuEfficiency.Plus80Gold);
        var result = _validator.TestValidate(options);
        result.ShouldHaveValidationErrorFor(x => x.PsuEfficiency);
    }

    [Fact]
    public void Validator_CoolerType_Fails_ForCase()
    {
        var options = new ProductFilterOptions(ComponentType: ComponentType.Case, CoolerType: CoolerType.Air);
        var result = _validator.TestValidate(options);
        result.ShouldHaveValidationErrorFor(x => x.CoolerType);
    }

    [Fact]
    public void Validator_HasIntegratedGpu_Fails_ForGpu()
    {
        var options = new ProductFilterOptions(ComponentType: ComponentType.Gpu, HasIntegratedGpu: true);
        var result = _validator.TestValidate(options);
        result.ShouldHaveValidationErrorFor(x => x.HasIntegratedGpu);
    }

    [Fact]
    public void Validator_HasWifi_Fails_ForRam()
    {
        var options = new ProductFilterOptions(ComponentType: ComponentType.Ram, HasWifi: true);
        var result = _validator.TestValidate(options);
        result.ShouldHaveValidationErrorFor(x => x.HasWifi);
    }

    [Fact]
    public void Validator_HasRgb_Fails_ForPsu()
    {
        var options = new ProductFilterOptions(ComponentType: ComponentType.Psu, HasRgb: true);
        var result = _validator.TestValidate(options);
        result.ShouldHaveValidationErrorFor(x => x.HasRgb);
    }

    [Fact]
    public void Validator_Has12VHPWR_Fails_ForCooler()
    {
        var options = new ProductFilterOptions(ComponentType: ComponentType.Cooler, Has12VHPWR: true);
        var result = _validator.TestValidate(options);
        result.ShouldHaveValidationErrorFor(x => x.Has12VHPWR);
    }

    [Fact]
    public void Validator_Requires12VHPWR_Fails_ForPsu()
    {
        var options = new ProductFilterOptions(ComponentType: ComponentType.Psu, Requires12VHPWR: true);
        var result = _validator.TestValidate(options);
        result.ShouldHaveValidationErrorFor(x => x.Requires12VHPWR);
    }

    #endregion

    #region Generic Filters Independent of Technical Filters

    [Fact]
    public void Validator_GenericFiltersWithoutComponentType_Passes()
    {
        var options = new ProductFilterOptions(
            PageIndex: 2,
            PageSize: 10,
            SearchTerm: "Intel Core",
            Brand: "Intel",
            MinPrice: 1000000,
            MaxPrice: 5000000,
            InStock: true,
            IsActive: true,
            SortBy: ProductSortOption.PriceLowToHigh);

        var result = _validator.TestValidate(options);
        result.ShouldNotHaveAnyValidationErrors();
    }

    #endregion
}
