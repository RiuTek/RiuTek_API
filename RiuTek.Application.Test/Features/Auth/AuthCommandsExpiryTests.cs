using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Auth.Commands;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Test.Features.Auth;

public class AuthCommandsExpiryTests
{
    [Fact]
    public async Task LoginCommandHandler_ShouldReturnExpiresInSecondsAndSingleSourceRefreshTokenExpiry()
    {
        // Arrange: Use non-7 day lifetime (e.g. 3 days)
        const int customDays = 3;
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = new User(
            email: "login@example.com",
            passwordHash: "validhash",
            fullName: "Login User",
            role: UserRole.Customer
        );
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var passwordHasherMock = new Mock<IPasswordHasher>();
        passwordHasherMock.Setup(x => x.VerifyPassword("ValidPassword123!", "validhash")).Returns(true);

        var jwtGeneratorMock = new Mock<IJwtTokenGenerator>();
        jwtGeneratorMock.Setup(x => x.GenerateAccessToken(It.IsAny<User>())).Returns("mock_access_token");
        jwtGeneratorMock.Setup(x => x.GenerateRefreshToken()).Returns("mock_refresh_token");
        jwtGeneratorMock.Setup(x => x.ExpiryInSeconds).Returns(7200); // 120 minutes
        jwtGeneratorMock.Setup(x => x.RefreshTokenExpiryDays).Returns(customDays);

        var refreshTokenHasherMock = new Mock<IRefreshTokenHasher>();
        refreshTokenHasherMock.Setup(x => x.HashToken(It.IsAny<string>())).Returns((string t) => "hash_" + t);

        var handler = new LoginCommandHandler(context, passwordHasherMock.Object, jwtGeneratorMock.Object, refreshTokenHasherMock.Object);

        // Act
        var result = await handler.Handle(new LoginCommand("login@example.com", "ValidPassword123!"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ExpiresInSeconds.Should().Be(7200);
        result.Value.AccessToken.Should().Be("mock_access_token");

        // Verify single-source expiry calculation
        result.Value.RefreshTokenExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(customDays), TimeSpan.FromSeconds(5));
        user.RefreshTokenExpiryTime.Should().Be(result.Value.RefreshTokenExpiresAt);
    }

    [Fact]
    public async Task RegisterCommandHandler_ShouldReturnExpiresInSecondsAndSingleSourceRefreshTokenExpiry()
    {
        // Arrange: Use non-7 day lifetime (e.g. 5 days)
        const int customDays = 5;
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();

        var passwordHasherMock = new Mock<IPasswordHasher>();
        passwordHasherMock.Setup(x => x.HashPassword(It.IsAny<string>())).Returns("hashed_pwd");

        var jwtGeneratorMock = new Mock<IJwtTokenGenerator>();
        jwtGeneratorMock.Setup(x => x.GenerateAccessToken(It.IsAny<User>())).Returns("mock_access_token");
        jwtGeneratorMock.Setup(x => x.GenerateRefreshToken()).Returns("mock_refresh_token");
        jwtGeneratorMock.Setup(x => x.ExpiryInSeconds).Returns(1800); // 30 minutes
        jwtGeneratorMock.Setup(x => x.RefreshTokenExpiryDays).Returns(customDays);

        var refreshTokenHasherMock = new Mock<IRefreshTokenHasher>();
        refreshTokenHasherMock.Setup(x => x.HashToken(It.IsAny<string>())).Returns((string t) => "hash_" + t);

        var handler = new RegisterCommandHandler(context, passwordHasherMock.Object, jwtGeneratorMock.Object, refreshTokenHasherMock.Object);

        var command = new RegisterCommand("Register User", "register@example.com", "SecurePassword123!", null);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ExpiresInSeconds.Should().Be(1800);

        // Verify single-source expiry calculation
        result.Value.RefreshTokenExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(customDays), TimeSpan.FromSeconds(5));
        var createdUser = await context.Users.FirstAsync(u => u.Email == "register@example.com");
        createdUser.RefreshTokenExpiryTime.Should().Be(result.Value.RefreshTokenExpiresAt);
    }

    [Fact]
    public async Task RefreshTokenCommandHandler_ShouldReturnExpiresInSecondsAndSingleSourceRefreshTokenExpiry()
    {
        // Arrange: Use non-7 day lifetime (e.g. 2 days)
        const int customDays = 2;
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = new User(
            email: "refresh@example.com",
            passwordHash: "validhash",
            fullName: "Refresh User",
            role: UserRole.Customer
        )
        {
            RefreshToken = "hash_existing_refresh_token",
            RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1)
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var jwtGeneratorMock = new Mock<IJwtTokenGenerator>();
        jwtGeneratorMock.Setup(x => x.GenerateAccessToken(It.IsAny<User>())).Returns("new_access_token");
        jwtGeneratorMock.Setup(x => x.GenerateRefreshToken()).Returns("new_refresh_token");
        jwtGeneratorMock.Setup(x => x.ExpiryInSeconds).Returns(5400); // 90 minutes
        jwtGeneratorMock.Setup(x => x.RefreshTokenExpiryDays).Returns(customDays);

        var refreshTokenHasherMock = new Mock<IRefreshTokenHasher>();
        refreshTokenHasherMock.Setup(x => x.HashToken("existing_refresh_token")).Returns("hash_existing_refresh_token");
        refreshTokenHasherMock.Setup(x => x.HashToken("new_refresh_token")).Returns("hash_new_refresh_token");

        var handler = new RefreshTokenCommandHandler(context, jwtGeneratorMock.Object, refreshTokenHasherMock.Object);

        // Act
        var result = await handler.Handle(new RefreshTokenCommand("existing_refresh_token"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ExpiresInSeconds.Should().Be(5400);

        // Verify single-source expiry calculation
        result.Value.RefreshTokenExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(customDays), TimeSpan.FromSeconds(5));
        user.RefreshTokenExpiryTime.Should().Be(result.Value.RefreshTokenExpiresAt);
    }
}
