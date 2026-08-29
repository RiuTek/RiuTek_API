using FluentAssertions;
using RiuTek.Infrastructure.Security;

namespace RiuTek.Application.Test.Security;

public class JwtSettingsTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_WhenSecretKeyIsMissingOrEmpty_ShouldThrow(string? secretKey)
    {
        var settings = new JwtSettings
        {
            SecretKey = secretKey!,
            ExpiryMinutes = 60
        };

        var action = () => settings.Validate();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*missing or empty*");
    }

    [Theory]
    [InlineData("YOUR_JWT_SECRET_KEY_MIN_32_CHARS_LONG_DO_NOT_COMMIT")]
    [InlineData("CHANGE_ME_TO_A_SECURE_KEY_123456789012345")]
    [InlineData("THIS_IS_A_PLACEHOLDER_KEY_FOR_LOCAL_DEV_12345")]
    public void Validate_WhenSecretKeyIsPlaceholder_ShouldThrow(string placeholderKey)
    {
        var settings = new JwtSettings
        {
            SecretKey = placeholderKey,
            ExpiryMinutes = 60
        };

        var action = () => settings.Validate();

        var exception = action.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain("placeholder text");
        exception.Message.Should().NotContain(placeholderKey);
    }

    [Fact]
    public void Validate_WhenSecretKeyIsTooShort_ShouldThrow()
    {
        var shortKey = "Short_Key_Under_32_Bytes"; // 24 bytes
        var settings = new JwtSettings
        {
            SecretKey = shortKey,
            ExpiryMinutes = 60
        };

        var action = () => settings.Validate();

        var exception = action.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain("too short");
        exception.Message.Should().NotContain(shortKey);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Validate_WhenExpiryMinutesIsZeroOrNegative_ShouldThrow(int expiryMinutes)
    {
        var settings = new JwtSettings
        {
            SecretKey = "Valid_Super_Secret_Key_At_Least_32_Bytes_Long_123456",
            ExpiryMinutes = expiryMinutes
        };

        var action = () => settings.Validate();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*ExpiryMinutes*");
    }

    [Fact]
    public void Validate_WhenConfigurationIsValid_ShouldNotThrow()
    {
        var settings = new JwtSettings
        {
            SecretKey = "Valid_Production_Secret_Key_At_Least_32_Bytes_Long_123456",
            Issuer = "RiuTek.API",
            Audience = "RiuTek.Client",
            ExpiryMinutes = 120
        };

        var action = () => settings.Validate();

        action.Should().NotThrow();
    }
}
