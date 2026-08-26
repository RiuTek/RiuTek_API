using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using RiuTek.Application.Common.Interfaces;

namespace RiuTek.Infrastructure.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId
    {
        get
        {
            var userIdClaim = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? _httpContextAccessor.HttpContext?.User?.FindFirstValue("sub");

            return Guid.TryParse(userIdClaim, out var id) ? id : null;
        }
    }

    public string? UserEmail =>
        _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Email)
        ?? _httpContextAccessor.HttpContext?.User?.FindFirstValue("email");

    public string? UserRole =>
        _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Role)
        ?? _httpContextAccessor.HttpContext?.User?.FindFirstValue("role");

    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
}
