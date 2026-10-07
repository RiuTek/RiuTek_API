using FluentAssertions;
using RiuTek.Infrastructure.Settings;
using Xunit;

namespace RiuTek.Application.Test.Features.Payments;

public class StripeSettingsUnitTests
{
    [Fact]
    public void Validate_WhenDisabled_AllowsMissingOrEmptySecrets()
    {
        var settings = new StripeSettings
        {
            Enabled = false,
            SecretKey = null,
            WebhookSecret = null,
            SuccessUrl = null,
            CancelUrl = null,
            CheckoutSessionMinutes = 0
        };

        var act = () => settings.Validate();
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("placeholder_key")]
    [InlineData("PLACEHOLDER_SK")]
    public void Validate_WhenEnabled_AndSecretKeyInvalidOrPlaceholder_ThrowsInvalidOperationException(string? secretKey)
    {
        var settings = new StripeSettings
        {
            Enabled = true,
            SecretKey = secretKey,
            WebhookSecret = "whsec_test_valid",
            SuccessUrl = "https://riutek.com/success",
            CancelUrl = "https://riutek.com/cancel",
            CheckoutSessionMinutes = 30
        };

        var act = () => settings.Validate();
        var ex = act.Should().Throw<InvalidOperationException>().Which;
        ex.Message.Should().Contain("Stripe:SecretKey");
        if (!string.IsNullOrWhiteSpace(secretKey))
        {
            ex.Message.Should().NotContain(secretKey);
        }
    }

    [Theory]
    [InlineData("sk_live_test_dummy_key")]
    [InlineData("SK_LIVE_MOCK_TEST_KEY")]
    public void Validate_WhenEnabled_AndSecretKeyIsLiveKey_ThrowsInvalidOperationExceptionAndDoesNotLeakSecret(string liveKey)
    {
        var settings = new StripeSettings
        {
            Enabled = true,
            SecretKey = liveKey,
            WebhookSecret = "whsec_test_valid_123",
            SuccessUrl = "https://riutek.com/success",
            CancelUrl = "https://riutek.com/cancel",
            CheckoutSessionMinutes = 30
        };

        var act = () => settings.Validate();
        var ex = act.Should().Throw<InvalidOperationException>().Which;
        ex.Message.Should().Contain("sk_live_");
        ex.Message.Should().Contain("sandbox");
        ex.Message.Should().NotContain(liveKey);
    }

    [Theory]
    [InlineData("pk_test_1234567890abcdef")]
    [InlineData("rk_test_1234567890abcdef")]
    [InlineData("custom_prefix_secret_key_123")]
    public void Validate_WhenEnabled_AndSecretKeyDoesNotStartWithSkTest_ThrowsInvalidOperationExceptionAndDoesNotLeakSecret(string invalidKey)
    {
        var settings = new StripeSettings
        {
            Enabled = true,
            SecretKey = invalidKey,
            WebhookSecret = "whsec_test_valid_123",
            SuccessUrl = "https://riutek.com/success",
            CancelUrl = "https://riutek.com/cancel",
            CheckoutSessionMinutes = 30
        };

        var act = () => settings.Validate();
        var ex = act.Should().Throw<InvalidOperationException>().Which;
        ex.Message.Should().Contain("sk_test_");
        ex.Message.Should().NotContain(invalidKey);
    }

