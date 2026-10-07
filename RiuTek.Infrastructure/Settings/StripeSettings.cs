namespace RiuTek.Infrastructure.Settings;

public class StripeSettings
{
    public const string SectionName = "Stripe";

    public bool Enabled { get; set; } = false;
    public string? SecretKey { get; set; }
    public string? WebhookSecret { get; set; }
    public string? SuccessUrl { get; set; }
    public string? CancelUrl { get; set; }
    public int CheckoutSessionMinutes { get; set; } = 30;

    public void Validate(string? environmentName = null)
    {
        if (!Enabled) return;

        if (string.IsNullOrWhiteSpace(SecretKey) || SecretKey.Trim().StartsWith("placeholder", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Stripe:SecretKey is required and cannot be a placeholder when Stripe is enabled.");
        }

        var trimmedSecretKey = SecretKey.Trim();
        if (trimmedSecretKey.StartsWith("sk_live_", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Stripe:SecretKey live keys ('sk_live_') are rejected. Phase C4 only supports Stripe sandbox test keys ('sk_test_').");
        }

        if (!trimmedSecretKey.StartsWith("sk_test_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Stripe:SecretKey must start with 'sk_test_' when Stripe is enabled.");
        }

        if (string.IsNullOrWhiteSpace(WebhookSecret) || WebhookSecret.Trim().StartsWith("placeholder", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Stripe:WebhookSecret is required and cannot be a placeholder when Stripe is enabled.");
        }

        var trimmedWebhookSecret = WebhookSecret.Trim();
        if (!trimmedWebhookSecret.StartsWith("whsec_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Stripe:WebhookSecret must start with 'whsec_' when Stripe is enabled.");
        }

        ValidateUrl(SuccessUrl, "Stripe:SuccessUrl", environmentName);
        ValidateUrl(CancelUrl, "Stripe:CancelUrl", environmentName);

        if (CheckoutSessionMinutes < 30 || CheckoutSessionMinutes > 1440)
        {
            throw new InvalidOperationException("Stripe:CheckoutSessionMinutes must be between 30 and 1440 minutes.");
        }
    }

    private static void ValidateUrl(string? url, string settingName, string? environmentName)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"{settingName} must be a valid absolute URL.");
        }

        var isDevelopment = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return;
        }

        if (isDevelopment && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{settingName} must be an absolute HTTPS URL (or HTTP loopback only in Development environment).");
    }
}
