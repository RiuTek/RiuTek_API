using System.Text.RegularExpressions;

namespace RiuTek.Application.Features.Products.Common;

public static class ProductMediaHelper
{
    private static readonly Dictionary<string, string> MimeToExtensionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "image/jpeg", ".jpg" },
        { "image/png", ".png" },
        { "image/webp", ".webp" }
    };

    private static readonly Dictionary<string, string> ExtensionToMimeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".jpg", "image/jpeg" },
        { ".png", "image/png" },
        { ".webp", "image/webp" }
    };

    private static readonly Regex ManagedKeyRegex = new(
        @"^products/\d{4}/\d{2}/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\.(jpg|png|webp)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsSupportedMimeType(string contentType) =>
        !string.IsNullOrWhiteSpace(contentType) && MimeToExtensionMap.ContainsKey(contentType.Trim());

    public static string? GetExtensionForMimeType(string contentType) =>
        !string.IsNullOrWhiteSpace(contentType) && MimeToExtensionMap.TryGetValue(contentType.Trim(), out var ext)
            ? ext
            : null;

    public static string? GetExpectedMimeTypeForExtension(string extension) =>
        !string.IsNullOrWhiteSpace(extension) && ExtensionToMimeMap.TryGetValue(extension.Trim(), out var mime)
            ? mime
            : null;

    public static string GenerateObjectKey(string contentType)
    {
        var ext = GetExtensionForMimeType(contentType)
            ?? throw new ArgumentException($"Unsupported content type: {contentType}", nameof(contentType));

        var now = DateTime.UtcNow;
        var guid = Guid.NewGuid().ToString("D");
        return $"products/{now:yyyy}/{now:MM}/{guid}{ext}";
    }

    public static bool IsValidManagedObjectKey(string? objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey)) return false;
        var trimmed = objectKey.Trim();
        if (trimmed.Contains("..", StringComparison.Ordinal) ||
            trimmed.Contains("//", StringComparison.Ordinal) ||
            trimmed.Contains('?', StringComparison.Ordinal) ||
            trimmed.Contains('#', StringComparison.Ordinal))
        {
            return false;
        }

        return ManagedKeyRegex.IsMatch(trimmed);
    }

    public static bool IsValidHttpsUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
    }

    public static List<string> NormalizeAdditionalImages(string? primaryImageUrl, IEnumerable<string>? additionalImages)
    {
        if (additionalImages == null) return new List<string>();

        var normalizedPrimary = primaryImageUrl?.Trim();
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(normalizedPrimary))
        {
            seen.Add(normalizedPrimary);
        }

        foreach (var img in additionalImages)
        {
            if (string.IsNullOrWhiteSpace(img)) continue;
            var trimmed = img.Trim();
            if (seen.Add(trimmed))
            {
                result.Add(trimmed);
            }
        }

        return result;
    }

    public static async Task<RiuTek.Core.Common.Result<Guid>> ValidateActiveContentManagerAsync(
        RiuTek.Application.Common.Interfaces.ICurrentUserService currentUserService,
        RiuTek.Application.Common.Interfaces.IApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        if (!currentUserService.IsAuthenticated || currentUserService.UserId is null || currentUserService.UserId == Guid.Empty)
        {
            return RiuTek.Core.Common.Result.Failure<Guid>(
                RiuTek.Core.Common.Error.Unauthorized("Auth.Unauthorized", "Authentication is required."));
        }

        if (!RiuTek.Application.Common.Authorization.AuthorizationRules.IsContentManager(currentUserService.UserRole))
        {
            return RiuTek.Core.Common.Result.Failure<Guid>(
                RiuTek.Core.Common.Error.Forbidden("Auth.Forbidden", "Content manager privileges are required."));
        }

        var userId = currentUserService.UserId.Value;
        var user = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(context.Users),
            u => u.Id == userId,
            cancellationToken);

        if (user is null)
        {
            return RiuTek.Core.Common.Result.Failure<Guid>(
                RiuTek.Core.Common.Error.NotFound("User.NotFound", "User account was not found."));
        }

        if (!user.IsActive)
        {
            return RiuTek.Core.Common.Result.Failure<Guid>(
                RiuTek.Core.Common.Error.Forbidden("User.AccountInactive", "User account is inactive."));
        }

        if (!RiuTek.Application.Common.Authorization.AuthorizationRules.IsContentManager(user.Role.ToString()))
        {
            return RiuTek.Core.Common.Result.Failure<Guid>(
                RiuTek.Core.Common.Error.Forbidden("Auth.Forbidden", "Content manager privileges are required."));
        }

        return RiuTek.Core.Common.Result.Success(userId);
    }
}
