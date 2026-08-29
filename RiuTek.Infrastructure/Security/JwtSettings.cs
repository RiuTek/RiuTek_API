using System.Text;

namespace RiuTek.Infrastructure.Security;

public class JwtSettings
{
    public const string SectionName = "JwtSettings";

    public string SecretKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "RiuTek.API";
    public string Audience { get; set; } = "RiuTek.Client";
    public int ExpiryMinutes { get; set; } = 60;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SecretKey))
        {
            throw new InvalidOperationException(
                "JWT SecretKey is missing or empty. Please set 'JwtSettings:SecretKey' or the 'JwtSettings__SecretKey' environment variable.");
        }

        if (SecretKey.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase) ||
            SecretKey.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase) ||
            SecretKey.Contains("YOUR_JWT_SECRET", StringComparison.OrdinalIgnoreCase) ||
            SecretKey.Contains("DO_NOT_COMMIT", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "JWT SecretKey contains placeholder text. Please configure a valid, secret key via environment variables or user secrets.");
        }

        if (Encoding.UTF8.GetByteCount(SecretKey) < 32)
        {
            throw new InvalidOperationException(
                "JWT SecretKey is too short. It must be at least 32 bytes (256 bits) for HMAC-SHA256 security.");
        }

        if (ExpiryMinutes <= 0)
        {
            throw new InvalidOperationException(
                "JWT ExpiryMinutes must be greater than 0.");
        }
    }
}
