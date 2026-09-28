using RiuTek.Core.Common;
using RiuTek.Core.Enums;

namespace RiuTek.Core.Entities;

public class PaymentAttempt : BaseEntity
{
    public Guid OrderId { get; private set; }
    public PaymentMethod Method { get; private set; }
    public PaymentAttemptStatus Status { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "VND";
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string? ProviderReference { get; private set; }
    public string? FailureCode { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    public Order Order { get; private set; } = null!;

    protected PaymentAttempt() { }

    public PaymentAttempt(
        Guid orderId,
        PaymentMethod method,
        decimal amount,
        string idempotencyKey,
        DateTime expiresAt,
        string currency = "VND",
        DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;

        if (orderId == Guid.Empty)
            throw new ArgumentException("OrderId cannot be empty.", nameof(orderId));
        if (method is not PaymentMethod.Stripe and not PaymentMethod.VNPay)
            throw new ArgumentOutOfRangeException(nameof(method), "Only online payment methods create payment attempts.");
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Payment amount must be greater than zero.");
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("Payment idempotency key cannot be empty.", nameof(idempotencyKey));
        if (idempotencyKey.Trim().Length > 128)
            throw new ArgumentException("Payment idempotency key cannot exceed 128 characters.", nameof(idempotencyKey));
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
            throw new ArgumentException("Currency must be a three-letter code.", nameof(currency));
        if (expiresAt.Kind != DateTimeKind.Utc || expiresAt <= now)
            throw new ArgumentException("Payment attempt expiry must be a future UTC time.", nameof(expiresAt));

        OrderId = orderId;
        Method = method;
        Amount = amount;
        IdempotencyKey = idempotencyKey.Trim();
        Currency = currency.Trim().ToUpperInvariant();
        ExpiresAt = expiresAt;
        Status = PaymentAttemptStatus.Pending;
        CreatedAt = now;
    }

    public Result SetProviderReference(string providerReference)
    {
        if (string.IsNullOrWhiteSpace(providerReference))
            return Result.Failure(Error.Validation(
                "Payment.InvalidProviderReference",
                "Provider reference is required."));
        if (providerReference.Trim().Length > 200)
            return Result.Failure(Error.Validation(
                "Payment.InvalidProviderReference",
                "Provider reference cannot exceed 200 characters."));

        if (ProviderReference is not null &&
            !string.Equals(ProviderReference, providerReference.Trim(), StringComparison.Ordinal))
        {
            return Result.Failure(Error.Conflict(
                "Payment.ProviderReferenceAlreadySet",
                "Provider reference cannot be replaced."));
        }

        ProviderReference = providerReference.Trim();
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public Result MarkSucceeded(DateTime completedAtUtc)
    {
        if (Status == PaymentAttemptStatus.Succeeded)
            return Result.Success();
        if (Status != PaymentAttemptStatus.Pending)
            return InvalidTransition(PaymentAttemptStatus.Succeeded);
        if (ProviderReference is null)
            return Result.Failure(Error.Conflict(
                "Payment.ProviderReferenceRequired",
                "A provider reference is required before completing an online payment."));
        if (completedAtUtc.Kind != DateTimeKind.Utc)
            return Result.Failure(Error.Validation("Payment.InvalidTimestamp", "Completion time must be UTC."));
        if (completedAtUtc < CreatedAt)
            return Result.Failure(Error.Validation("Payment.InvalidTimestamp", "Completion time cannot precede creation time."));

        Status = PaymentAttemptStatus.Succeeded;
        CompletedAt = completedAtUtc;
        FailureCode = null;
        UpdatedAt = completedAtUtc;
        return Result.Success();
    }

    public Result MarkFailed(string failureCode, DateTime failedAtUtc)
    {
        if (Status == PaymentAttemptStatus.Failed &&
            string.Equals(FailureCode, failureCode?.Trim(), StringComparison.Ordinal))
            return Result.Success();
        if (Status != PaymentAttemptStatus.Pending)
            return InvalidTransition(PaymentAttemptStatus.Failed);
        if (string.IsNullOrWhiteSpace(failureCode))
            return Result.Failure(Error.Validation("Payment.InvalidFailureCode", "Failure code is required."));
        if (failureCode.Trim().Length > 100)
            return Result.Failure(Error.Validation("Payment.InvalidFailureCode", "Failure code cannot exceed 100 characters."));
        if (failedAtUtc.Kind != DateTimeKind.Utc)
            return Result.Failure(Error.Validation("Payment.InvalidTimestamp", "Failure time must be UTC."));
        if (failedAtUtc < CreatedAt)
            return Result.Failure(Error.Validation("Payment.InvalidTimestamp", "Failure time cannot precede creation time."));

        Status = PaymentAttemptStatus.Failed;
        FailureCode = failureCode.Trim();
        CompletedAt = failedAtUtc;
        UpdatedAt = failedAtUtc;
        return Result.Success();
    }

    public Result MarkExpired(DateTime expiredAtUtc)
    {
        if (Status == PaymentAttemptStatus.Expired)
            return Result.Success();
        if (Status != PaymentAttemptStatus.Pending)
            return InvalidTransition(PaymentAttemptStatus.Expired);
        if (expiredAtUtc.Kind != DateTimeKind.Utc)
            return Result.Failure(Error.Validation("Payment.InvalidTimestamp", "Expiry transition time must be UTC."));
        if (expiredAtUtc < ExpiresAt)
            return Result.Failure(Error.Validation("Payment.InvalidTimestamp", "Payment attempt cannot expire before its expiry time."));

        Status = PaymentAttemptStatus.Expired;
        CompletedAt = expiredAtUtc;
        UpdatedAt = expiredAtUtc;
        return Result.Success();
    }

    public Result MarkCancelled(DateTime cancelledAtUtc)
    {
        if (Status == PaymentAttemptStatus.Cancelled)
            return Result.Success();
        if (Status != PaymentAttemptStatus.Pending)
            return InvalidTransition(PaymentAttemptStatus.Cancelled);
        if (cancelledAtUtc.Kind != DateTimeKind.Utc)
            return Result.Failure(Error.Validation("Payment.InvalidTimestamp", "Cancellation time must be UTC."));
        if (cancelledAtUtc < CreatedAt)
            return Result.Failure(Error.Validation("Payment.InvalidTimestamp", "Cancellation time cannot precede creation time."));

        Status = PaymentAttemptStatus.Cancelled;
        CompletedAt = cancelledAtUtc;
        UpdatedAt = cancelledAtUtc;
        return Result.Success();
    }

    private Result InvalidTransition(PaymentAttemptStatus target) =>
        Result.Failure(Error.Conflict(
            "Payment.InvalidStatusTransition",
            $"Cannot transition payment attempt from {Status} to {target}."));
}
