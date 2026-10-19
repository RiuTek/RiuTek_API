using FluentAssertions;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.PCBuilds.Common;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using Xunit;

namespace RiuTek.Application.Test.Features.PCBuilds;

public class PCBuildValidationHelperTests
{
    private static ComponentSpecification CreateDefaultSpec(ComponentType type) => type switch
    {
        ComponentType.Cpu => new CpuSpecification { Socket = CpuSocket.LGA1700, CoreCount = 8, ThreadCount = 16, TdpWattage = 65, SupportedMemoryType = RamType.DDR4 },
        ComponentType.Motherboard => new MotherboardSpecification { Socket = CpuSocket.LGA1700, MemoryType = RamType.DDR4, MemorySlots = 4, MaxMemoryCapacityGb = 128, FormFactor = MotherboardFormFactor.ATX },
        ComponentType.Ram => new RamSpecification { MemoryType = RamType.DDR4, ModuleCount = 1, CapacityGb = 16 },
        ComponentType.Gpu => new GpuSpecification { Chipset = "RTX 4070", VramGb = 12, TdpWattage = 200, LengthMm = 240 },
        ComponentType.Storage => new StorageSpecification { StorageType = StorageType.NVMe_M2, CapacityGb = 1000 },
        ComponentType.Psu => new PsuSpecification { Wattage = 750, Efficiency = PsuEfficiency.Plus80Gold, FormFactor = PsuFormFactor.ATX },
        ComponentType.Case => new CaseSpecification { SupportedFormFactors = [MotherboardFormFactor.ATX], MaxGpuLengthMm = 350, MaxCpuCoolerHeightMm = 165 },
        ComponentType.Cooler => new CoolerSpecification { CoolerType = CoolerType.Air, SupportedSockets = [CpuSocket.LGA1700], HeightMm = 155, MaxTdpRating = 180 },
        _ => new StorageSpecification { StorageType = StorageType.SATA_SSD, CapacityGb = 500 }
    };

    private static Product CreateProduct(TestApplicationDbContext context, ComponentType type, decimal price = 100_000m)
    {
        var category = new Category(type.ToString(), type.ToString().ToLower(), type, "desc");
        context.Categories.Add(category);

        var product = new Product(
            category.Id,
            $"Test {type}",
            $"slug-{Guid.NewGuid():N}",
            $"SKU_{Guid.NewGuid():N}",
            "Brand",
            price,
            10,
            "img.jpg",
            type,
            CreateDefaultSpec(type));

        context.Products.Add(product);
        return product;
    }

    [Fact]
    public async Task ValidateAndResolveItemsAsync_WhenEmpty_ShouldReturnEmptyItemsError()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();

        var result = await PCBuildValidationHelper.ValidateAndResolveItemsAsync(
            [],
            context,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("PCBuild.EmptyItems");
    }

    [Fact]
    public async Task ValidateAndResolveItemsAsync_WhenExceeds30Items_ShouldReturnTooManyItemsError()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();

        var items = Enumerable.Range(1, 31)
            .Select(_ => new PCBuildItemInputDto(Guid.NewGuid(), 1))
            .ToList();

        var result = await PCBuildValidationHelper.ValidateAndResolveItemsAsync(
            items,
            context,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("PCBuild.TooManyItems");
    }

    [Fact]
    public async Task ValidateAndResolveItemsAsync_WhenDuplicateProductId_ShouldReturnDuplicateProductError()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var prodId = Guid.NewGuid();

        var items = new List<PCBuildItemInputDto>
        {
            new(prodId, 1),
            new(prodId, 2)
        };

        var result = await PCBuildValidationHelper.ValidateAndResolveItemsAsync(
            items,
            context,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("PCBuild.DuplicateProduct");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(21)]
    public async Task ValidateAndResolveItemsAsync_WhenInvalidQuantity_ShouldReturnInvalidQuantityError(int qty)
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();

        var items = new List<PCBuildItemInputDto>
        {
            new(Guid.NewGuid(), qty)
        };

        var result = await PCBuildValidationHelper.ValidateAndResolveItemsAsync(
            items,
            context,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("PCBuild.InvalidQuantity");
    }

    [Fact]
    public async Task ValidateAndResolveItemsAsync_WhenProductNotFound_ShouldReturnProductNotFoundError()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var missingId = Guid.NewGuid();

        var items = new List<PCBuildItemInputDto>
        {
            new(missingId, 1)
        };

        var result = await PCBuildValidationHelper.ValidateAndResolveItemsAsync(
            items,
            context,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("PCBuild.ProductNotFound");
    }

    [Fact]
    public async Task ValidateAndResolveItemsAsync_WhenMultipleSingleChoiceComponents_ShouldReturnError()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var cpu1 = CreateProduct(context, ComponentType.Cpu);
        var cpu2 = CreateProduct(context, ComponentType.Cpu);
        await context.SaveChangesAsync();

        var items = new List<PCBuildItemInputDto>
        {
            new(cpu1.Id, 1),
            new(cpu2.Id, 1)
        };

        var result = await PCBuildValidationHelper.ValidateAndResolveItemsAsync(
            items,
            context,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("PCBuild.MultipleSingleChoiceComponents");
    }

    [Fact]
    public async Task ValidateAndResolveItemsAsync_WhenSingleChoiceQuantityNotOne_ShouldReturnError()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var gpu = CreateProduct(context, ComponentType.Gpu);
        await context.SaveChangesAsync();

        var items = new List<PCBuildItemInputDto>
        {
            new(gpu.Id, 2)
        };

        var result = await PCBuildValidationHelper.ValidateAndResolveItemsAsync(
            items,
            context,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("PCBuild.InvalidSingleChoiceQuantity");
    }

    [Fact]
    public async Task ValidateAndResolveItemsAsync_WhenValidMultiChoice_ShouldSucceedAndSortDeterministically()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var ram = CreateProduct(context, ComponentType.Ram);
        var storage = CreateProduct(context, ComponentType.Storage);
        var cpu = CreateProduct(context, ComponentType.Cpu);
        await context.SaveChangesAsync();

        // Pass in random order: Storage, RAM, CPU
        var items = new List<PCBuildItemInputDto>
        {
            new(storage.Id, 2),
            new(ram.Id, 4),
            new(cpu.Id, 1)
        };

        var result = await PCBuildValidationHelper.ValidateAndResolveItemsAsync(
            items,
            context,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var resolved = result.Value;
        resolved.Should().HaveCount(3);

        // Deterministic sort: Cpu (1) -> Ram (4) -> Storage (5)
        resolved[0].Product.ComponentType.Should().Be(ComponentType.Cpu);
        resolved[1].Product.ComponentType.Should().Be(ComponentType.Ram);
        resolved[2].Product.ComponentType.Should().Be(ComponentType.Storage);
    }
}
