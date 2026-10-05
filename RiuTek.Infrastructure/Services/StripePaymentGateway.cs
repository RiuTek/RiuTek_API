using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Core.Common;
using RiuTek.Infrastructure.Settings;
using Stripe;
using Stripe.Checkout;

namespace RiuTek.Infrastructure.Services;

public class StripePaymentGateway : IStripePaymentGateway
{
    private readonly StripeSettings _settings;
    private readonly IStripeClient? _stripeClient;
    private readonly ILogger<StripePaymentGateway> _logger;

    public bool IsEnabled => _settings.Enabled;

    public StripePaymentGateway(
        IOptions<StripeSettings> settings,
        ILogger<StripePaymentGateway> logger,
        IStripeClient? stripeClient = null)
    {
        _settings = settings.Value;
        _logger = logger;

        if (_settings.Enabled && !string.IsNullOrWhiteSpace(_settings.SecretKey))
        {
            _stripeClient = stripeClient ?? new StripeClient(_settings.SecretKey);
        }
    }

    public async Task<Result<StripeCheckoutSessionResult>> EnsureCheckoutSessionAsync(
        CreateStripeCheckoutSessionRequest request,
        string? existingProviderReference,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || _stripeClient is null)
        {
            return Result.Failure<StripeCheckoutSessionResult>(Error.Validation(
                "Checkout.PaymentMethodNotAvailable",
                "Stripe payment is not available at this time."));
        }

        if (request.Amount <= 0)
        {
            return Result.Failure<StripeCheckoutSessionResult>(Error.Validation(
                "Payment.InvalidAmount",
                "Payment amount must be greater than zero."));
        }

        if (request.Amount != Math.Floor(request.Amount))
        {
            return Result.Failure<StripeCheckoutSessionResult>(Error.Validation(
                "Payment.InvalidAmount",
                "Fractional amounts are not supported for VND currency."));
        }

        long stripeAmount;
        try
        {
            stripeAmount = Convert.ToInt64(request.Amount);
        }
        catch (OverflowException)
        {
            return Result.Failure<StripeCheckoutSessionResult>(Error.Validation(
                "Payment.InvalidAmount",
                "Payment amount overflowed."));
        }

        var sessionService = new SessionService(_stripeClient);

