namespace RiuTek.Infrastructure.Settings;

public class R2Settings
{
    public const string SectionName = "R2";
    public const long MaxAllowedFileSizeBytes = 20971520; // 20 MiB

    private static readonly string[] PlaceholderKeywords = ["placeholder", "your_", "change_me", "do_not_commit"];

    public bool Enabled { get; set; } = false;
    public string ServiceUrl { get; set; } = string.Empty;
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;
    public string BucketName { get; set; } = string.Empty;
    public string PublicBaseUrl { get; set; } = string.Empty;
    public int PresignedUrlExpiryMinutes { get; set; } = 10;
    public long MaxFileSizeBytes { get; set; } = 5242880; // 5 MiB

    public static bool IsPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var trimmed = value.Trim();
        return PlaceholderKeywords.Any(k => trimmed.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    public void Validate()
    {
        if (!Enabled)
        {
            return;
        }

        ServiceUrl = ServiceUrl?.Trim() ?? string.Empty;
        AccessKeyId = AccessKeyId?.Trim() ?? string.Empty;
        SecretAccessKey = SecretAccessKey?.Trim() ?? string.Empty;
        BucketName ??= string.Empty;
        PublicBaseUrl = PublicBaseUrl?.Trim() ?? string.Empty;

        if (IsPlaceholder(ServiceUrl))
        {
            throw new InvalidOperationException("R2:ServiceUrl is required and cannot be a placeholder when R2 is enabled.");
        }

        ValidateRootHttpsUrl(ServiceUrl, "R2:ServiceUrl");

        if (IsPlaceholder(AccessKeyId))
        {
            throw new InvalidOperationException("R2:AccessKeyId is required and cannot be a placeholder when R2 is enabled.");
        }

        if (IsPlaceholder(SecretAccessKey))
        {
            // Do NOT include secret in exception
            throw new InvalidOperationException("R2:SecretAccessKey is required and cannot be a placeholder when R2 is enabled.");
        }

        if (IsPlaceholder(BucketName))
        {
            throw new InvalidOperationException("R2:BucketName is required and cannot be a placeholder when R2 is enabled.");
        }

        if (BucketName.Any(char.IsWhiteSpace))
        {
            throw new InvalidOperationException("R2:BucketName cannot contain whitespace.");
        }

        BucketName = BucketName.Trim();

        if (IsPlaceholder(PublicBaseUrl))
        {
            throw new InvalidOperationException("R2:PublicBaseUrl is required and cannot be a placeholder when R2 is enabled.");
        }

        ValidateHttpsUrl(PublicBaseUrl, "R2:PublicBaseUrl");

        if (PresignedUrlExpiryMinutes < 1 || PresignedUrlExpiryMinutes > 60)
        {
            throw new InvalidOperationException("R2:PresignedUrlExpiryMinutes must be between 1 and 60 minutes.");
        }

        if (MaxFileSizeBytes <= 0 || MaxFileSizeBytes > MaxAllowedFileSizeBytes)
        {
            throw new InvalidOperationException($"R2:MaxFileSizeBytes must be greater than 0 and cannot exceed {MaxAllowedFileSizeBytes} bytes (20 MiB).");
        }
    }

    private static void ValidateHttpsUrl(string url, string settingName)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException($"{settingName} must be a valid absolute HTTPS URL.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException($"{settingName} must not contain user-info.");
        }

        if (!string.IsNullOrEmpty(uri.Query))
        {
            throw new InvalidOperationException($"{settingName} must not contain query parameters.");
        }

        if (!string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException($"{settingName} must not contain URI fragments.");
        }
    }

    private static void ValidateRootHttpsUrl(string url, string settingName)
    {
        ValidateHttpsUrl(url, settingName);

        var uri = new Uri(url, UriKind.Absolute);
        if (uri.AbsolutePath != "/" && uri.AbsolutePath != "")
        {
            throw new InvalidOperationException($"{settingName} must be a root endpoint URL without path segments.");
        }
    }
}
