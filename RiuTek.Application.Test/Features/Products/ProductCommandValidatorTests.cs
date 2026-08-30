using FluentValidation.TestHelper;
using RiuTek.Application.Features.Products.Commands;
using RiuTek.Application.Features.Products.Validation;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Test.Features.Products;

public class ProductCommandValidatorTests
{
    private readonly CreateProductCommandValidator _createValidator = new();
    private readonly UpdateProductCommandValidator _updateValidator = new();
    private readonly ComponentSpecificationValidator _specValidator = new();

    #region Helper Valid Specifications

    public static CpuSpecification CreateValidCpuSpec() => new()
    {
        Socket = CpuSocket.LGA1700,
        CoreCount = 16,
        ThreadCount = 24,
        BaseClockGhz = 3.4,
        BoostClockGhz = 5.4,
        TdpWattage = 125,
        HasIntegratedGpu = true,
        SupportedMemoryType = RamType.DDR5,
        MaxMemorySpeedMhz = 5600
    };

    public static MotherboardSpecification CreateValidMotherboardSpec() => new()
    {
        Socket = CpuSocket.AM5,
        Chipset = "B650",
        FormFactor = MotherboardFormFactor.ATX,
        MemoryType = RamType.DDR5,
        MemorySlots = 4,
        MaxMemoryCapacityGb = 128,
        M2Slots = 3,
        SataPorts = 4,
        PcieSlots = 2,
        HasWifi = true
    };

    public static GpuSpecification CreateValidGpuSpec() => new()
    {
        Chipset = "RTX 4070",
        VramGb = 12,
        VramType = "GDDR6X",
        LengthMm = 300,
        SlotWidth = 2.5,
        TdpWattage = 200,
        RecommendedPsuWattage = 650,
        Requires12VHPWR = true,
        PowerConnectors = "1x 16-pin"
    };

    public static RamSpecification CreateValidRamSpec() => new()
    {
        MemoryType = RamType.DDR5,
        FormFactor = RamFormFactor.DIMM,
        CapacityGb = 32,
        ModuleCount = 2,
        SpeedMhz = 6000,
        CasLatency = 30,
        HasRgb = true
    };

    public static StorageSpecification CreateValidStorageSpec() => new()
    {
        StorageType = StorageType.NVMe_M2,
        Interface = StorageInterface.PCIe_4_0,
        CapacityGb = 1000,
        ReadSpeedMBs = 7000,
        WriteSpeedMBs = 5000,
        FormFactor = "M.2 2280"
    };

    public static PsuSpecification CreateValidPsuSpec() => new()
    {
        Wattage = 850,
        Efficiency = PsuEfficiency.Plus80Gold,
        Modularity = PsuModularity.FullModular,
        FormFactor = PsuFormFactor.ATX,
        Has12VHPWR = true,
        LengthMm = 150
    };

    public static CaseSpecification CreateValidCaseSpec() => new()
    {
        SupportedFormFactors = [MotherboardFormFactor.ATX, MotherboardFormFactor.MicroATX],
        MaxGpuLengthMm = 380,
        MaxCpuCoolerHeightMm = 170,
        MaxPsuLengthMm = 200,
        SupportedRadiatorSizesMm = [240, 280, 360],
        IncludedFans = 3
    };

    public static CoolerSpecification CreateValidAirCoolerSpec() => new()
    {
        CoolerType = CoolerType.Air,
        SupportedSockets = [CpuSocket.LGA1700, CpuSocket.AM5],
        HeightMm = 155,
        MaxTdpRating = 220,
        HasRgb = true
    };

    public static CoolerSpecification CreateValidAioCoolerSpec() => new()
    {
        CoolerType = CoolerType.AIO_360,
        SupportedSockets = [CpuSocket.LGA1700, CpuSocket.AM5],
        RadiatorSizeMm = 360,
        MaxTdpRating = 300,
        HasRgb = true
    };

    public static CoolerSpecification CreateValidCustomLoopCoolerSpec() => new()
    {
        CoolerType = CoolerType.CustomLoop,
        SupportedSockets = [CpuSocket.LGA1700, CpuSocket.AM5],
        HeightMm = 0,
        RadiatorSizeMm = 0,
        MaxTdpRating = 500,
        HasRgb = false
    };

    public static AccessorySpecification CreateValidAccessorySpec() => new()
    {
        Details = "Thermal paste with 14.2 W/mK thermal conductivity"
    };

    #endregion

    #region Create / Update Command Level Validation

