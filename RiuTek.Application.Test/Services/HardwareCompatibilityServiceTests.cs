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
        // Arrange: User only selected CPU (with integrated GPU) and Motherboard with matching socket
        var cpu = new CpuSpecification
        {
            Socket = CpuSocket.AM5,
            TdpWattage = 65,
            SupportedMemoryType = RamType.DDR5,
            HasIntegratedGpu = true
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
    public void ValidateSpecifications_WhenFullBuildWithoutIgpuOrDiscreteGpu_ShouldReturnComp12WarningAndIncomplete_UntilDiscreteGpuAdded()
    {
        var cpu = new CpuSpecification
        {
            Socket = CpuSocket.LGA1700,
            CoreCount = 14,
            TdpWattage = 125,
            SupportedMemoryType = RamType.DDR5,
            HasIntegratedGpu = false
        };

        var mobo = new MotherboardSpecification
        {
            Socket = CpuSocket.LGA1700,
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

        var storage = new StorageSpecification
        {
            StorageType = StorageType.NVMe_M2,
            CapacityGb = 1000
        };

        var psu = new PsuSpecification
        {
            Wattage = 750,
            Efficiency = PsuEfficiency.Plus80Gold,
            LengthMm = 150
        };

        var pcCase = new CaseSpecification
        {
            SupportedFormFactors = [MotherboardFormFactor.ATX],
            MaxGpuLengthMm = 360,
            MaxCpuCoolerHeightMm = 165,
            MaxPsuLengthMm = 180
        };

        // Act 1: No discrete GPU
        var resultWithoutGpu = _service.ValidateSpecifications([cpu, mobo, ram, storage, psu, pcCase]);

        // Assert 1: COMP-12 Warning, MissingComponents has "Card đồ họa rời (GPU)", IsCompleteSystem == false, IsCompatible == true
        resultWithoutGpu.IsCompatible.Should().BeTrue();
        resultWithoutGpu.IsCompleteSystem.Should().BeFalse();
        resultWithoutGpu.MissingComponents.Should().ContainSingle().Which.Should().Be("Card đồ họa rời (GPU)");
        resultWithoutGpu.Issues.Should().ContainSingle(i => i.RuleId == "COMP-12" && i.Severity == "Warning");

        // Act 2: Add discrete GPU
        var gpu = new GpuSpecification
        {
            Chipset = "RTX 4060",
            LengthMm = 240,
            TdpWattage = 115
        };
        var resultWithGpu = _service.ValidateSpecifications([cpu, mobo, ram, storage, psu, pcCase, gpu]);

        // Assert 2: No COMP-12, MissingComponents empty, IsCompleteSystem == true
        resultWithGpu.IsCompatible.Should().BeTrue();
        resultWithGpu.IsCompleteSystem.Should().BeTrue();
        resultWithGpu.MissingComponents.Should().BeEmpty();
        resultWithGpu.Issues.Should().NotContain(i => i.RuleId == "COMP-12");
    }

    [Fact]
    public void ValidateSpecifications_WhenCpuHasIntegratedGpuAndNoDiscreteGpu_ShouldNotReturnComp12()
    {
        var cpu = new CpuSpecification
        {
            Socket = CpuSocket.AM5,
            TdpWattage = 65,
            SupportedMemoryType = RamType.DDR5,
            HasIntegratedGpu = true
        };
        var mobo = new MotherboardSpecification
        {
            Socket = CpuSocket.AM5,
            FormFactor = MotherboardFormFactor.ATX,
            MemoryType = RamType.DDR5,
            MemorySlots = 4,
            MaxMemoryCapacityGb = 128
        };
        var ram = new RamSpecification { MemoryType = RamType.DDR5, CapacityGb = 32, ModuleCount = 2 };
        var storage = new StorageSpecification { StorageType = StorageType.NVMe_M2, CapacityGb = 1000 };
        var psu = new PsuSpecification { Wattage = 650 };
        var pcCase = new CaseSpecification { SupportedFormFactors = [MotherboardFormFactor.ATX] };

        var result = _service.ValidateSpecifications([cpu, mobo, ram, storage, psu, pcCase]);

        result.IsCompatible.Should().BeTrue();
        result.IsCompleteSystem.Should().BeTrue();
        result.MissingComponents.Should().BeEmpty();
        result.Issues.Should().NotContain(i => i.RuleId == "COMP-12");
    }

    [Fact]
    public void ValidateSpecifications_WhenNoCpuSelected_ShouldReportMissingCpuAndNotComp12()
    {
        var mobo = new MotherboardSpecification
        {
            Socket = CpuSocket.AM5,
            FormFactor = MotherboardFormFactor.ATX,
            MemoryType = RamType.DDR5,
            MemorySlots = 4,
            MaxMemoryCapacityGb = 128
        };

        var result = _service.ValidateSpecifications([mobo]);

        result.IsCompatible.Should().BeTrue();
        result.IsCompleteSystem.Should().BeFalse();
        result.MissingComponents.Should().Contain("CPU");
        result.MissingComponents.Should().NotContain("Card đồ họa rời (GPU)");
        result.Issues.Should().NotContain(i => i.RuleId == "COMP-12");
    }

    [Fact]
    public void ValidateSpecifications_WhenOnlyCpuWithoutIgpuSelected_ShouldReturnPartialWithComp12WarningAndNoError()
    {
        var cpu = new CpuSpecification
        {
            Socket = CpuSocket.LGA1700,
            TdpWattage = 65,
            SupportedMemoryType = RamType.DDR5,
            HasIntegratedGpu = false
        };

        var result = _service.ValidateSpecifications([cpu]);

        result.IsCompatible.Should().BeTrue();
        result.IsCompleteSystem.Should().BeFalse();
        result.MissingComponents.Should().Contain("Card đồ họa rời (GPU)");
        result.Issues.Should().ContainSingle(i => i.RuleId == "COMP-12" && i.Severity == "Warning");
        result.Issues.Should().NotContain(i => i.Severity == "Error");
    }

    [Theory]
    [InlineData(181, 180, true)]
    [InlineData(180, 180, false)]
    [InlineData(179, 180, false)]
    [InlineData(0, 180, false)]
    [InlineData(190, 0, false)]
    public void ValidateSpecifications_PsuLengthVsCaseClearance_BoundaryChecks(int psuLengthMm, int caseMaxPsuLengthMm, bool expectError)
    {
        var psu = new PsuSpecification { Wattage = 750, LengthMm = psuLengthMm };
        var pcCase = new CaseSpecification { MaxPsuLengthMm = caseMaxPsuLengthMm };

        var result = _service.ValidateSpecifications([psu, pcCase]);

        if (expectError)
        {
            result.IsCompatible.Should().BeFalse();
            var issue = result.Issues.Should().ContainSingle(i => i.RuleId == "COMP-13" && i.Severity == "Error").Subject;
            issue.Message.Should().Contain($"{psuLengthMm} mm").And.Contain($"{caseMaxPsuLengthMm} mm");
        }
        else
        {
            result.IsCompatible.Should().BeTrue();
            result.Issues.Should().NotContain(i => i.RuleId == "COMP-13");
        }
    }

    [Fact]
    public void ValidateSpecifications_WhenPsuOrCaseMissing_ShouldNotReturnComp13()
    {
        var psuOnly = _service.ValidateSpecifications([new PsuSpecification { Wattage = 750, LengthMm = 200 }]);
        psuOnly.Issues.Should().NotContain(i => i.RuleId == "COMP-13");

        var caseOnly = _service.ValidateSpecifications([new CaseSpecification { MaxPsuLengthMm = 160 }]);
        caseOnly.Issues.Should().NotContain(i => i.RuleId == "COMP-13");
    }

    [Fact]
    public void ValidateSpecifications_DeterministicWattageAndGpuRecommendedPsu_ShouldEvaluateComp10Correctly()
    {
        // Deterministic fixture:
        // CPU 65W + GPU 150W + Mobo 50W + 1 kit of 2 RAM modules (10W) + 1 SSD (5W) + system overhead (25W)
        // => EstimatedWattage = 305W. Formula recommended = RoundUp(305 * 1.25 = 381.25) = 450W.
        // GPU RecommendedPsuWattage = 650W => RecommendedPsuWattage = max(450, 650) = 650W.
        var cpu = new CpuSpecification
        {
            Socket = CpuSocket.AM5,
            TdpWattage = 65,
            SupportedMemoryType = RamType.DDR5,
            HasIntegratedGpu = false
        };
        var gpu = new GpuSpecification
        {
            Chipset = "RTX 4070",
            TdpWattage = 150,
            RecommendedPsuWattage = 650,
            LengthMm = 240
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
            ModuleCount = 2,
            CapacityGb = 32
        };
        var storage = new StorageSpecification
        {
            StorageType = StorageType.NVMe_M2,
            CapacityGb = 1000
        };

        // 1. PSU 300W (< EstimatedWattage 305W) -> COMP-10 Error
        var res300 = _service.ValidateSpecifications([cpu, gpu, mobo, ram, storage, new PsuSpecification { Wattage = 300 }]);
        res300.EstimatedWattage.Should().Be(305);
        res300.RecommendedPsuWattage.Should().Be(650);
        res300.IsCompatible.Should().BeFalse();
        res300.Issues.Should().ContainSingle(i => i.RuleId == "COMP-10" && i.Severity == "Error");

        // 2. PSU 550W (>= 305W but < RecommendedPsuWattage 650W) -> COMP-10 Warning
        var res550 = _service.ValidateSpecifications([cpu, gpu, mobo, ram, storage, new PsuSpecification { Wattage = 550 }]);
        res550.EstimatedWattage.Should().Be(305);
        res550.RecommendedPsuWattage.Should().Be(650);
        res550.IsCompatible.Should().BeTrue();
        var warning = res550.Issues.Should().ContainSingle(i => i.RuleId == "COMP-10" && i.Severity == "Warning").Subject;
        warning.Message.Should().Contain("550 W").And.Contain("305 W").And.Contain("650 W");

        // 3. PSU 650W (>= RecommendedPsuWattage 650W) -> No COMP-10
        var res650 = _service.ValidateSpecifications([cpu, gpu, mobo, ram, storage, new PsuSpecification { Wattage = 650 }]);
        res650.EstimatedWattage.Should().Be(305);
        res650.RecommendedPsuWattage.Should().Be(650);
        res650.IsCompatible.Should().BeTrue();
        res650.Issues.Should().NotContain(i => i.RuleId == "COMP-10");
    }

    [Fact]
    public void ValidateSpecifications_WhenGpuRecommendedPsuWattageLowerOrZeroOrNoGpu_ShouldNeverLowerFormulaRecommendation()
    {
        var cpu = new CpuSpecification { TdpWattage = 65, HasIntegratedGpu = true };
        var mobo = new MotherboardSpecification { MemorySlots = 4, MaxMemoryCapacityGb = 128 };
        var ram = new RamSpecification { ModuleCount = 2, CapacityGb = 32 };
        var storage = new StorageSpecification { CapacityGb = 1000 };

        // With GPU 150W, EstimatedWattage = 305W -> formula = 450W.
        // GPU RecommendedPsuWattage = 300W (< 450W) -> must stay 450W
        var gpuLow = new GpuSpecification { TdpWattage = 150, RecommendedPsuWattage = 300 };
        var resLow = _service.ValidateSpecifications([cpu, gpuLow, mobo, ram, storage]);
        resLow.EstimatedWattage.Should().Be(305);
        resLow.RecommendedPsuWattage.Should().Be(450);

        // GPU RecommendedPsuWattage = 0 -> fallback to formula 450W
        var gpuZero = new GpuSpecification { TdpWattage = 150, RecommendedPsuWattage = 0 };
        var resZero = _service.ValidateSpecifications([cpu, gpuZero, mobo, ram, storage]);
        resZero.EstimatedWattage.Should().Be(305);
        resZero.RecommendedPsuWattage.Should().Be(450);

        // No GPU -> EstimatedWattage = 65 + 50 + 10 + 5 + 25 = 155W -> 155 * 1.25 = 193.75 -> 450W minimum
        var resNoGpu = _service.ValidateSpecifications([cpu, mobo, ram, storage]);
        resNoGpu.EstimatedWattage.Should().Be(155);
        resNoGpu.RecommendedPsuWattage.Should().Be(450);
    }

    [Fact]
    public void ValidateSpecifications_RegressionBoundaries_Comp05_Comp06_Comp07_Comp08_Comp11()
    {
        // COMP-05: Motherboard form factor not supported by Case
        var eatxMobo = new MotherboardSpecification { FormFactor = MotherboardFormFactor.EATX, MemorySlots = 4, MaxMemoryCapacityGb = 128 };
        var atxCase = new CaseSpecification { SupportedFormFactors = [MotherboardFormFactor.ATX, MotherboardFormFactor.MicroATX] };
        var comp05Res = _service.ValidateSpecifications([eatxMobo, atxCase]);
        comp05Res.Issues.Should().ContainSingle(i => i.RuleId == "COMP-05" && i.Severity == "Error");

        // COMP-06 boundary: GPU LengthMm == MaxGpuLengthMm (ok) vs > MaxGpuLengthMm (error)
        var gpuExact = new GpuSpecification { LengthMm = 320, TdpWattage = 200 };
        var case320 = new CaseSpecification { MaxGpuLengthMm = 320 };
        _service.ValidateSpecifications([gpuExact, case320]).Issues.Should().NotContain(i => i.RuleId == "COMP-06");

        var gpuOver = new GpuSpecification { LengthMm = 321, TdpWattage = 200 };
        _service.ValidateSpecifications([gpuOver, case320]).Issues.Should().ContainSingle(i => i.RuleId == "COMP-06" && i.Severity == "Error");

        // COMP-07: Cooler socket support
        var cpuAm5 = new CpuSpecification { Socket = CpuSocket.AM5, HasIntegratedGpu = true };
        var coolerLgaOnly = new CoolerSpecification { CoolerType = CoolerType.Air, SupportedSockets = [CpuSocket.LGA1700], MaxTdpRating = 200 };
        _service.ValidateSpecifications([cpuAm5, coolerLgaOnly]).Issues.Should().ContainSingle(i => i.RuleId == "COMP-07" && i.Severity == "Error");

        // COMP-08 boundary: Air cooler HeightMm == MaxCpuCoolerHeightMm (ok) vs > MaxCpuCoolerHeightMm (error), and AIO radiator support
        var airExact = new CoolerSpecification { CoolerType = CoolerType.Air, HeightMm = 160 };
        var caseCooler160 = new CaseSpecification { MaxCpuCoolerHeightMm = 160, SupportedRadiatorSizesMm = [240] };
        _service.ValidateSpecifications([airExact, caseCooler160]).Issues.Should().NotContain(i => i.RuleId == "COMP-08");

        var aio360 = new CoolerSpecification { CoolerType = CoolerType.AIO_360, RadiatorSizeMm = 360 };
        _service.ValidateSpecifications([aio360, caseCooler160]).Issues.Should().ContainSingle(i => i.RuleId == "COMP-08" && i.Severity == "Error");

        var aio240 = new CoolerSpecification { CoolerType = CoolerType.AIO_240, RadiatorSizeMm = 240 };
        _service.ValidateSpecifications([aio240, caseCooler160]).Issues.Should().NotContain(i => i.RuleId == "COMP-08");

        // COMP-11: GPU requires 12VHPWR, PSU without native 12VHPWR -> Warning with neutral wording
        var gpu12V = new GpuSpecification { Chipset = "RTX 4080 Super", Requires12VHPWR = true, TdpWattage = 320 };
        var psuNo12V = new PsuSpecification { Wattage = 850, Has12VHPWR = false };
        var comp11Res = _service.ValidateSpecifications([gpu12V, psuNo12V]);
        var comp11Issue = comp11Res.Issues.Should().ContainSingle(i => i.RuleId == "COMP-11" && i.Severity == "Warning").Subject;
        comp11Issue.Message.Should().Contain("12VHPWR").And.Contain("nhà sản xuất");
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
