using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using RiuTek.API.Contracts;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Auth.Commands;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Security;

namespace RiuTek.Application.Test.Features.Auth;

public class AuthSerializationAndContractTests
{
    [Fact]
    public void AuthResponse_Serialization_ShouldNotContainRefreshToken()
    {
        // Arrange
        var userDto = new UserDto(
            Id: Guid.NewGuid(),
            Email: "test@example.com",
            FullName: "Test User",
            PhoneNumber: "0123456789",
            Role: UserRole.Customer,
            CreatedAt: DateTime.UtcNow
        );

        var response = new AuthResponse(
            AccessToken: "access_token_sample",
            ExpiresInSeconds: 3600,
            User: userDto
        );

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // Act
        var json = JsonSerializer.Serialize(response, options);

        // Assert
        json.Should().NotContainEquivalentOf("refreshToken");
        json.Should().Contain("accessToken");
        json.Should().Contain("expiresInSeconds");
        json.Should().Contain("user");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.TryGetProperty("refreshToken", out _).Should().BeFalse();
        root.TryGetProperty("RefreshToken", out _).Should().BeFalse();
        root.GetProperty("accessToken").GetString().Should().Be("access_token_sample");
        root.GetProperty("expiresInSeconds").GetInt32().Should().Be(3600);
        root.GetProperty("user").GetProperty("email").GetString().Should().Be("test@example.com");
    }

    [Fact]
    public void Sha256RefreshTokenHasher_ShouldProduceDeterministic64CharLowercaseHex()
    {
        // Arrange
        var hasher = new Sha256RefreshTokenHasher();
        const string rawToken = "sample-raw-refresh-token-xyz-123456789";

        // Act
        var hash1 = hasher.HashToken(rawToken);
        var hash2 = hasher.HashToken(rawToken);

        // Assert
        hash1.Should().NotBeNullOrWhiteSpace();
        hash1.Length.Should().Be(64);
        hash1.Should().Be(hash2, "Hashing must be deterministic");
        hash1.Should().MatchRegex("^[0-9a-f]{64}$", "Hash must be 64-char lowercase hexadecimal");

        var differentHash = hasher.HashToken("different-token");
        differentHash.Should().NotBe(hash1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sha256RefreshTokenHasher_ShouldReturnEmptyStringOnNullOrWhitespace(string? invalidToken)
    {
        // Arrange
        var hasher = new Sha256RefreshTokenHasher();

        // Act
        var result = hasher.HashToken(invalidToken!);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void RefreshCookieSettings_DefaultValues_ShouldMatchSecurityContract()
    {
        // Arrange & Act
        var settings = new RefreshCookieSettings();

        // Assert
        settings.CookieName.Should().Be("riutek.refresh_token");
        settings.Path.Should().Be("/api/v1/auth");
        settings.SameSite.Should().Be(SameSiteMode.Strict);
        settings.HttpOnly.Should().BeTrue();
        settings.Secure.Should().BeTrue();
        settings.ExpiryDays.Should().Be(7);

        var validateAct = () => settings.Validate();
        validateAct.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RefreshCookieSettings_Validation_ShouldThrowWhenCookieNameEmpty(string? cookieName)
    {
        var settings = new RefreshCookieSettings { CookieName = cookieName! };
        var act = () => settings.Validate();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CookieName*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative/path")]
    [InlineData("no-leading-slash")]
    public void RefreshCookieSettings_Validation_ShouldThrowWhenPathInvalid(string? path)
    {
        var settings = new RefreshCookieSettings { Path = path! };
        var act = () => settings.Validate();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Path*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-7)]
    public void RefreshCookieSettings_Validation_ShouldThrowWhenExpiryDaysNonPositive(int days)
    {
        var settings = new RefreshCookieSettings { ExpiryDays = days };
        var act = () => settings.Validate();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ExpiryDays*");
    }

    [Fact]
    public async Task LogoutCommandHandler_WhenMatchingTokenExists_ShouldRevokeTokenAndReturnSuccess()
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var hasher = new Sha256RefreshTokenHasher();
        const string rawToken = "valid-raw-refresh-token";
        var hashedToken = hasher.HashToken(rawToken);

        var user = new User(
            email: "logout-user@example.com",
            passwordHash: "hash",
            fullName: "Logout User",
            role: UserRole.Customer
        )
        {
            RefreshToken = hashedToken,
            RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7)
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var handler = new LogoutCommandHandler(context, hasher);

        // Act
        var result = await handler.Handle(new LogoutCommand(rawToken), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        user.RefreshToken.Should().BeNull();
        user.RefreshTokenExpiryTime.Should().BeNull();
    }

    [Fact]
    public async Task LogoutCommandHandler_WhenTokenNotFoundOrEmpty_ShouldReturnSuccessWithoutError()
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var hasher = new Sha256RefreshTokenHasher();
        var handler = new LogoutCommandHandler(context, hasher);

        // Act & Assert 1: Non-existent token
        var resultNotFound = await handler.Handle(new LogoutCommand("non-existent-token"), CancellationToken.None);
        resultNotFound.IsSuccess.Should().BeTrue();

        // Act & Assert 2: Null token
        var resultNull = await handler.Handle(new LogoutCommand(null), CancellationToken.None);
        resultNull.IsSuccess.Should().BeTrue();

        // Act & Assert 3: Empty token
        var resultEmpty = await handler.Handle(new LogoutCommand("   "), CancellationToken.None);
        resultEmpty.IsSuccess.Should().BeTrue();
    }
}
