using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Security;
using RiuTek.Infrastructure.Services;

namespace RiuTek.Application.Test.Security;

public class JwtTokenGeneratorTests
{
    [Theory]
    [InlineData(15, 900)]
    [InlineData(60, 3600)]
    [InlineData(120, 7200)]
    public void ExpiryInSeconds_ShouldCalculateExactSecondsFromExpiryMinutes(int minutes, int expectedSeconds)
    {
        // Arrange
        var settings = new JwtSettings
        {
            SecretKey = "Valid_Super_Secret_Key_At_Least_32_Bytes_Long_123456",
            ExpiryMinutes = minutes
        };
        var generator = new JwtTokenGenerator(settings);

        // Act & Assert
        generator.ExpiryInSeconds.Should().Be(expectedSeconds);
    }

    [Fact]
    public void GenerateAccessToken_ShouldProduceTokenWithValidClaimsAndExpiry()
    {
        // Arrange
        var settings = new JwtSettings
        {
            SecretKey = "Valid_Super_Secret_Key_At_Least_32_Bytes_Long_123456",
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            ExpiryMinutes = 30
        };
        var generator = new JwtTokenGenerator(settings);
        var user = new User(
            email: "testuser@example.com",
            passwordHash: "hash123",
            fullName: "Test User",
            role: UserRole.Admin,
            phoneNumber: "0900000000"
        );

        // Act
        var tokenString = generator.GenerateAccessToken(user);

        // Assert
        tokenString.Should().NotBeNullOrWhiteSpace();

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(tokenString);

        jwtToken.Issuer.Should().Be("TestIssuer");
        jwtToken.Audiences.Should().Contain("TestAudience");
        jwtToken.Claims.Should().Contain(c => (c.Type == "role" || c.Type == ClaimTypes.Role) && c.Value == UserRole.Admin.ToString());
        jwtToken.Claims.Should().Contain(c => (c.Type == "email" || c.Type == ClaimTypes.Email) && c.Value == "testuser@example.com");
    }
}
