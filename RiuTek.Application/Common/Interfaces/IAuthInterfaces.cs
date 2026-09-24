using RiuTek.Core.Entities;

namespace RiuTek.Application.Common.Interfaces;

public interface IPasswordHasher
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string passwordHash);
}

public interface IJwtTokenGenerator
{
    string GenerateAccessToken(User user);
    string GenerateRefreshToken();
    int ExpiryInSeconds { get; }
}

public interface IRefreshTokenHasher
{
    string HashToken(string token);
}

public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? UserEmail { get; }
    string? UserRole { get; }
    bool IsAuthenticated { get; }
}
