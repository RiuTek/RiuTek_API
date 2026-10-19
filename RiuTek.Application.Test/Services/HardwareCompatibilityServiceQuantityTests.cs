using FluentAssertions;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Services;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using Xunit;

namespace RiuTek.Application.Test.Services;

public class HardwareCompatibilityServiceQuantityTests
{
    private readonly HardwareCompatibilityService _service = new();

    [Fact]
    public void ValidateSpecificationSelections_WhenRamQuantityExceedsSlots_ShouldReturnError()
    {
        // Motherboard has 2 slots, max 64GB
        var mobo = new MotherboardSpecification
        {
            Socket = CpuSocket.AM5,
            MemoryType = RamType.DDR5,
            MemorySlots = 2,
            MaxMemoryCapacityGb = 64
        };

        // RAM kit is 2 modules (2x16GB = 32GB)
        var ramKit = new RamSpecification
        {
            MemoryType = RamType.DDR5,
            ModuleCount = 2,
            CapacityGb = 32
        };

        // Quantity 2 kits -> 4 modules, 64GB -> exceeds 2 slots!
        var selections = new List<SpecificationSelection>
        {
            new(mobo, 1),
            new(ramKit, 2)
        };

        var result = _service.ValidateSpecificationSelections(selections);

        result.IsCompatible.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "COMP-03" && i.Severity == "Error");
    }

    [Fact]
    public void ValidateSpecificationSelections_WhenRamQuantityExceedsMaxCapacity_ShouldReturnError()
    {
        // Motherboard has 4 slots, max 64GB
        var mobo = new MotherboardSpecification
        {
            Socket = CpuSocket.AM5,
            MemoryType = RamType.DDR5,
            MemorySlots = 4,
            MaxMemoryCapacityGb = 64
        };

        // RAM kit is 2 modules of 32GB (64GB total per kit)
        var ramKit = new RamSpecification
        {
            MemoryType = RamType.DDR5,
            ModuleCount = 2,
            CapacityGb = 64
        };

        // Quantity 2 kits -> 4 modules (ok), 128GB capacity -> exceeds 64GB max!
        var selections = new List<SpecificationSelection>
        {
            new(mobo, 1),
            new(ramKit, 2)
        };

        var result = _service.ValidateSpecificationSelections(selections);

        result.IsCompatible.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "COMP-04" && i.Severity == "Error");
    }

    [Fact]
    public void ValidateSpecificationSelections_WhenStorageQuantityIncreases_EstimatedWattageShouldScale()
    {
        var hddSpec = new StorageSpecification
        {
            StorageType = StorageType.HDD_3_5,
            CapacityGb = 2000
        };

        // 1 HDD
        var singleSelection = new List<SpecificationSelection>
        {
            new(hddSpec, 1)
        };
        var singleResult = _service.ValidateSpecificationSelections(singleSelection);

        // 3 HDDs
        var tripleSelection = new List<SpecificationSelection>
        {
            new(hddSpec, 3)
        };
        var tripleResult = _service.ValidateSpecificationSelections(tripleSelection);

        // Difference should be 2 HDDs * 10W = 20W
        (tripleResult.EstimatedWattage - singleResult.EstimatedWattage).Should().Be(20);
    }

    [Fact]
    public void ValidateComponentSelections_WithProducts_ShouldCalculateCorrectly()
    {
        var category = new Category("RAM", "ram", ComponentType.Ram, "RAM category");
        var ramSpec = new RamSpecification
        {
            MemoryType = RamType.DDR5,
            ModuleCount = 2,
            CapacityGb = 32
        };

        var product = new Product(
            category.Id,
            "Corsair Vengeance 32GB",
            "corsair-vengeance-32gb",
            "SKU_RAM_1",
            "Corsair",
            3_000_000m,
            10,
            "ram.png",
            ComponentType.Ram,
            ramSpec);

        var selections = new List<ComponentSelection>
        {
            new(product, 2)
        };

        var result = _service.ValidateComponentSelections(selections);

        result.EstimatedWattage.Should().BeGreaterThan(0);
        result.MissingComponents.Should().NotContain("RAM");
    }
}
