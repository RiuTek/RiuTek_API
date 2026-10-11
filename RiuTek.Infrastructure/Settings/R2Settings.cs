namespace RiuTek.Infrastructure.Settings;

public class R2Settings
{
    public const string SectionName = "R2";

    public bool Enabled { get; set; } = false;
    public string ServiceUrl { get; set; } = string.Empty;
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;
    public string BucketName { get; set; } = string.Empty;
    public string PublicBaseUrl { get; set; } = string.Empty;
    public int PresignedUrlExpiryMinutes { get; set; } = 10;
    public long MaxFileSizeBytes { get; set; } = 5242880; // 5 MiB

    public void Validate()
    {
        if (!Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ServiceUrl) || ServiceUrl.Trim().StartsWith("placeholder", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("R2:ServiceUrl is required and cannot be a placeholder when R2 is enabled.");
        }

        ValidateHttpsUrl(ServiceUrl, "R2:ServiceUrl");

        if (string.IsNullOrWhiteSpace(AccessKeyId) || AccessKeyId.Trim().StartsWith("placeholder", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("R2:AccessKeyId is required and cannot be a placeholder when R2 is enabled.");
        }

        if (string.IsNullOrWhiteSpace(SecretAccessKey) || SecretAccessKey.Trim().StartsWith("placeholder", StringComparison.OrdinalIgnoreCase))
        {
            // Do NOT include secret in exception
            throw new InvalidOperationException("R2:SecretAccessKey is required and cannot be a placeholder when R2 is enabled.");
        }

        if (string.IsNullOrWhiteSpace(BucketName) || BucketName.Trim().StartsWith("placeholder", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("R2:BucketName is required and cannot be a placeholder when R2 is enabled.");
        }

        if (string.IsNullOrWhiteSpace(PublicBaseUrl) || PublicBaseUrl.Trim().StartsWith("placeholder", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("R2:PublicBaseUrl is required and cannot be a placeholder when R2 is enabled.");
        }

        ValidateHttpsUrl(PublicBaseUrl, "R2:PublicBaseUrl");

        if (PresignedUrlExpiryMinutes < 1 || PresignedUrlExpiryMinutes > 60)
        {
            throw new InvalidOperationException("R2:PresignedUrlExpiryMinutes must be between 1 and 60 minutes.");
        }

        const long maxAllowedFileSizeBytes = 20971520; // 20 MiB
        if (MaxFileSizeBytes <= 0 || MaxFileSizeBytes > maxAllowedFileSizeBytes)
        {
            throw new InvalidOperationException($"R2:MaxFileSizeBytes must be greater than 0 and cannot exceed {maxAllowedFileSizeBytes} bytes (20 MiB).");
        }
    }

    private static void ValidateHttpsUrl(string url, string settingName)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException($"{settingName} must be a valid absolute HTTPS URL.");
        }
    }
}
