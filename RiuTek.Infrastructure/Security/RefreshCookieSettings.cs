using Microsoft.AspNetCore.Http;

namespace RiuTek.Infrastructure.Security;

public class RefreshCookieSettings
{
    public const string SectionName = "RefreshCookieSettings";

    public string CookieName { get; set; } = "riutek.refresh_token";
    public string Path { get; set; } = "/api/v1/auth";
    public SameSiteMode SameSite { get; set; } = SameSiteMode.Strict;
    public bool HttpOnly { get; set; } = true;
    public bool Secure { get; set; } = true;
    public int ExpiryDays { get; set; } = 7;

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

        if (ExpiryDays <= 0)
        {
            throw new InvalidOperationException("Refresh cookie ExpiryDays must be greater than 0.");
        }
    }
}
