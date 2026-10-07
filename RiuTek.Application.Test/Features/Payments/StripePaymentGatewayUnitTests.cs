using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Core.Common;
using RiuTek.Infrastructure.Services;
using RiuTek.Infrastructure.Settings;
using Stripe;
using Stripe.Checkout;
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

    [Fact]
    public async Task EnsureCheckoutSessionAsync_SetsAllowedPaymentMethodTypesToCardOnly()
    {
        var settings = Options.Create(new StripeSettings
        {
            Enabled = true,
            SecretKey = "sk_test_mock_123",
            WebhookSecret = WebhookSecret,
            SuccessUrl = "https://riutek.com/success",
            CancelUrl = "https://riutek.com/cancel"
        });

        SessionCreateOptions? capturedOptions = null;
        var mockClient = new Mock<IStripeClient>();
        mockClient.Setup(c => c.RequestAsync<Session>(
            HttpMethod.Post,
            "/v1/checkout/sessions",
            It.IsAny<BaseOptions>(),
            It.IsAny<RequestOptions>(),
            It.IsAny<CancellationToken>()))
            .Callback<HttpMethod, string, BaseOptions, RequestOptions, CancellationToken>((_, _, opts, _, _) =>
            {
                capturedOptions = opts as SessionCreateOptions;
            })
            .ReturnsAsync(new Session
            {
                Id = "cs_test_session_card_1",
                Url = "https://checkout.stripe.com/pay/cs_test_session_card_1",
                ExpiresAt = DateTime.UtcNow.AddMinutes(30)
            });

        var gateway = new StripePaymentGateway(settings, NullLogger<StripePaymentGateway>.Instance, mockClient.Object);

        var orderId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var expiresAt = DateTime.UtcNow.AddMinutes(30);
        var request = new CreateStripeCheckoutSessionRequest(
            OrderId: orderId,
            PaymentAttemptId: attemptId,
            OrderNumber: "ORD-12345",
            Amount: 250_000m,
            Currency: "VND",
            CustomerEmail: "customer@riutek.com",
            ProviderIdempotencyKey: "idem-key-123",
            ExpiresAt: expiresAt
        );

        var result = await gateway.EnsureCheckoutSessionAsync(request, null);

        result.IsSuccess.Should().BeTrue();
        result.Value.SessionId.Should().Be("cs_test_session_card_1");
        result.Value.Url.Should().Be("https://checkout.stripe.com/pay/cs_test_session_card_1");

        capturedOptions.Should().NotBeNull();
        capturedOptions!.AllowedPaymentMethodTypes.Should().NotBeNull();
        capturedOptions.AllowedPaymentMethodTypes.Should().ContainSingle().Which.Should().Be("card");
        capturedOptions.Mode.Should().Be("payment");
        capturedOptions.CustomerEmail.Should().Be("customer@riutek.com");
        capturedOptions.SuccessUrl.Should().Be("https://riutek.com/success");
        capturedOptions.CancelUrl.Should().Be("https://riutek.com/cancel");
        capturedOptions.ExpiresAt.Should().Be(expiresAt);
        capturedOptions.Metadata["OrderId"].Should().Be(orderId.ToString());
        capturedOptions.Metadata["PaymentAttemptId"].Should().Be(attemptId.ToString());
        capturedOptions.LineItems.Should().HaveCount(1);
        capturedOptions.LineItems[0].PriceData.Currency.Should().Be("vnd");
        capturedOptions.LineItems[0].PriceData.UnitAmount.Should().Be(250_000);
    }

    [Fact]
    public async Task EnsureCheckoutSessionAsync_WhenReplayingValidOpenSession_ReturnsExistingSessionAndNeverCallsCreate()
    {
        var settings = Options.Create(new StripeSettings
        {
            Enabled = true,
            SecretKey = "sk_test_mock_123",
            WebhookSecret = WebhookSecret,
            SuccessUrl = "https://riutek.com/success",
            CancelUrl = "https://riutek.com/cancel"
        });

        var existingSessionId = "cs_existing_open_session";
        var existingExpiresAt = DateTime.UtcNow.AddMinutes(20);
        var mockClient = new Mock<IStripeClient>();
        mockClient.Setup(c => c.RequestAsync<Session>(
            HttpMethod.Get,
            $"/v1/checkout/sessions/{existingSessionId}",
            It.IsAny<BaseOptions>(),
            It.IsAny<RequestOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Session
            {
                Id = existingSessionId,
                Status = "open",
                Url = $"https://checkout.stripe.com/pay/{existingSessionId}",
                ExpiresAt = existingExpiresAt
            });

        var gateway = new StripePaymentGateway(settings, NullLogger<StripePaymentGateway>.Instance, mockClient.Object);

        var request = new CreateStripeCheckoutSessionRequest(
            OrderId: Guid.NewGuid(),
            PaymentAttemptId: Guid.NewGuid(),
            OrderNumber: "ORD-REPLAY-1",
            Amount: 100_000m,
            Currency: "VND",
            CustomerEmail: "replay@riutek.com",
            ProviderIdempotencyKey: "key-replay-1",
            ExpiresAt: DateTime.UtcNow.AddMinutes(30)
        );

        var result = await gateway.EnsureCheckoutSessionAsync(request, existingSessionId);

        result.IsSuccess.Should().BeTrue();
        result.Value.SessionId.Should().Be(existingSessionId);
        result.Value.Url.Should().Be($"https://checkout.stripe.com/pay/{existingSessionId}");
        result.Value.ExpiresAt.Should().Be(existingExpiresAt);

        mockClient.Verify(c => c.RequestAsync<Session>(
            HttpMethod.Post,
            It.IsAny<string>(),
            It.IsAny<BaseOptions>(),
            It.IsAny<RequestOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnsureCheckoutSessionAsync_WhenReplaySessionNotFound_ReturnsConflictPaymentInvalidSessionStateAndNeverCallsCreate()
    {
        var settings = Options.Create(new StripeSettings
        {
            Enabled = true,
            SecretKey = "sk_test_mock_123",
            WebhookSecret = WebhookSecret
        });

        var mockClient = new Mock<IStripeClient>();
        mockClient.Setup(c => c.RequestAsync<Session>(
            HttpMethod.Get,
            It.IsAny<string>(),
            It.IsAny<BaseOptions>(),
            It.IsAny<RequestOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((Session)null!);

        var gateway = new StripePaymentGateway(settings, NullLogger<StripePaymentGateway>.Instance, mockClient.Object);

        var request = new CreateStripeCheckoutSessionRequest(
            OrderId: Guid.NewGuid(),
            PaymentAttemptId: Guid.NewGuid(),
            OrderNumber: "ORD-NOTFOUND",
            Amount: 100_000m,
            Currency: "VND",
            CustomerEmail: "test@riutek.com",
            ProviderIdempotencyKey: "key-1",
            ExpiresAt: DateTime.UtcNow.AddMinutes(30)
        );

        var result = await gateway.EnsureCheckoutSessionAsync(request, "cs_missing_session");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Payment.InvalidSessionState");
        result.Error.Type.Should().Be(ErrorType.Conflict);

        mockClient.Verify(c => c.RequestAsync<Session>(
            HttpMethod.Post,
            It.IsAny<string>(),
            It.IsAny<BaseOptions>(),
            It.IsAny<RequestOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("expired")]
    [InlineData("canceled")]
    [InlineData("unknown_status")]
    public async Task EnsureCheckoutSessionAsync_WhenReplaySessionNotOpen_ReturnsConflictPaymentInvalidSessionStateAndNeverCallsCreate(string status)
    {
        var settings = Options.Create(new StripeSettings
        {
            Enabled = true,
            SecretKey = "sk_test_mock_123",
            WebhookSecret = WebhookSecret
        });

        var existingSessionId = "cs_status_test";
        var mockClient = new Mock<IStripeClient>();
        mockClient.Setup(c => c.RequestAsync<Session>(
            HttpMethod.Get,
            $"/v1/checkout/sessions/{existingSessionId}",
            It.IsAny<BaseOptions>(),
            It.IsAny<RequestOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Session
            {
                Id = existingSessionId,
                Status = status,
                Url = $"https://checkout.stripe.com/pay/{existingSessionId}",
                ExpiresAt = DateTime.UtcNow.AddMinutes(20)
            });

        var gateway = new StripePaymentGateway(settings, NullLogger<StripePaymentGateway>.Instance, mockClient.Object);

        var request = new CreateStripeCheckoutSessionRequest(
            OrderId: Guid.NewGuid(),
            PaymentAttemptId: Guid.NewGuid(),
            OrderNumber: "ORD-STATUS",
            Amount: 100_000m,
            Currency: "VND",
            CustomerEmail: "test@riutek.com",
            ProviderIdempotencyKey: "key-1",
            ExpiresAt: DateTime.UtcNow.AddMinutes(30)
        );

        var result = await gateway.EnsureCheckoutSessionAsync(request, existingSessionId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Payment.InvalidSessionState");
        result.Error.Type.Should().Be(ErrorType.Conflict);

        mockClient.Verify(c => c.RequestAsync<Session>(
            HttpMethod.Post,
            It.IsAny<string>(),
            It.IsAny<BaseOptions>(),
            It.IsAny<RequestOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-valid-url")]
    public async Task EnsureCheckoutSessionAsync_WhenReplaySessionUrlInvalidOrMissing_ReturnsConflictPaymentInvalidSessionStateAndNeverCallsCreate(string? url)
    {
        var settings = Options.Create(new StripeSettings
        {
            Enabled = true,
            SecretKey = "sk_test_mock_123",
            WebhookSecret = WebhookSecret
        });

        var existingSessionId = "cs_url_test";
        var mockClient = new Mock<IStripeClient>();
        mockClient.Setup(c => c.RequestAsync<Session>(
            HttpMethod.Get,
            $"/v1/checkout/sessions/{existingSessionId}",
            It.IsAny<BaseOptions>(),
            It.IsAny<RequestOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Session
            {
                Id = existingSessionId,
                Status = "open",
                Url = url,
                ExpiresAt = DateTime.UtcNow.AddMinutes(20)
            });

        var gateway = new StripePaymentGateway(settings, NullLogger<StripePaymentGateway>.Instance, mockClient.Object);

        var request = new CreateStripeCheckoutSessionRequest(
            OrderId: Guid.NewGuid(),
            PaymentAttemptId: Guid.NewGuid(),
            OrderNumber: "ORD-URL",
            Amount: 100_000m,
            Currency: "VND",
            CustomerEmail: "test@riutek.com",
            ProviderIdempotencyKey: "key-1",
            ExpiresAt: DateTime.UtcNow.AddMinutes(30)
        );

        var result = await gateway.EnsureCheckoutSessionAsync(request, existingSessionId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Payment.InvalidSessionState");
        result.Error.Type.Should().Be(ErrorType.Conflict);

        mockClient.Verify(c => c.RequestAsync<Session>(
            HttpMethod.Post,
            It.IsAny<string>(),
            It.IsAny<BaseOptions>(),
            It.IsAny<RequestOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnsureCheckoutSessionAsync_WhenReplaySessionExpiresInPast_ReturnsConflictPaymentInvalidSessionStateAndNeverCallsCreate()
    {
        var settings = Options.Create(new StripeSettings
        {
            Enabled = true,
            SecretKey = "sk_test_mock_123",
            WebhookSecret = WebhookSecret
        });

        var existingSessionId = "cs_expired_test";
        var mockClient = new Mock<IStripeClient>();
        mockClient.Setup(c => c.RequestAsync<Session>(
            HttpMethod.Get,
            $"/v1/checkout/sessions/{existingSessionId}",
            It.IsAny<BaseOptions>(),
            It.IsAny<RequestOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Session
            {
                Id = existingSessionId,
                Status = "open",
                Url = $"https://checkout.stripe.com/pay/{existingSessionId}",
                ExpiresAt = DateTime.UtcNow.AddMinutes(-5)
            });

        var gateway = new StripePaymentGateway(settings, NullLogger<StripePaymentGateway>.Instance, mockClient.Object);

        var request = new CreateStripeCheckoutSessionRequest(
            OrderId: Guid.NewGuid(),
            PaymentAttemptId: Guid.NewGuid(),
            OrderNumber: "ORD-EXPIRED",
            Amount: 100_000m,
            Currency: "VND",
            CustomerEmail: "test@riutek.com",
            ProviderIdempotencyKey: "key-1",
            ExpiresAt: DateTime.UtcNow.AddMinutes(30)
        );

        var result = await gateway.EnsureCheckoutSessionAsync(request, existingSessionId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Payment.InvalidSessionState");
        result.Error.Type.Should().Be(ErrorType.Conflict);

        mockClient.Verify(c => c.RequestAsync<Session>(
            HttpMethod.Post,
            It.IsAny<string>(),
            It.IsAny<BaseOptions>(),
            It.IsAny<RequestOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnsureCheckoutSessionAsync_WhenRequestCancellationRequested_RethrowsOperationCanceledException()
    {
        var settings = Options.Create(new StripeSettings
        {
            Enabled = true,
            SecretKey = "sk_test_mock_123",
            WebhookSecret = WebhookSecret
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var mockClient = new Mock<IStripeClient>();
        mockClient.Setup(c => c.RequestAsync<Session>(
            HttpMethod.Post,
            It.IsAny<string>(),
            It.IsAny<BaseOptions>(),
            It.IsAny<RequestOptions>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var gateway = new StripePaymentGateway(settings, NullLogger<StripePaymentGateway>.Instance, mockClient.Object);

        var request = new CreateStripeCheckoutSessionRequest(
            OrderId: Guid.NewGuid(),
            PaymentAttemptId: Guid.NewGuid(),
            OrderNumber: "ORD-CANCEL",
            Amount: 100_000m,
            Currency: "VND",
            CustomerEmail: "test@riutek.com",
            ProviderIdempotencyKey: "key-1",
            ExpiresAt: DateTime.UtcNow.AddMinutes(30)
        );

        var act = () => gateway.EnsureCheckoutSessionAsync(request, null, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task EnsureCheckoutSessionAsync_WhenInternalOperationCanceledExceptionAndRequestNotCancelled_Returns503PaymentGatewayUnavailable()
    {
        var settings = Options.Create(new StripeSettings
        {
            Enabled = true,
            SecretKey = "sk_test_mock_123",
            WebhookSecret = WebhookSecret
        });

        var mockClient = new Mock<IStripeClient>();
        mockClient.Setup(c => c.RequestAsync<Session>(
            HttpMethod.Post,
            It.IsAny<string>(),
            It.IsAny<BaseOptions>(),
            It.IsAny<RequestOptions>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var gateway = new StripePaymentGateway(settings, NullLogger<StripePaymentGateway>.Instance, mockClient.Object);

        var request = new CreateStripeCheckoutSessionRequest(
            OrderId: Guid.NewGuid(),
            PaymentAttemptId: Guid.NewGuid(),
            OrderNumber: "ORD-TIMEOUT",
            Amount: 100_000m,
            Currency: "VND",
            CustomerEmail: "test@riutek.com",
            ProviderIdempotencyKey: "key-1",
            ExpiresAt: DateTime.UtcNow.AddMinutes(30)
        );

        var result = await gateway.EnsureCheckoutSessionAsync(request, null, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Payment.GatewayUnavailable");
        result.Error.Type.Should().Be(ErrorType.Unavailable);
    }

    [Fact]
    public async Task EnsureCheckoutSessionAsync_WhenInternalTaskCanceledExceptionAndRequestNotCancelled_Returns503PaymentGatewayUnavailable()
    {
        var settings = Options.Create(new StripeSettings
        {
            Enabled = true,
            SecretKey = "sk_test_mock_123",
            WebhookSecret = WebhookSecret
        });

        var mockClient = new Mock<IStripeClient>();
        mockClient.Setup(c => c.RequestAsync<Session>(
            HttpMethod.Post,
            It.IsAny<string>(),
            It.IsAny<BaseOptions>(),
            It.IsAny<RequestOptions>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        var gateway = new StripePaymentGateway(settings, NullLogger<StripePaymentGateway>.Instance, mockClient.Object);

        var request = new CreateStripeCheckoutSessionRequest(
            OrderId: Guid.NewGuid(),
            PaymentAttemptId: Guid.NewGuid(),
            OrderNumber: "ORD-TIMEOUT-TASK",
            Amount: 100_000m,
            Currency: "VND",
            CustomerEmail: "test@riutek.com",
            ProviderIdempotencyKey: "key-1",
            ExpiresAt: DateTime.UtcNow.AddMinutes(30)
        );

        var result = await gateway.EnsureCheckoutSessionAsync(request, null, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Payment.GatewayUnavailable");
        result.Error.Type.Should().Be(ErrorType.Unavailable);
    }
}
