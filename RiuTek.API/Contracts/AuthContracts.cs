using RiuTek.Application.DTOs;

namespace RiuTek.API.Contracts;

public record RegisterRequest(
    string FullName,
    string Email,
    string Password,
    string? PhoneNumber);

public record LoginRequest(
    string Email,
    string Password);

public record AuthResponse(
    string AccessToken,
    int ExpiresInSeconds,
    UserDto User);
