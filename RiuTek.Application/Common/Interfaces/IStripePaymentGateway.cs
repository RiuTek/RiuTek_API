using RiuTek.Core.Common;

namespace RiuTek.Application.Common.Interfaces;

public record CreateStripeCheckoutSessionRequest(
    Guid OrderId,
    Guid PaymentAttemptId,
    string OrderNumber,
    decimal Amount,
    string Currency,
    string CustomerEmail,
    string ProviderIdempotencyKey,
    DateTime ExpiresAt
);

public record StripeCheckoutSessionResult(
    string SessionId,
    string Url,
    DateTime ExpiresAt
);

public enum StripeWebhookEventType
{
    Unknown,
    CheckoutSessionCompleted,
    CheckoutSessionExpired
}

public record StripeWebhookEvent(
    StripeWebhookEventType EventType,
    string EventId,
    string? SessionId,
    string? OrderIdString,
    string? PaymentAttemptIdString,
    decimal? AmountTotal,
    string? Currency,
    string? PaymentStatus
);

public interface IStripePaymentGateway
{
    bool IsEnabled { get; }
    TimeSpan CheckoutSessionLifetime { get; }

    Task<Result<StripeCheckoutSessionResult>> EnsureCheckoutSessionAsync(
        CreateStripeCheckoutSessionRequest request,
        string? existingProviderReference,
        CancellationToken cancellationToken = default);

    Result<StripeWebhookEvent> ParseAndVerifyWebhook(string payload, string signature);
}
