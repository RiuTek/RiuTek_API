using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Core.Common;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Payments.Commands;

public record ProcessStripeWebhookCommand(string Payload, string Signature) : IRequest<Result>;

public class ProcessStripeWebhookCommandHandler : IRequestHandler<ProcessStripeWebhookCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly IStripePaymentGateway _stripeGateway;

    public ProcessStripeWebhookCommandHandler(
        IApplicationDbContext context,
        IStripePaymentGateway stripeGateway)
    {
        _context = context;
        _stripeGateway = stripeGateway;
    }

    public async Task<Result> Handle(
        ProcessStripeWebhookCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Verify webhook signature and extract event
        var verifyResult = _stripeGateway.ParseAndVerifyWebhook(request.Payload, request.Signature);
        if (verifyResult.IsFailure)
        {
            return Result.Failure(verifyResult.Error);
        }

        var webhookEvent = verifyResult.Value;

        // 2. Ignore unknown signed events without database mutation
        if (webhookEvent.EventType == StripeWebhookEventType.Unknown)
        {
            return Result.Success();
        }

        // 3. Validate metadata
        if (!Guid.TryParse(webhookEvent.OrderIdString, out var orderId) ||
            !Guid.TryParse(webhookEvent.PaymentAttemptIdString, out var paymentAttemptId))
        {
            return Result.Failure(Error.Validation(
                "Webhook.InvalidMetadata",
                "Webhook metadata missing valid OrderId or PaymentAttemptId."));
        }

        // 4. Handle checkout.session.completed
        if (webhookEvent.EventType == StripeWebhookEventType.CheckoutSessionCompleted)
        {
            if (webhookEvent.PaymentStatus != "paid")
            {
                return Result.Success();
            }

            var order = await _context.Orders
                .Include(o => o.PaymentAttempts)
                .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

            if (order is null)
            {
                return Result.Failure(Error.NotFound("Order.NotFound", "Order not found."));
            }

            var attempt = order.PaymentAttempts.FirstOrDefault(p => p.Id == paymentAttemptId);
            if (attempt is null)
            {
                return Result.Failure(Error.NotFound("Payment.AttemptNotFound", "Payment attempt not found."));
            }

            if (attempt.Method != PaymentMethod.Stripe)
            {
                return Result.Failure(Error.Conflict("Payment.InvalidMethod", "Not a Stripe payment attempt."));
            }

            if (!string.Equals(attempt.Currency, webhookEvent.Currency, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure(Error.Conflict("Payment.CurrencyMismatch", "Payment currency does not match attempt."));
            }

            if (!webhookEvent.AmountTotal.HasValue || attempt.Amount != webhookEvent.AmountTotal.Value)
            {
                return Result.Failure(Error.Conflict("Payment.AmountMismatch", "Payment amount does not match attempt."));
            }

            if (!string.IsNullOrWhiteSpace(webhookEvent.SessionId) &&
                !string.IsNullOrWhiteSpace(attempt.ProviderReference) &&
                !string.Equals(attempt.ProviderReference, webhookEvent.SessionId, StringComparison.Ordinal))
            {
                return Result.Failure(Error.Conflict("Payment.ProviderReferenceMismatch", "Provider reference mismatch."));
            }

            // Idempotent duplicate success check
            if (attempt.Status == PaymentAttemptStatus.Succeeded && order.PaymentStatus == PaymentStatus.Completed)
            {
                return Result.Success();
            }

            if (attempt.Status != PaymentAttemptStatus.Pending)
            {
                return Result.Failure(Error.Conflict("Payment.InvalidState", "Payment attempt is not in a pending state."));
            }

            var markResult = order.MarkPaymentSucceeded(attempt.Id, DateTime.UtcNow);
            if (markResult.IsFailure)
            {
                return markResult;
            }

            await _context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        // 5. Handle checkout.session.expired
        if (webhookEvent.EventType == StripeWebhookEventType.CheckoutSessionExpired)
        {
            var order = await _context.Orders
                .Include(o => o.Items)
                .Include(o => o.PaymentAttempts)
                .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

            if (order is null)
            {
                return Result.Failure(Error.NotFound("Order.NotFound", "Order not found."));
            }

            var attempt = order.PaymentAttempts.FirstOrDefault(p => p.Id == paymentAttemptId);
            if (attempt is null)
            {
                return Result.Failure(Error.NotFound("Payment.AttemptNotFound", "Payment attempt not found."));
            }

            if (attempt.Method != PaymentMethod.Stripe)
            {
                return Result.Failure(Error.Conflict("Payment.InvalidMethod", "Not a Stripe payment attempt."));
            }

            // Duplicate expired event check: if already expired and cancelled, return 200 without restoring stock again
            if (attempt.Status == PaymentAttemptStatus.Expired && order.Status == OrderStatus.Cancelled)
            {
                return Result.Success();
            }

            // If attempt succeeded already, do not cancel or restore stock
            if (attempt.Status == PaymentAttemptStatus.Succeeded || order.Status != OrderStatus.PendingPayment)
            {
                return Result.Success();
            }

            var expiredAt = DateTime.UtcNow >= attempt.ExpiresAt ? DateTime.UtcNow : attempt.ExpiresAt;
            var expireResult = order.ExpirePaymentAttempt(attempt.Id, expiredAt);
            if (expireResult.IsFailure)
            {
                return expireResult;
            }

            // Restore product stock once
            var productIds = order.Items.Select(i => i.ProductId).Distinct().ToList();
            var products = await _context.Products
                .Where(p => productIds.Contains(p.Id))
                .ToListAsync(cancellationToken);

            var productMap = products.ToDictionary(p => p.Id);
            foreach (var item in order.Items)
            {
                if (productMap.TryGetValue(item.ProductId, out var product))
                {
                    product.StockQuantity += item.Quantity;
                }
            }

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return Result.Success();
            }
            catch (DbUpdateConcurrencyException)
            {
                // Reload order state to check if duplicate concurrent webhook already succeeded
                var reloadedOrder = await _context.Orders
                    .AsNoTracking()
                    .Include(o => o.PaymentAttempts)
                    .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

                var reloadedAttempt = reloadedOrder?.PaymentAttempts.FirstOrDefault(p => p.Id == paymentAttemptId);
                if (reloadedAttempt?.Status == PaymentAttemptStatus.Expired)
                {
                    // Winning concurrent transaction already restored stock and marked attempt expired
                    return Result.Success();
                }

                throw;
            }
        }

        return Result.Success();
    }
}
