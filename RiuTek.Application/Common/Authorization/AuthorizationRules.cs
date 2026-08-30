using RiuTek.Core.Enums;

namespace RiuTek.Application.Common.Authorization;

public static class AuthorizationRules
{
    public static bool IsContentManager(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return false;

        return string.Equals(role, UserRole.Admin.ToString(), StringComparison.OrdinalIgnoreCase) ||
               string.Equals(role, UserRole.Staff.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
