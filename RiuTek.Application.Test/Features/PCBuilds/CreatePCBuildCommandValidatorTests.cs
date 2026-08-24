using FluentAssertions;
using RiuTek.Application.Features.PCBuilds.Commands;

namespace RiuTek.Application.Test.Features.PCBuilds;

public class CreatePCBuildCommandValidatorTests
{
    private readonly CreatePCBuildCommandValidator _validator = new();

    [Fact]
    public void Validate_WhenNameIsEmpty_ShouldHaveValidationError()
    {
        // Arrange
        var command = new CreatePCBuildCommand(
            Name: "",
            Description: null,
            UserId: null,
            Items: [new PCBuildItemRequest(Guid.NewGuid(), 1)]
        );

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validate_WhenItemsListIsEmpty_ShouldHaveValidationError()
    {
        // Arrange
        var command = new CreatePCBuildCommand(
            Name: "Gaming PC 2026",
            Description: null,
            UserId: null,
            Items: []
        );

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Items");
    }

    [Fact]
    public void Validate_WhenItemQuantityIsZero_ShouldHaveValidationError()
    {
        // Arrange
        var command = new CreatePCBuildCommand(
            Name: "Gaming PC 2026",
            Description: null,
            UserId: null,
            Items: [new PCBuildItemRequest(Guid.NewGuid(), 0)]
        );

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("Quantity"));
    }

    [Fact]
    public void Validate_WhenCommandIsValid_ShouldPassValidation()
    {
        // Arrange
        var command = new CreatePCBuildCommand(
            Name: "Ryzen 7 + RTX 4070 PC",
            Description: "Cấu hình đồ họa & chơi game 2K",
            UserId: Guid.NewGuid(),
            Items:
            [
                new PCBuildItemRequest(Guid.NewGuid(), 1),
                new PCBuildItemRequest(Guid.NewGuid(), 2)
            ]
        );

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }
}
