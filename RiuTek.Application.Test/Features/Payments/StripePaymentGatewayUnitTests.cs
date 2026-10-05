using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Infrastructure.Services;
using RiuTek.Infrastructure.Settings;
using Xunit;

namespace RiuTek.Application.Test.Features.Payments;

public class StripePaymentGatewayUnitTests
{
    private const string WebhookSecret = "whsec_test_secret_for_unit_tests_123456789";

    private static string GenerateStripeSignature(string payload, string secret, long? timestamp = null)
    {
        var ts = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signedPayload = $"{ts}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
        var signature = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        return $"t={ts},v1={signature}";
    }

    [Fact]
    public async Task EnsureCheckoutSessionAsync_WhenDisabled_ReturnsPaymentMethodNotAvailable()
    {
        var settings = Options.Create(new StripeSettings { Enabled = false });
        var gateway = new StripePaymentGateway(settings, NullLogger<StripePaymentGateway>.Instance);

        var request = new CreateStripeCheckoutSessionRequest(
            OrderId: Guid.NewGuid(),
            PaymentAttemptId: Guid.NewGuid(),
            OrderNumber: "ORD-123",
            Amount: 100_000m,
            Currency: "VND",
            CustomerEmail: "test@riutek.com",
            ProviderIdempotencyKey: "key-1",
            ExpiresAt: DateTime.UtcNow.AddMinutes(30)
        );

        var result = await gateway.EnsureCheckoutSessionAsync(request, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.PaymentMethodNotAvailable");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50000)]
    public async Task EnsureCheckoutSessionAsync_WhenAmountNonPositive_ReturnsInvalidAmount(decimal amount)
    {
        var settings = Options.Create(new StripeSettings
        {
            Enabled = true,
            SecretKey = "sk_test_mock_123",
            WebhookSecret = WebhookSecret,
            SuccessUrl = "https://riutek.com/success",
            CancelUrl = "https://riutek.com/cancel"
        });
        var gateway = new StripePaymentGateway(settings, NullLogger<StripePaymentGateway>.Instance);

        var request = new CreateStripeCheckoutSessionRequest(
            OrderId: Guid.NewGuid(),
            PaymentAttemptId: Guid.NewGuid(),
            OrderNumber: "ORD-123",
            Amount: amount,
            Currency: "VND",
            CustomerEmail: "test@riutek.com",
            ProviderIdempotencyKey: "key-1",
            ExpiresAt: DateTime.UtcNow.AddMinutes(30)
        );

        var result = await gateway.EnsureCheckoutSessionAsync(request, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Payment.InvalidAmount");
    }

    [Fact]
    public async Task EnsureCheckoutSessionAsync_WhenAmountHasFractionalPart_ReturnsInvalidAmount()
    {
        var settings = Options.Create(new StripeSettings
        {
            Enabled = true,
            SecretKey = "sk_test_mock_123",
            WebhookSecret = WebhookSecret,
            SuccessUrl = "https://riutek.com/success",
            CancelUrl = "https://riutek.com/cancel"
        });
        var gateway = new StripePaymentGateway(settings, NullLogger<StripePaymentGateway>.Instance);

        var request = new CreateStripeCheckoutSessionRequest(
            OrderId: Guid.NewGuid(),
            PaymentAttemptId: Guid.NewGuid(),
            OrderNumber: "ORD-123",
            Amount: 100_000.50m,
            Currency: "VND",
            CustomerEmail: "test@riutek.com",
            ProviderIdempotencyKey: "key-1",
            ExpiresAt: DateTime.UtcNow.AddMinutes(30)
        );

        var result = await gateway.EnsureCheckoutSessionAsync(request, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Payment.InvalidAmount");
        result.Error.Description.Should().Contain("Fractional amounts");
    }

    [Fact]
    public void ParseAndVerifyWebhook_WhenDisabledOrMissingSecret_ReturnsNotConfigured()
    {
        var settings = Options.Create(new StripeSettings { Enabled = false });
        var gateway = new StripePaymentGateway(settings, NullLogger<StripePaymentGateway>.Instance);

        var result = gateway.ParseAndVerifyWebhook("{}", "dummy-signature");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Webhook.NotConfigured");
    }

    [Theory]
    [InlineData("", "valid-sig")]
    [InlineData("{}", "")]
    [InlineData("   ", "valid-sig")]
    [InlineData("{}", "   ")]
    public void ParseAndVerifyWebhook_WhenPayloadOrSignatureEmpty_ReturnsInvalidSignature(string payload, string signature)
    {
        var settings = Options.Create(new StripeSettings
        {
            Enabled = true,
            SecretKey = "sk_test_mock_123",
            WebhookSecret = WebhookSecret
        });
        var gateway = new StripePaymentGateway(settings, NullLogger<StripePaymentGateway>.Instance);

        var result = gateway.ParseAndVerifyWebhook(payload, signature);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Webhook.InvalidSignature");
    }

    [Fact]
    public void ParseAndVerifyWebhook_WhenSignatureInvalid_ReturnsInvalidSignature()
    {
        var settings = Options.Create(new StripeSettings
        {
            Enabled = true,
            SecretKey = "sk_test_mock_123",
            WebhookSecret = WebhookSecret
        });
        var gateway = new StripePaymentGateway(settings, NullLogger<StripePaymentGateway>.Instance);

        var payload = "{\"id\":\"evt_123\",\"type\":\"checkout.session.completed\"}";
        var signature = "t=1234567890,v1=invalid_hash_signature_value";

        var result = gateway.ParseAndVerifyWebhook(payload, signature);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Webhook.InvalidSignature");
    }

    [Fact]
    public void ParseAndVerifyWebhook_WhenSignatureValid_CompletedEvent_ReturnsNormalizedEvent()
    {
        var settings = Options.Create(new StripeSettings
        {
            Enabled = true,
            SecretKey = "sk_test_mock_123",
            WebhookSecret = WebhookSecret
        });
        var gateway = new StripePaymentGateway(settings, NullLogger<StripePaymentGateway>.Instance);

        var orderId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var sessionId = "cs_test_session_123";

        var payload = $$"""
        {
          "id": "evt_test_completed_1",
          "object": "event",
          "type": "checkout.session.completed",
          "data": {
            "object": {
              "id": "{{sessionId}}",
              "object": "checkout.session",
              "amount_total": 250000,
              "currency": "vnd",
              "payment_status": "paid",
              "metadata": {
                "OrderId": "{{orderId}}",
                "PaymentAttemptId": "{{attemptId}}"
              }
            }
          }
        }
        """;

        var signature = GenerateStripeSignature(payload, WebhookSecret);

        var result = gateway.ParseAndVerifyWebhook(payload, signature);

        result.IsSuccess.Should().BeTrue();
        var evt = result.Value;
        evt.EventType.Should().Be(StripeWebhookEventType.CheckoutSessionCompleted);
        evt.EventId.Should().Be("evt_test_completed_1");
        evt.SessionId.Should().Be(sessionId);
        evt.OrderIdString.Should().Be(orderId.ToString());
        evt.PaymentAttemptIdString.Should().Be(attemptId.ToString());
        evt.AmountTotal.Should().Be(250000m);
        evt.Currency.Should().Be("vnd");
        evt.PaymentStatus.Should().Be("paid");
    }

    [Fact]
    public void ParseAndVerifyWebhook_WhenSignatureValid_ExpiredEvent_ReturnsNormalizedEvent()
    {
        var settings = Options.Create(new StripeSettings
        {
            Enabled = true,
            SecretKey = "sk_test_mock_123",
            WebhookSecret = WebhookSecret
        });
        var gateway = new StripePaymentGateway(settings, NullLogger<StripePaymentGateway>.Instance);

        var orderId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var sessionId = "cs_test_session_expired";

        var payload = $$"""
        {
          "id": "evt_test_expired_1",
          "object": "event",
          "type": "checkout.session.expired",
          "data": {
            "object": {
              "id": "{{sessionId}}",
              "object": "checkout.session",
              "amount_total": 300000,
              "currency": "vnd",
              "payment_status": "unpaid",
              "metadata": {
                "OrderId": "{{orderId}}",
                "PaymentAttemptId": "{{attemptId}}"
              }
            }
          }
        }
        """;

        var signature = GenerateStripeSignature(payload, WebhookSecret);

        var result = gateway.ParseAndVerifyWebhook(payload, signature);

        result.IsSuccess.Should().BeTrue();
        var evt = result.Value;
        evt.EventType.Should().Be(StripeWebhookEventType.CheckoutSessionExpired);
        evt.EventId.Should().Be("evt_test_expired_1");
        evt.SessionId.Should().Be(sessionId);
        evt.OrderIdString.Should().Be(orderId.ToString());
        evt.PaymentAttemptIdString.Should().Be(attemptId.ToString());
        evt.PaymentStatus.Should().Be("unpaid");
    }

    [Fact]
    public void ParseAndVerifyWebhook_WhenSignatureValid_UnknownEvent_ReturnsUnknownType()
    {
        var settings = Options.Create(new StripeSettings
        {
            Enabled = true,
            SecretKey = "sk_test_mock_123",
            WebhookSecret = WebhookSecret
        });
        var gateway = new StripePaymentGateway(settings, NullLogger<StripePaymentGateway>.Instance);

        var payload = """
        {
          "id": "evt_test_payment_intent",
          "object": "event",
          "type": "payment_intent.created",
          "data": {
            "object": {
              "id": "pi_123"
            }
          }
        }
        """;

        var signature = GenerateStripeSignature(payload, WebhookSecret);

        var result = gateway.ParseAndVerifyWebhook(payload, signature);

        result.IsSuccess.Should().BeTrue();
        result.Value.EventType.Should().Be(StripeWebhookEventType.Unknown);
        result.Value.EventId.Should().Be("evt_test_payment_intent");
    }
}
