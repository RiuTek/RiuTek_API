using RiuTek.Core.Enums;

namespace RiuTek.Application.DTOs;

public record UserDto(
    Guid Id,
    string Email,
    string FullName,
    string? PhoneNumber,
    UserRole Role,
    DateTime CreatedAt
);

public record AuthResponseDto(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds,
    UserDto User
);

public record UserProfileDto(
    Guid Id,
    string Email,
    string FullName,
    string? PhoneNumber,
    UserRole Role,
    List<UserAddressDto> Addresses,
    DateTime CreatedAt
);