    [Fact]
    public void CreateProductCommand_HappyPath_PassesValidation()
    {
        var command = new CreateProductCommand(
            CategoryId: Guid.NewGuid(),
            Name: "Intel Core i7-14700K",
            Sku: "CPU-INT-14700K",
            Brand: "Intel",
            Price: 400,
            OriginalPrice: 450,
            StockQuantity: 15,
            ImageUrl: "https://example.com/cpu.jpg",
            AdditionalImages: ["https://example.com/cpu-box.jpg"],
            ComponentType: ComponentType.Cpu,
            Specifications: CreateValidCpuSpec()
        );

        var result = _createValidator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void UpdateProductCommand_HappyPath_PassesValidation()
    {
        var command = new UpdateProductCommand(
            Id: Guid.NewGuid(),
            CategoryId: Guid.NewGuid(),
            Name: "Intel Core i7-14700K",
            Sku: "CPU-INT-14700K",
            Brand: "Intel",
            Price: 400,
            OriginalPrice: null,
            StockQuantity: 0,
            IsActive: false,
            ImageUrl: "https://example.com/cpu.jpg",
            AdditionalImages: null,
            ComponentType: ComponentType.Cpu,
            Specifications: CreateValidCpuSpec()
        );

        var result = _updateValidator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void UpdateProductCommand_WhenIdIsEmpty_FailsValidation()
    {
        var command = new UpdateProductCommand(
            Id: Guid.Empty,
            CategoryId: Guid.NewGuid(),
            Name: "Valid Name",
            Sku: "SKU1",
            Brand: "Brand",
            Price: 100,
            OriginalPrice: null,
            StockQuantity: 10,
            IsActive: true,
            ImageUrl: "img.jpg",
            AdditionalImages: null,
            ComponentType: ComponentType.Cpu,
            Specifications: CreateValidCpuSpec()
        );

        _updateValidator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ProductCommand_WhenNameIsEmptyOrWhitespace_FailsValidation(string invalidName)
    {
        var command = new CreateProductCommand(
            Guid.NewGuid(), invalidName, "SKU1", "Brand", 100, null, 10, "img.jpg", null, ComponentType.Cpu, CreateValidCpuSpec());

        _createValidator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void ProductCommand_WhenNameExceedsMaxLength_FailsValidation()
    {
        var command = new CreateProductCommand(
            Guid.NewGuid(), new string('a', 256), "SKU1", "Brand", 100, null, 10, "img.jpg", null, ComponentType.Cpu, CreateValidCpuSpec());

        _createValidator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ProductCommand_WhenSkuIsEmptyOrWhitespace_FailsValidation(string invalidSku)
    {
        var command = new CreateProductCommand(
            Guid.NewGuid(), "Valid Name", invalidSku, "Brand", 100, null, 10, "img.jpg", null, ComponentType.Cpu, CreateValidCpuSpec());

        _createValidator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.Sku);
    }

    [Fact]
    public void ProductCommand_WhenSkuExceedsMaxLength_FailsValidation()
    {
        var command = new CreateProductCommand(
            Guid.NewGuid(), "Valid Name", new string('s', 101), "Brand", 100, null, 10, "img.jpg", null, ComponentType.Cpu, CreateValidCpuSpec());

        _createValidator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.Sku);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ProductCommand_WhenBrandIsEmptyOrWhitespace_FailsValidation(string invalidBrand)
    {
        var command = new CreateProductCommand(
            Guid.NewGuid(), "Valid Name", "SKU1", invalidBrand, 100, null, 10, "img.jpg", null, ComponentType.Cpu, CreateValidCpuSpec());

        _createValidator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.Brand);
    }

    [Fact]
    public void ProductCommand_WhenBrandExceedsMaxLength_FailsValidation()
    {
        var command = new CreateProductCommand(
            Guid.NewGuid(), "Valid Name", "SKU1", new string('b', 101), 100, null, 10, "img.jpg", null, ComponentType.Cpu, CreateValidCpuSpec());

        _createValidator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.Brand);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void ProductCommand_WhenPriceIsZeroOrNegative_FailsValidation(decimal invalidPrice)
    {
        var command = new CreateProductCommand(
            Guid.NewGuid(), "Valid Name", "SKU1", "Brand", invalidPrice, null, 10, "img.jpg", null, ComponentType.Cpu, CreateValidCpuSpec());

        _createValidator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.Price);
    }

    [Fact]
    public void ProductCommand_WhenOriginalPriceIsLessThanPrice_FailsValidation()
    {
        var command = new CreateProductCommand(
            Guid.NewGuid(), "Valid Name", "SKU1", "Brand", 100, 90, 10, "img.jpg", null, ComponentType.Cpu, CreateValidCpuSpec());

        _createValidator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.OriginalPrice);
    }

    [Fact]
    public void ProductCommand_WhenStockQuantityIsNegative_FailsValidation()
    {
        var command = new CreateProductCommand(
            Guid.NewGuid(), "Valid Name", "SKU1", "Brand", 100, null, -1, "img.jpg", null, ComponentType.Cpu, CreateValidCpuSpec());

        _createValidator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.StockQuantity);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ProductCommand_WhenImageUrlIsEmptyOrWhitespace_FailsValidation(string invalidUrl)
    {
        var command = new CreateProductCommand(
            Guid.NewGuid(), "Valid Name", "SKU1", "Brand", 100, null, 10, invalidUrl, null, ComponentType.Cpu, CreateValidCpuSpec());

        _createValidator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.ImageUrl);
    }

