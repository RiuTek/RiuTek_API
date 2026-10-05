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

    public void Validate()
    {
        if (!Enabled) return;

        if (string.IsNullOrWhiteSpace(SecretKey) || SecretKey.Trim().StartsWith("placeholder", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Stripe:SecretKey is required and cannot be a placeholder when Stripe is enabled.");
        }

        if (string.IsNullOrWhiteSpace(WebhookSecret) || WebhookSecret.Trim().StartsWith("placeholder", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Stripe:WebhookSecret is required and cannot be a placeholder when Stripe is enabled.");
        }

        if (string.IsNullOrWhiteSpace(SuccessUrl) ||
            !Uri.TryCreate(SuccessUrl, UriKind.Absolute, out var successUri) ||
            (successUri.Scheme != Uri.UriSchemeHttps && !successUri.IsLoopback))
        {
            throw new InvalidOperationException("Stripe:SuccessUrl must be a valid absolute HTTPS URL (or HTTP localhost in development).");
        }

        if (string.IsNullOrWhiteSpace(CancelUrl) ||
            !Uri.TryCreate(CancelUrl, UriKind.Absolute, out var cancelUri) ||
            (cancelUri.Scheme != Uri.UriSchemeHttps && !cancelUri.IsLoopback))
        {
            throw new InvalidOperationException("Stripe:CancelUrl must be a valid absolute HTTPS URL (or HTTP localhost in development).");
        }

        if (CheckoutSessionMinutes < 30 || CheckoutSessionMinutes > 1440)
        {
            throw new InvalidOperationException("Stripe:CheckoutSessionMinutes must be between 30 and 1440 minutes.");
        }
    }
}
