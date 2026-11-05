namespace RiuTek.Application.Features.Banners.Common;

public static class BannerHelper
{
    public static bool IsValidInternalTargetUrl(string? targetUrl)
    {
        if (string.IsNullOrWhiteSpace(targetUrl)) return false;

        var trimmed = targetUrl.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 2048) return false;

        // Must start with exactly one '/' and not protocol-relative '//'
        if (!trimmed.StartsWith('/') || trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        // Must not contain backslash
        if (trimmed.Contains('\\'))
        {
            return false;
        }

        // Must not contain control characters (including CR, LF, tab)
        foreach (var ch in trimmed)
        {
            if (char.IsControl(ch))
            {
                return false;
            }
        }

        // Must be a valid relative URI (rejects absolute URIs like http://, javascript:, data:)
        if (!Uri.TryCreate(trimmed, UriKind.Relative, out var uri) || uri.IsAbsoluteUri)
        {
            return false;
        }

        return true;
    }
}
