using FluentAssertions;
using RiuTek.Application.Services;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Test.Services;

public class HardwareCompatibilityServiceTests
{
    private readonly HardwareCompatibilityService _service = new();

    [Fact]
    public void ValidateSpecifications_WhenSocketMismatch_ShouldReturnError()
    {
        // Arrange: Intel LGA1700 CPU + AMD AM5 Motherboard
        var cpu = new CpuSpecification
        {
            Socket = CpuSocket.LGA1700,
            CoreCount = 16,
            ThreadCount = 24,
            TdpWattage = 125,
            SupportedMemoryType = RamType.DDR5
        };

        var mobo = new MotherboardSpecification
        {
            Socket = CpuSocket.AM5,
            Chipset = "B650",
            FormFactor = MotherboardFormFactor.ATX,
            MemoryType = RamType.DDR5,
            MemorySlots = 4,
            MaxMemoryCapacityGb = 128
        };

        // Act
        var result = _service.ValidateSpecifications([cpu, mobo]);

        // Assert
        result.IsCompatible.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "COMP-01" && i.Severity == "Error");
    }

    [Fact]
    public void ValidateSpecifications_WhenRamTypeMismatch_ShouldReturnError()
    {
        // Arrange: DDR4 RAM on DDR5 Motherboard
        var mobo = new MotherboardSpecification
        {
            Socket = CpuSocket.AM5,
            MemoryType = RamType.DDR5,
            MemorySlots = 4,
            MaxMemoryCapacityGb = 128
        };

        var ram = new RamSpecification
        {
            MemoryType = RamType.DDR4,
            CapacityGb = 32,
            ModuleCount = 2
        };

        // Act
        var result = _service.ValidateSpecifications([mobo, ram]);

        // Assert
        result.IsCompatible.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "COMP-02" && i.Severity == "Error");
    }

    [Fact]
    public void ValidateSpecifications_WhenRamModulesExceedSlots_ShouldReturnError()
    {
        // Arrange: 4 RAM modules on Motherboard with 2 slots
        var mobo = new MotherboardSpecification
        {
            Socket = CpuSocket.AM5,
            MemoryType = RamType.DDR5,
            MemorySlots = 2,
            MaxMemoryCapacityGb = 64
        };

        var ram = new RamSpecification
        {
            MemoryType = RamType.DDR5,
            CapacityGb = 64,
            ModuleCount = 4
        };

        // Act
        var result = _service.ValidateSpecifications([mobo, ram]);

        // Assert
        result.IsCompatible.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "COMP-03" && i.Severity == "Error");
    }

    [Fact]
    public void ValidateSpecifications_WhenGpuTooLongForCase_ShouldReturnError()
    {
        // Arrange: GPU 350mm, Case max 300mm
        var gpu = new GpuSpecification
        {
            Chipset = "RTX 4080",
            VramGb = 16,
            LengthMm = 350,
            TdpWattage = 320
        };

        var pcCase = new CaseSpecification
        {
            MaxGpuLengthMm = 300,
            MaxCpuCoolerHeightMm = 160
        };

        // Act
        var result = _service.ValidateSpecifications([gpu, pcCase]);

        // Assert
        result.IsCompatible.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "COMP-06" && i.Severity == "Error");
    }

    [Fact]
    public void ValidateSpecifications_WhenAirCoolerTooTallForCase_ShouldReturnError()
    {
        // Arrange: Cooler height 170mm, Case max cooler height 155mm
        var cooler = new CoolerSpecification
        {
            CoolerType = CoolerType.Air,
            HeightMm = 170,
            MaxTdpRating = 200
        };

        var pcCase = new CaseSpecification
        {
            MaxCpuCoolerHeightMm = 155
        };

        // Act
        var result = _service.ValidateSpecifications([cooler, pcCase]);

        // Assert
        result.IsCompatible.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "COMP-08" && i.Severity == "Error");
    }

    [Fact]
    public void ValidateSpecifications_WhenPsuInsufficient_ShouldReturnError()
    {
        // Arrange: CPU (250W) + GPU (450W) = 700W system load, but PSU is 500W
        var cpu = new CpuSpecification { TdpWattage = 250 };
        var gpu = new GpuSpecification { TdpWattage = 450 };
        var psu = new PsuSpecification { Wattage = 500 };

        // Act
        var result = _service.ValidateSpecifications([cpu, gpu, psu]);

        // Assert
        result.IsCompatible.Should().BeFalse();
        result.EstimatedWattage.Should().BeGreaterThan(700);
        result.Issues.Should().Contain(i => i.RuleId == "COMP-10" && i.Severity == "Error");
    }

    [Fact]
    public void ValidateSpecifications_WhenPartialBuildHasNoConflicts_ShouldReturnCompatible()
    {
        // Arrange: User only selected CPU and Motherboard with matching socket
        var cpu = new CpuSpecification
        {
            Socket = CpuSocket.AM5,
            TdpWattage = 65,
            SupportedMemoryType = RamType.DDR5
        };

        var mobo = new MotherboardSpecification
        {
            Socket = CpuSocket.AM5,
            MemoryType = RamType.DDR5,
            MemorySlots = 4,
            MaxMemoryCapacityGb = 128
        };

        // Act
        var result = _service.ValidateSpecifications([cpu, mobo]);

        // Assert
        result.IsCompatible.Should().BeTrue();
        result.IsCompleteSystem.Should().BeFalse();
        result.MissingComponents.Should().Contain("RAM");
        result.MissingComponents.Should().Contain("Nguồn (PSU)");
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void ValidateSpecifications_WhenFullBuildIs100PercentCompatible_ShouldReturnCompleteAndCompatible()
    {
        // Arrange: Complete full PC build with matching parts
        var cpu = new CpuSpecification
        {
            Socket = CpuSocket.AM5,
            CoreCount = 8,
            TdpWattage = 120,
            SupportedMemoryType = RamType.DDR5
        };

        var mobo = new MotherboardSpecification
        {
            Socket = CpuSocket.AM5,
            FormFactor = MotherboardFormFactor.ATX,
            MemoryType = RamType.DDR5,
            MemorySlots = 4,
            MaxMemoryCapacityGb = 128
        };

        var ram = new RamSpecification
        {
            MemoryType = RamType.DDR5,
            CapacityGb = 32,
            ModuleCount = 2
        };

        var gpu = new GpuSpecification
        {
            Chipset = "RTX 4070",
            LengthMm = 280,
            TdpWattage = 200,
            Requires12VHPWR = true
        };

        var storage = new StorageSpecification
        {
            StorageType = StorageType.NVMe_M2,
            CapacityGb = 1000
        };

        var psu = new PsuSpecification
        {
            Wattage = 750,
            Efficiency = PsuEfficiency.Plus80Gold,
            Has12VHPWR = true
        };

        var pcCase = new CaseSpecification
        {
            SupportedFormFactors = [MotherboardFormFactor.ATX, MotherboardFormFactor.MicroATX],
            MaxGpuLengthMm = 380,
            MaxCpuCoolerHeightMm = 165,
            SupportedRadiatorSizesMm = [240, 360]
        };

        var cooler = new CoolerSpecification
        {
            CoolerType = CoolerType.Air,
            SupportedSockets = [CpuSocket.AM5, CpuSocket.LGA1700],
            HeightMm = 158,
            MaxTdpRating = 220
        };

        // Act
        var result = _service.ValidateSpecifications([cpu, mobo, ram, gpu, storage, psu, pcCase, cooler]);

        // Assert
        result.IsCompatible.Should().BeTrue();
        result.IsCompleteSystem.Should().BeTrue();
        result.MissingComponents.Should().BeEmpty();
        result.Issues.Where(i => i.Severity == "Error").Should().BeEmpty();
    }
}