        try
        {
            if (!string.IsNullOrWhiteSpace(existingProviderReference))
            {
                var existingSession = await sessionService.GetAsync(
                    existingProviderReference.Trim(),
                    cancellationToken: cancellationToken);

                if (existingSession is not null && !string.IsNullOrWhiteSpace(existingSession.Url))
                {
                    return Result.Success(new StripeCheckoutSessionResult(
                        existingSession.Id,
                        existingSession.Url,
                        existingSession.ExpiresAt
                    ));
                }
            }

            var options = new SessionCreateOptions
            {
                Mode = "payment",
                CustomerEmail = !string.IsNullOrWhiteSpace(request.CustomerEmail) ? request.CustomerEmail.Trim() : null,
                SuccessUrl = _settings.SuccessUrl,
                CancelUrl = _settings.CancelUrl,
                ExpiresAt = request.ExpiresAt,
                LineItems =
                [
                    new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            Currency = request.Currency.Trim().ToLowerInvariant(),
                            UnitAmount = stripeAmount,
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = $"Đơn hàng {request.OrderNumber}"
                            }
                        },
                        Quantity = 1
                    }
                ],
                Metadata = new Dictionary<string, string>
                {
                    ["OrderId"] = request.OrderId.ToString(),
                    ["PaymentAttemptId"] = request.PaymentAttemptId.ToString()
                }
            };

            var requestOptions = new RequestOptions
            {
                IdempotencyKey = request.ProviderIdempotencyKey.Trim()
            };

            var session = await sessionService.CreateAsync(options, requestOptions, cancellationToken);
            return Result.Success(new StripeCheckoutSessionResult(
                session.Id,
                session.Url,
                session.ExpiresAt
            ));
        }
        catch (StripeException ex)
        {
            _logger.LogError("Stripe API error ensuring checkout session: {ErrorMessage}", ex.Message);
            return Result.Failure<StripeCheckoutSessionResult>(Error.Unavailable(
                "Payment.GatewayUnavailable",
                "Stripe payment gateway is temporarily unavailable. Please retry."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error communicating with Stripe gateway: {ErrorMessage}", ex.Message);
            return Result.Failure<StripeCheckoutSessionResult>(Error.Unavailable(
                "Payment.GatewayUnavailable",
                "Stripe payment gateway is temporarily unavailable. Please retry."));
        }
    }

    public Result<StripeWebhookEvent> ParseAndVerifyWebhook(string payload, string signature)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(_settings.WebhookSecret))
        {
            return Result.Failure<StripeWebhookEvent>(Error.Validation(
                "Webhook.NotConfigured",
                "Stripe webhook is not configured."));
        }

        if (string.IsNullOrWhiteSpace(payload) || string.IsNullOrWhiteSpace(signature))
        {
            return Result.Failure<StripeWebhookEvent>(Error.Validation(
                "Webhook.InvalidSignature",
                "Payload or Stripe-Signature header is missing."));
        }

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                payload,
                signature,
                _settings.WebhookSecret,
                throwOnApiVersionMismatch: false
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Stripe webhook signature verification failed: {Message}", ex.Message);
            return Result.Failure<StripeWebhookEvent>(Error.Validation(
                "Webhook.InvalidSignature",
                "Stripe webhook signature verification failed."));
        }

        if (stripeEvent.Type == "checkout.session.completed")
        {
            if (stripeEvent.Data.Object is not Session session)
            {
                return Result.Failure<StripeWebhookEvent>(Error.Validation(
                    "Webhook.InvalidPayload",
                    "Invalid session object in completed event."));
            }

            session.Metadata.TryGetValue("OrderId", out var orderIdStr);
            session.Metadata.TryGetValue("PaymentAttemptId", out var paymentAttemptIdStr);

            decimal? amount = session.AmountTotal.HasValue ? (decimal)session.AmountTotal.Value : null;

            return Result.Success(new StripeWebhookEvent(
                EventType: StripeWebhookEventType.CheckoutSessionCompleted,
                EventId: stripeEvent.Id,
                SessionId: session.Id,
                OrderIdString: orderIdStr,
                PaymentAttemptIdString: paymentAttemptIdStr,
                AmountTotal: amount,
                Currency: session.Currency,
                PaymentStatus: session.PaymentStatus
            ));
        }

        if (stripeEvent.Type == "checkout.session.expired")
        {
            if (stripeEvent.Data.Object is not Session session)
            {
                return Result.Failure<StripeWebhookEvent>(Error.Validation(
                    "Webhook.InvalidPayload",
                    "Invalid session object in expired event."));
            }

            session.Metadata.TryGetValue("OrderId", out var orderIdStr);
            session.Metadata.TryGetValue("PaymentAttemptId", out var paymentAttemptIdStr);

            decimal? amount = session.AmountTotal.HasValue ? (decimal)session.AmountTotal.Value : null;

            return Result.Success(new StripeWebhookEvent(
                EventType: StripeWebhookEventType.CheckoutSessionExpired,
                EventId: stripeEvent.Id,
                SessionId: session.Id,
                OrderIdString: orderIdStr,
                PaymentAttemptIdString: paymentAttemptIdStr,
                AmountTotal: amount,
                Currency: session.Currency,
                PaymentStatus: session.PaymentStatus
            ));
        }

        return Result.Success(new StripeWebhookEvent(
            EventType: StripeWebhookEventType.Unknown,
            EventId: stripeEvent.Id,
            SessionId: null,
            OrderIdString: null,
            PaymentAttemptIdString: null,
            AmountTotal: null,
            Currency: null,
            PaymentStatus: null
        ));
    }
}