    [Fact]
    public void ProductCommand_WhenImageUrlExceedsMaxLength_FailsValidation()
    {
        var command = new CreateProductCommand(
            Guid.NewGuid(), "Valid Name", "SKU1", "Brand", 100, null, 10, new string('u', 1001), null, ComponentType.Cpu, CreateValidCpuSpec());

        _createValidator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.ImageUrl);
    }

    [Fact]
    public void ProductCommand_WhenAdditionalImagesExceeds10Items_FailsValidation()
    {
        var images = Enumerable.Range(1, 11).Select(i => $"img{i}.jpg").ToList();
        var command = new CreateProductCommand(
            Guid.NewGuid(), "Valid Name", "SKU1", "Brand", 100, null, 10, "img.jpg", images, ComponentType.Cpu, CreateValidCpuSpec());

        _createValidator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.AdditionalImages);
    }

    [Fact]
    public void ProductCommand_WhenAdditionalImageItemIsEmptyOrTooLong_FailsValidation()
    {
        var images = new List<string> { "valid.jpg", "", new string('x', 1001) };
        var command = new CreateProductCommand(
            Guid.NewGuid(), "Valid Name", "SKU1", "Brand", 100, null, 10, "img.jpg", images, ComponentType.Cpu, CreateValidCpuSpec());

        _createValidator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.AdditionalImages);
    }

    [Fact]
    public void ProductCommand_WhenComponentTypeIsInvalidEnum_FailsValidation()
    {
        var command = new CreateProductCommand(
            Guid.NewGuid(), "Valid Name", "SKU1", "Brand", 100, null, 10, "img.jpg", null, (ComponentType)999, CreateValidCpuSpec());

        _createValidator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.ComponentType);
    }

    [Fact]
    public void ProductCommand_WhenSpecificationsIsNull_FailsValidation()
    {
        var command = new CreateProductCommand(
            Guid.NewGuid(), "Valid Name", "SKU1", "Brand", 100, null, 10, "img.jpg", null, ComponentType.Cpu, null!);

        _createValidator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.Specifications);
    }

    [Fact]
    public void ProductCommand_WhenSpecificationTypeMismatchesComponentType_FailsValidation()
    {
        var command = new CreateProductCommand(
            Guid.NewGuid(), "Valid Name", "SKU1", "Brand", 100, null, 10, "img.jpg", null, ComponentType.Motherboard, CreateValidCpuSpec());

        _createValidator.TestValidate(command).ShouldHaveValidationErrorFor(x => x);
    }

    #endregion

    #region Component Specification Subtype Validation

    [Fact]
    public void SpecValidator_AllSubtypesValidCases_PassValidation()
    {
        _specValidator.TestValidate(CreateValidCpuSpec()).ShouldNotHaveAnyValidationErrors();
        _specValidator.TestValidate(CreateValidMotherboardSpec()).ShouldNotHaveAnyValidationErrors();
        _specValidator.TestValidate(CreateValidGpuSpec()).ShouldNotHaveAnyValidationErrors();
        _specValidator.TestValidate(CreateValidRamSpec()).ShouldNotHaveAnyValidationErrors();
        _specValidator.TestValidate(CreateValidStorageSpec()).ShouldNotHaveAnyValidationErrors();
        _specValidator.TestValidate(CreateValidPsuSpec()).ShouldNotHaveAnyValidationErrors();
        _specValidator.TestValidate(CreateValidCaseSpec()).ShouldNotHaveAnyValidationErrors();
        _specValidator.TestValidate(CreateValidAirCoolerSpec()).ShouldNotHaveAnyValidationErrors();
        _specValidator.TestValidate(CreateValidAioCoolerSpec()).ShouldNotHaveAnyValidationErrors();
        _specValidator.TestValidate(CreateValidCustomLoopCoolerSpec()).ShouldNotHaveAnyValidationErrors();
        _specValidator.TestValidate(CreateValidAccessorySpec()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void CpuSpec_InvalidRules_FailsValidation()
    {
        var invalid = new CpuSpecification
        {
            Socket = (CpuSocket)999,
            CoreCount = 0,
            ThreadCount = -1,
            BaseClockGhz = 0,
            BoostClockGhz = -1,
            TdpWattage = 0,
            SupportedMemoryType = (RamType)999,
            MaxMemorySpeedMhz = 0
        };

        var result = _specValidator.TestValidate(invalid);
        result.ShouldHaveValidationErrorFor("Socket");
        result.ShouldHaveValidationErrorFor("CoreCount");
        result.ShouldHaveValidationErrorFor("ThreadCount");
        result.ShouldHaveValidationErrorFor("BaseClockGhz");
        result.ShouldHaveValidationErrorFor("BoostClockGhz");
        result.ShouldHaveValidationErrorFor("TdpWattage");
        result.ShouldHaveValidationErrorFor("SupportedMemoryType");
        result.ShouldHaveValidationErrorFor("MaxMemorySpeedMhz");
    }

    [Fact]
    public void CpuSpec_WhenBoostClockLessThanBaseClock_FailsValidation()
    {
        var invalid = CreateValidCpuSpec() with { BaseClockGhz = 4.0, BoostClockGhz = 3.5 };
        _specValidator.TestValidate(invalid).ShouldHaveValidationErrorFor("BoostClockGhz");
    }

    [Fact]
    public void MotherboardSpec_InvalidRules_FailsValidation()
    {
        var invalid = new MotherboardSpecification
        {
            Socket = (CpuSocket)999,
            Chipset = "   ",
            FormFactor = (MotherboardFormFactor)999,
            MemoryType = (RamType)999,
            MemorySlots = 0,
            MaxMemoryCapacityGb = 0,
            M2Slots = -1,
            SataPorts = -1,
            PcieSlots = -1
        };

        var result = _specValidator.TestValidate(invalid);
        result.ShouldHaveValidationErrorFor("Socket");
        result.ShouldHaveValidationErrorFor("Chipset");
        result.ShouldHaveValidationErrorFor("FormFactor");
        result.ShouldHaveValidationErrorFor("MemoryType");
        result.ShouldHaveValidationErrorFor("MemorySlots");
        result.ShouldHaveValidationErrorFor("MaxMemoryCapacityGb");
        result.ShouldHaveValidationErrorFor("M2Slots");
        result.ShouldHaveValidationErrorFor("SataPorts");
        result.ShouldHaveValidationErrorFor("PcieSlots");
    }

    [Fact]
    public void GpuSpec_InvalidRules_FailsValidation()
    {
        var invalid = new GpuSpecification
        {
            Chipset = "",
            VramGb = 0,
            LengthMm = -10,
            SlotWidth = 0,
            TdpWattage = 0,
            RecommendedPsuWattage = -500
        };

        var result = _specValidator.TestValidate(invalid);
        result.ShouldHaveValidationErrorFor("Chipset");
        result.ShouldHaveValidationErrorFor("VramGb");
        result.ShouldHaveValidationErrorFor("LengthMm");
        result.ShouldHaveValidationErrorFor("SlotWidth");
        result.ShouldHaveValidationErrorFor("TdpWattage");
        result.ShouldHaveValidationErrorFor("RecommendedPsuWattage");
    }

    [Fact]
    public void RamSpec_InvalidRules_FailsValidation()
    {
        var invalid = new RamSpecification
        {
            MemoryType = (RamType)999,
            FormFactor = (RamFormFactor)999,
            CapacityGb = 0,
            ModuleCount = 0,
            SpeedMhz = 0,
            CasLatency = 0
        };

        var result = _specValidator.TestValidate(invalid);
        result.ShouldHaveValidationErrorFor("MemoryType");
        result.ShouldHaveValidationErrorFor("FormFactor");
        result.ShouldHaveValidationErrorFor("CapacityGb");
        result.ShouldHaveValidationErrorFor("ModuleCount");
        result.ShouldHaveValidationErrorFor("SpeedMhz");
        result.ShouldHaveValidationErrorFor("CasLatency");
    }

    [Fact]
    public void StorageSpec_InvalidRules_FailsValidation()
    {
        var invalid = new StorageSpecification
        {
            StorageType = (StorageType)999,
            Interface = (StorageInterface)999,
            CapacityGb = 0,
            ReadSpeedMBs = -1,
            WriteSpeedMBs = -1,
            FormFactor = "   "
        };

        var result = _specValidator.TestValidate(invalid);
        result.ShouldHaveValidationErrorFor("StorageType");
        result.ShouldHaveValidationErrorFor("Interface");
        result.ShouldHaveValidationErrorFor("CapacityGb");
        result.ShouldHaveValidationErrorFor("ReadSpeedMBs");
        result.ShouldHaveValidationErrorFor("WriteSpeedMBs");
        result.ShouldHaveValidationErrorFor("FormFactor");
    }

    [Fact]
    public void PsuSpec_InvalidRules_FailsValidation()
    {
        var invalid = new PsuSpecification
        {
            Wattage = 0,
            Efficiency = (PsuEfficiency)999,
            Modularity = (PsuModularity)999,
            FormFactor = (PsuFormFactor)999,
            LengthMm = 0
        };

        var result = _specValidator.TestValidate(invalid);
        result.ShouldHaveValidationErrorFor("Wattage");
        result.ShouldHaveValidationErrorFor("Efficiency");
        result.ShouldHaveValidationErrorFor("Modularity");
        result.ShouldHaveValidationErrorFor("FormFactor");
        result.ShouldHaveValidationErrorFor("LengthMm");
    }

    [Fact]
    public void CaseSpec_InvalidRules_FailsValidation()
    {
        var invalid = new CaseSpecification
        {
            SupportedFormFactors = [],
            MaxGpuLengthMm = 0,
            MaxCpuCoolerHeightMm = 0,
            MaxPsuLengthMm = 0,
            SupportedRadiatorSizesMm = [-240],
            IncludedFans = -1
        };

        var result = _specValidator.TestValidate(invalid);
        result.ShouldHaveValidationErrorFor("SupportedFormFactors");
        result.ShouldHaveValidationErrorFor("MaxGpuLengthMm");
        result.ShouldHaveValidationErrorFor("MaxCpuCoolerHeightMm");
        result.ShouldHaveValidationErrorFor("MaxPsuLengthMm");
        result.ShouldHaveValidationErrorFor("SupportedRadiatorSizesMm");
        result.ShouldHaveValidationErrorFor("IncludedFans");
    }

    [Fact]
    public void CaseSpec_WithDuplicateOrInvalidFormFactor_FailsValidation()
    {
        var duplicate = CreateValidCaseSpec() with
        {
            SupportedFormFactors = [MotherboardFormFactor.ATX, MotherboardFormFactor.ATX]
        };
        _specValidator.TestValidate(duplicate).ShouldHaveValidationErrorFor("SupportedFormFactors");

        var invalidEnum = CreateValidCaseSpec() with
        {
            SupportedFormFactors = [(MotherboardFormFactor)999]
        };
        _specValidator.TestValidate(invalidEnum).ShouldHaveValidationErrorFor("SupportedFormFactors");
    }

    [Fact]
    public void CoolerSpec_InvalidRules_FailsValidation()
    {
        var invalid = new CoolerSpecification
        {
            CoolerType = (CoolerType)999,
            SupportedSockets = [],
            MaxTdpRating = 0
        };

        var result = _specValidator.TestValidate(invalid);
        result.ShouldHaveValidationErrorFor("CoolerType");
        result.ShouldHaveValidationErrorFor("SupportedSockets");
        result.ShouldHaveValidationErrorFor("MaxTdpRating");
    }

    [Fact]
    public void CoolerSpec_WithDuplicateSockets_FailsValidation()
    {
        var duplicate = CreateValidAirCoolerSpec() with
        {
            SupportedSockets = [CpuSocket.AM5, CpuSocket.AM5]
        };
        _specValidator.TestValidate(duplicate).ShouldHaveValidationErrorFor("SupportedSockets");
    }

    [Fact]
    public void CoolerSpec_AirCoolerWithZeroHeight_FailsValidation()
    {
        var invalid = CreateValidAirCoolerSpec() with { HeightMm = 0 };
        _specValidator.TestValidate(invalid).ShouldHaveValidationErrorFor("HeightMm");
    }

    [Fact]
    public void CoolerSpec_AioCoolerWithZeroRadiatorSize_FailsValidation()
    {
        var invalid = CreateValidAioCoolerSpec() with { RadiatorSizeMm = 0 };
        _specValidator.TestValidate(invalid).ShouldHaveValidationErrorFor("RadiatorSizeMm");
    }

    [Fact]
    public void AccessorySpec_InvalidRules_FailsValidation()
    {
        var empty = new AccessorySpecification { Details = "   " };
        _specValidator.TestValidate(empty).ShouldHaveValidationErrorFor("Details");

        var tooLong = new AccessorySpecification { Details = new string('x', 2001) };
        _specValidator.TestValidate(tooLong).ShouldHaveValidationErrorFor("Details");
    }

    #endregion
}
