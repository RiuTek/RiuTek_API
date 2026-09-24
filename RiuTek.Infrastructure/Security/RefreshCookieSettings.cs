using Microsoft.AspNetCore.Http;

namespace RiuTek.Infrastructure.Security;

public class RefreshCookieSettings
{
    public const string SectionName = "RefreshCookieSettings";

    public string CookieName { get; set; } = "riutek.refresh_token";
    public string Path { get; set; } = "/api/v1/auth";

    // Security invariants - not configurable via appsettings or environment variables
    public SameSiteMode SameSite => SameSiteMode.Strict;
    public bool HttpOnly => true;
    public bool Secure => true;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(CookieName))
        {
            throw new InvalidOperationException("Refresh cookie CookieName must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(Path) || !Path.StartsWith('/'))
        {
            throw new InvalidOperationException("Refresh cookie Path must start with '/'.");
        }
    }
}
