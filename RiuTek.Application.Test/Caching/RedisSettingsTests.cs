using FluentAssertions;
using RiuTek.Infrastructure.Caching;

namespace RiuTek.Application.Test.Caching;

public class RedisSettingsTests
{
    [Fact]
    public void Validate_WhenDisabled_DoesNotRequireConnectionString()
    {
        var settings = new RedisSettings
        {
            Enabled = false,
            ConnectionString = string.Empty,
            DefaultExpirationMinutes = 10,
            ConnectTimeoutMs = 5000,
            SyncTimeoutMs = 3000
        };

        var act = () => settings.Validate();
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_WhenEnabledAndConnectionStringIsMissing_Throws(string? connStr)
    {
        var settings = new RedisSettings
        {
            Enabled = true,
            ConnectionString = connStr!,
            DefaultExpirationMinutes = 10
        };

        var act = () => settings.Validate();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*missing or empty*");
    }

    [Theory]
    [InlineData("YOUR_REDIS_CONNECTION_STRING_SET_VIA_ENV_DO_NOT_COMMIT")]
    [InlineData("CHANGE_ME:6379")]
    [InlineData("THIS_IS_A_PLACEHOLDER_FOR_REDIS")]
    public void Validate_WhenEnabledAndConnectionStringIsPlaceholder_Throws(string placeholder)
    {
        var settings = new RedisSettings
        {
            Enabled = true,
            ConnectionString = placeholder,
            DefaultExpirationMinutes = 10
        };

        var act = () => settings.Validate();
        var ex = act.Should().Throw<InvalidOperationException>().Which;
        ex.Message.Should().Contain("placeholder text");
        ex.Message.Should().NotContain(placeholder);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Validate_WhenDefaultExpirationMinutesIsZeroOrNegative_Throws(int expiry)
    {
        var settings = new RedisSettings
        {
            Enabled = false,
            DefaultExpirationMinutes = expiry
        };

        var act = () => settings.Validate();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DefaultExpirationMinutes*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Validate_WhenConnectTimeoutMsIsZeroOrNegative_Throws(int timeout)
    {
        var settings = new RedisSettings
        {
            Enabled = false,
            ConnectTimeoutMs = timeout
        };

        var act = () => settings.Validate();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectTimeoutMs*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Validate_WhenSyncTimeoutMsIsZeroOrNegative_Throws(int timeout)
    {
        var settings = new RedisSettings
        {
            Enabled = false,
            SyncTimeoutMs = timeout
        };

        var act = () => settings.Validate();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SyncTimeoutMs*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_WhenEnabledAndInstanceNameIsMissingOrEmpty_Throws(string? instanceName)
    {
        var settings = new RedisSettings
        {
            Enabled = true,
            ConnectionString = "localhost:6379",
            InstanceName = instanceName!
        };

        var act = () => settings.Validate();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*InstanceName*");
    }

    [Theory]
    [InlineData("riutek*")]
    [InlineData("riu?tek")]
    [InlineData("riutek[1]:")]
    [InlineData("riutek]:")]
    public void Validate_WhenEnabledAndInstanceNameContainsGlobMetacharacters_Throws(string invalidInstanceName)
    {
        var settings = new RedisSettings
        {
            Enabled = true,
            ConnectionString = "localhost:6379",
            InstanceName = invalidInstanceName
        };

        var act = () => settings.Validate();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*wildcard*");
    }

    [Fact]
    public void Validate_WhenEnabledAndInstanceNameDoesNotEndWithColon_NormalizesWithColon()
    {
        var settings = new RedisSettings
        {
            Enabled = true,
            ConnectionString = "localhost:6379",
            InstanceName = "myprefix"
        };

        settings.Validate();
        settings.InstanceName.Should().Be("myprefix:");
    }

    [Fact]
    public void Validate_WhenEnabledAndValid_Succeeds()
    {
        var settings = new RedisSettings
        {
            Enabled = true,
            ConnectionString = "localhost:6379,abortConnect=false",
            InstanceName = "riutek:",
            DefaultExpirationMinutes = 15,
            ConnectTimeoutMs = 5000,
            SyncTimeoutMs = 3000
        };

        var act = () => settings.Validate();
        act.Should().NotThrow();
    }
}
