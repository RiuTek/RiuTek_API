using FluentAssertions;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;
using Xunit;

namespace RiuTek.Application.Test.Domain;

public class PCBuildDomainTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_WhenNameIsInvalid_ShouldThrowArgumentException(string? name)
    {
        var act = () => new PCBuild(name!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WhenNameExceeds200Chars_ShouldThrowArgumentException()
    {
        var longName = new string('A', 201);

        var act = () => new PCBuild(longName);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WhenNameHasSurroundingWhitespace_ShouldTrim()
    {
        var build = new PCBuild("  Gaming PC 2026  ");

        build.Name.Should().Be("Gaming PC 2026");
        build.Status.Should().Be(PCBuildStatus.Saved);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  Build for 4K video editing  ", "Build for 4K video editing")]
    public void Constructor_Description_ShouldNormalizeProperly(string? input, string? expected)
    {
        var build = new PCBuild("Workstation", description: input);

        build.Description.Should().Be(expected);
    }

    [Fact]
    public void Constructor_WhenDescriptionExceeds1000Chars_ShouldThrowArgumentException()
    {
        var longDesc = new string('D', 1001);

        var act = () => new PCBuild("Test Build", description: longDesc);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WhenUserIdIsEmptyGuid_ShouldThrowArgumentException()
    {
        var act = () => new PCBuild("Test Build", userId: Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PCBuildItem_WhenParametersInvalid_ShouldThrow()
    {
        var buildId = Guid.NewGuid();
        var prodId = Guid.NewGuid();

        // Empty build ID
        var act1 = () => new PCBuildItem(Guid.Empty, prodId, ComponentType.Cpu, 100m, 1);
        act1.Should().Throw<ArgumentException>();

        // Empty product ID
        var act2 = () => new PCBuildItem(buildId, Guid.Empty, ComponentType.Cpu, 100m, 1);
        act2.Should().Throw<ArgumentException>();

        // Zero or negative quantity
        var act3 = () => new PCBuildItem(buildId, prodId, ComponentType.Cpu, 100m, 0);
        act3.Should().Throw<ArgumentOutOfRangeException>();

        var act4 = () => new PCBuildItem(buildId, prodId, ComponentType.Cpu, 100m, -1);
        act4.Should().Throw<ArgumentOutOfRangeException>();

        // Negative unit price
        var act5 = () => new PCBuildItem(buildId, prodId, ComponentType.Cpu, -1m, 1);
        act5.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void PCBuildItem_UpdateQuantityAndPrice_ShouldValidate()
    {
        var item = new PCBuildItem(Guid.NewGuid(), Guid.NewGuid(), ComponentType.Ram, 50m, 1);

        item.UpdateQuantity(4);
        item.Quantity.Should().Be(4);
        item.UpdatedAt.Should().NotBeNull();

        var actQty = () => item.UpdateQuantity(0);
        actQty.Should().Throw<ArgumentOutOfRangeException>();

        item.UpdateUnitPrice(65.5m);
        item.UnitPrice.Should().Be(65.5m);

        var actPrice = () => item.UpdateUnitPrice(-5m);
        actPrice.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AddItem_And_ReplaceItems_ShouldRecalculateTotals()
    {
        var build = new PCBuild("Gaming Rig");
        var item1 = new PCBuildItem(build.Id, Guid.NewGuid(), ComponentType.Cpu, 5_000_000m, 1);
        var item2 = new PCBuildItem(build.Id, Guid.NewGuid(), ComponentType.Ram, 1_000_000m, 2);

        build.AddItem(item1);
        build.TotalPrice.Should().Be(5_000_000m);
        build.Items.Should().HaveCount(1);

        build.AddItem(item2);
        build.TotalPrice.Should().Be(7_000_000m);
        build.Items.Should().HaveCount(2);

        var item3 = new PCBuildItem(build.Id, Guid.NewGuid(), ComponentType.Gpu, 15_000_000m, 1);
        build.ReplaceItems([item3]);

        build.Items.Should().HaveCount(1);
        build.TotalPrice.Should().Be(15_000_000m);
    }

    [Fact]
    public void RecalculateTotals_WhenOverflow_ShouldThrowOverflowException()
    {
        var build = new PCBuild("Overflow Build");
        var item1 = new PCBuildItem(build.Id, Guid.NewGuid(), ComponentType.Gpu, decimal.MaxValue, 1);
        var item2 = new PCBuildItem(build.Id, Guid.NewGuid(), ComponentType.Gpu, 10m, 1);

        build.AddItem(item1);

        var act = () => build.AddItem(item2);
        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void ApplyCompatibilitySnapshot_ShouldUpdatePropertiesAndTimestamp()
    {
        var build = new PCBuild("Photo Editing PC");
        build.UpdatedAt.Should().BeNull();

        build.ApplyCompatibilitySnapshot(false, 650, ["[Error] PSU wattage insufficient."]);

        build.IsCompatible.Should().BeFalse();
        build.EstimatedWattage.Should().Be(650);
        build.CompatibilityNotes.Should().ContainSingle().Which.Should().Contain("PSU wattage");
        build.UpdatedAt.Should().NotBeNull();
    }
}
