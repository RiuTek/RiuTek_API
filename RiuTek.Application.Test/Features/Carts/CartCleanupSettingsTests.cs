using FluentAssertions;
using RiuTek.Infrastructure.Services;
using Xunit;

namespace RiuTek.Application.Test.Features.Carts;

public class CartCleanupSettingsTests
{
    [Fact]
    public void DefaultSettings_HaveExpectedValidValues()
    {
        var settings = new CartCleanupSettings();

        settings.Enabled.Should().BeTrue();
        settings.IntervalHours.Should().Be(24);
        settings.BatchSize.Should().Be(200);
        settings.MaxBatchesPerSweep.Should().Be(10);
        CartCleanupSettings.RetentionDays.Should().Be(30);

        var act = () => settings.Validate();
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(169)]
    public void Validate_InvalidIntervalHours_ThrowsInvalidOperationException(int intervalHours)
    {
        var settings = new CartCleanupSettings { IntervalHours = intervalHours };

        var act = () => settings.Validate();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IntervalHours*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1001)]
    public void Validate_InvalidBatchSize_ThrowsInvalidOperationException(int batchSize)
    {
        var settings = new CartCleanupSettings { BatchSize = batchSize };

        var act = () => settings.Validate();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*BatchSize*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(101)]
    public void Validate_InvalidMaxBatchesPerSweep_ThrowsInvalidOperationException(int maxBatches)
    {
        var settings = new CartCleanupSettings { MaxBatchesPerSweep = maxBatches };

        var act = () => settings.Validate();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MaxBatchesPerSweep*");
    }
}