    [Theory]
    [InlineData("sec_test_1234567890abcdef")]
    [InlineData("webhook_secret_without_prefix")]
    public void Validate_WhenEnabled_AndWebhookSecretDoesNotStartWithWhsec_ThrowsInvalidOperationExceptionAndDoesNotLeakSecret(string invalidSecret)
    {
        var settings = new StripeSettings
        {
            Enabled = true,
            SecretKey = "sk_test_valid_key_123",
            WebhookSecret = invalidSecret,
            SuccessUrl = "https://riutek.com/success",
            CancelUrl = "https://riutek.com/cancel",
            CheckoutSessionMinutes = 30
        };

        var act = () => settings.Validate();
        var ex = act.Should().Throw<InvalidOperationException>().Which;
        ex.Message.Should().Contain("whsec_");
        ex.Message.Should().NotContain(invalidSecret);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("placeholder_whsec")]
    [InlineData("PLACEHOLDER_SECRET")]
    public void Validate_WhenEnabled_AndWebhookSecretInvalidOrPlaceholder_ThrowsInvalidOperationException(string? webhookSecret)
    {
        var settings = new StripeSettings
        {
            Enabled = true,
            SecretKey = "sk_test_valid_key_123",
            WebhookSecret = webhookSecret,
            SuccessUrl = "https://riutek.com/success",
            CancelUrl = "https://riutek.com/cancel",
            CheckoutSessionMinutes = 30
        };

        var act = () => settings.Validate();
        var ex = act.Should().Throw<InvalidOperationException>().Which;
        ex.Message.Should().Contain("Stripe:WebhookSecret");
        if (!string.IsNullOrWhiteSpace(webhookSecret))
        {
            ex.Message.Should().NotContain(webhookSecret);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-valid-url")]
    [InlineData("ftp://riutek.com/success")]
    [InlineData("http://insecure-domain.com/success")]
    public void Validate_WhenEnabled_AndSuccessUrlInvalid_ThrowsInvalidOperationException(string? successUrl)
    {
        var settings = new StripeSettings
        {
            Enabled = true,
            SecretKey = "sk_test_valid_key_123",
            WebhookSecret = "whsec_valid_secret_123",
            SuccessUrl = successUrl,
            CancelUrl = "https://riutek.com/cancel",
            CheckoutSessionMinutes = 30
        };

        var act = () => settings.Validate();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SuccessUrl*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-valid-url")]
    [InlineData("http://insecure-domain.com/cancel")]
    public void Validate_WhenEnabled_AndCancelUrlInvalid_ThrowsInvalidOperationException(string? cancelUrl)
    {
        var settings = new StripeSettings
        {
            Enabled = true,
            SecretKey = "sk_test_valid_key_123",
            WebhookSecret = "whsec_valid_secret_123",
            SuccessUrl = "https://riutek.com/success",
            CancelUrl = cancelUrl,
            CheckoutSessionMinutes = 30
        };

        var act = () => settings.Validate();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CancelUrl*");
    }

    [Fact]
    public void Validate_WhenEnabled_AllowsLocalhostHttpUrlInDevelopment()
    {
        var settings = new StripeSettings
        {
            Enabled = true,
            SecretKey = "sk_test_valid_key_123",
            WebhookSecret = "whsec_valid_secret_123",
            SuccessUrl = "http://localhost:3000/order-success",
            CancelUrl = "http://127.0.0.1:3000/order-cancel",
            CheckoutSessionMinutes = 30
        };

        var act = () => settings.Validate("Development");
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Testing")]
    public void Validate_WhenEnabled_AndLocalhostHttpUrlInNonDevelopment_ThrowsInvalidOperationException(string? env)
    {
        var settings = new StripeSettings
        {
            Enabled = true,
            SecretKey = "sk_test_valid_key_123",
            WebhookSecret = "whsec_valid_secret_123",
            SuccessUrl = "http://localhost:3000/order-success",
            CancelUrl = "http://127.0.0.1:3000/order-cancel",
            CheckoutSessionMinutes = 30
        };

        var act = () => settings.Validate(env);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SuccessUrl*");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Development")]
    [InlineData(null)]
    public void Validate_WhenEnabled_AndHttpsUrlInAnyEnvironment_DoesNotThrow(string? env)
    {
        var settings = new StripeSettings
        {
            Enabled = true,
            SecretKey = "sk_test_valid_key_123",
            WebhookSecret = "whsec_valid_secret_123",
            SuccessUrl = "https://riutek.com/checkout/success",
            CancelUrl = "https://riutek.com/checkout/cancel",
            CheckoutSessionMinutes = 30
        };

        var act = () => settings.Validate(env);
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_WhenEnabled_AndNonLoopbackHttpUrlInDevelopment_ThrowsInvalidOperationException()
    {
        var settings = new StripeSettings
        {
            Enabled = true,
            SecretKey = "sk_test_valid_key_123",
            WebhookSecret = "whsec_valid_secret_123",
            SuccessUrl = "http://external-site.com/checkout/success",
            CancelUrl = "https://riutek.com/checkout/cancel",
            CheckoutSessionMinutes = 30
        };

        var act = () => settings.Validate("Development");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SuccessUrl*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(29)]
    [InlineData(1441)]
    public void Validate_WhenEnabled_AndDurationOutOfRange_ThrowsInvalidOperationException(int minutes)
    {
        var settings = new StripeSettings
        {
            Enabled = true,
            SecretKey = "sk_test_valid_key_123",
            WebhookSecret = "whsec_valid_secret_123",
            SuccessUrl = "https://riutek.com/success",
            CancelUrl = "https://riutek.com/cancel",
            CheckoutSessionMinutes = minutes
        };

        var act = () => settings.Validate();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CheckoutSessionMinutes*");
    }

    [Fact]
    public void Validate_WhenEnabled_AndValidSettings_DoesNotThrow()
    {
        var settings = new StripeSettings
        {
            Enabled = true,
            SecretKey = "sk_test_valid_key_123",
            WebhookSecret = "whsec_valid_secret_123",
            SuccessUrl = "https://riutek.com/checkout/success",
            CancelUrl = "https://riutek.com/checkout/cancel",
            CheckoutSessionMinutes = 30
        };

        var act = () => settings.Validate();
        act.Should().NotThrow();
    }
}
