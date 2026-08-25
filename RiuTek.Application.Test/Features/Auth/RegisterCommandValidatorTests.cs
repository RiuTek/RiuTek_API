using FluentAssertions;
using RiuTek.Application.Features.Auth.Commands;

namespace RiuTek.Application.Test.Features.Auth;

public class RegisterCommandValidatorTests
{
    private readonly RegisterCommandValidator _validator = new();

    [Theory]
    [InlineData("weak")] // Too short (< 8 chars)
    [InlineData("alllowercase123!")] // Missing uppercase
    [InlineData("ALLUPPERCASE123!")] // Missing lowercase
    [InlineData("NoSpecialChar123")] // Missing special char
    [InlineData("NoDigitsInPassword!")] // Missing digit
    public void Validate_WhenPasswordIsWeak_ShouldFailValidation(string weakPassword)
    {
        // Arrange
        var command = new RegisterCommand(
            FullName: "Nguyen Van A",
            Email: "user@example.com",
            Password: weakPassword,
            PhoneNumber: "0901234567"
        );

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Password");
    }

    [Fact]
    public void Validate_WhenPasswordMeetsAllCriteria_ShouldPassValidation()
    {
        // Arrange
        var command = new RegisterCommand(
            FullName: "Nguyen Van A",
            Email: "user@example.com",
            Password: "SecurePassword123!",
            PhoneNumber: "0901234567"
        );

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }
}
