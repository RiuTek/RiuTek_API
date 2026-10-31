using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Products.Common;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Orders.Common;

public sealed record CheckoutOrderLine(
    Product Product,
    int Quantity,
    Guid? PCBuildId = null);

public sealed record PlaceCheckoutOrderInput(
    User User,
    UserAddress Address,
    PaymentMethod PaymentMethod,
    string? Notes,
    string IdempotencyKey,
    IReadOnlyList<CheckoutOrderLine> Lines,
    Cart? SourceCart = null);

public class CheckoutOrderProcessor
{
    private readonly IApplicationDbContext _context;
    private readonly IStripePaymentGateway? _stripeGateway;

    public CheckoutOrderProcessor(
        IApplicationDbContext context,
        IStripePaymentGateway? stripeGateway = null)
    {
        _context = context;
        _stripeGateway = stripeGateway;
    }

    public Result ValidatePaymentMethodAvailability(PaymentMethod paymentMethod)
    {
        if (paymentMethod == PaymentMethod.Stripe)
        {
            if (_stripeGateway is null || !_stripeGateway.IsEnabled)
            {
                return Result.Failure(Error.Validation(
                    "Checkout.PaymentMethodNotAvailable",
                    "Stripe payment is not available at this time."));
            }
        }
        else if (paymentMethod != PaymentMethod.COD)
        {
            return Result.Failure(Error.Validation(
                "Checkout.PaymentMethodNotAvailable",
                "Payment method is not available."));
        }

        return Result.Success();
    }

    public async Task<Result<CheckoutOrderDto>> PlaceOrderAsync(
        PlaceCheckoutOrderInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        // 1. Validate Payment Method availability
        var paymentCheck = ValidatePaymentMethodAvailability(input.PaymentMethod);
        if (paymentCheck.IsFailure)
        {
            return Result.Failure<CheckoutOrderDto>(paymentCheck.Error);
        }

        // 2. Validate Lines defensively
        if (input.Lines == null || input.Lines.Count == 0)
        {
            return Result.Failure<CheckoutOrderDto>(Error.Validation(
                "Checkout.EmptyLines",
                "Order must contain at least one line."));
        }

        var seenProductIds = new HashSet<Guid>();
        foreach (var line in input.Lines)
        {
            if (line.Product is null)
            {
                return Result.Failure<CheckoutOrderDto>(Error.NotFound(
                    "Checkout.ProductNotFound",
                    "A product in the checkout lines was not found."));
            }

            if (!seenProductIds.Add(line.Product.Id))
            {
                return Result.Failure<CheckoutOrderDto>(Error.Validation(
                    "Checkout.DuplicateProduct",
                    "Duplicate products are not allowed in checkout lines."));
            }

            if (!line.Product.IsActive)
            {
                return Result.Failure<CheckoutOrderDto>(Error.Conflict(
                    "Checkout.ProductInactive",
                    $"Product '{line.Product.Name}' is no longer active."));
            }

            if (line.Product.Price <= 0)
            {
                return Result.Failure<CheckoutOrderDto>(Error.Conflict(
                    "Checkout.InvalidPrice",
                    $"Product '{line.Product.Name}' has an invalid price."));
            }

            if (line.Product.StockQuantity < line.Quantity)
            {
                return Result.Failure<CheckoutOrderDto>(Error.Conflict(
                    "Checkout.InsufficientStock",
                    $"Insufficient stock for product '{line.Product.Name}'. Available: {line.Product.StockQuantity}, Requested: {line.Quantity}."));
            }
        }

        // 3. Build Order entity and OrderItems
        var orderNumber = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        var customerName = !string.IsNullOrWhiteSpace(input.Address.ReceiverName) ? input.Address.ReceiverName : input.User.FullName;
        var customerPhone = !string.IsNullOrWhiteSpace(input.Address.PhoneNumber) ? input.Address.PhoneNumber : (input.User.PhoneNumber ?? "0000000000");
        var customerEmail = input.User.Email;
        var formattedShippingAddress = CheckoutMappingExtensions.FormatShippingAddress(input.Address);
        var notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();

        var order = new Order(
            orderNumber: orderNumber,
            userId: input.User.Id,
            checkoutIdempotencyKey: input.IdempotencyKey,
            customerName: customerName,
            customerEmail: customerEmail,
            customerPhone: customerPhone,
            shippingAddress: formattedShippingAddress,
            paymentMethod: input.PaymentMethod,
            notes: notes
        );

        foreach (var line in input.Lines)
        {
            var product = line.Product;
            var addItemResult = order.AddItem(
                productId: product.Id,
                productName: product.Name,
                productSku: product.Sku,
                unitPrice: product.Price,
                quantity: line.Quantity,
                pcBuildId: line.PCBuildId
            );

            if (addItemResult.IsFailure)
            {
                return Result.Failure<CheckoutOrderDto>(addItemResult.Error);
            }
        }

        // 4. Decrement Product.StockQuantity for each line and record StockMovement
        foreach (var line in input.Lines)
        {
            var product = line.Product;
            var movementResult = StockMovementHelper.ApplyStockMovement(
                product,
                -line.Quantity,
                StockMovementReason.Checkout,
                note: null,
                actorUserId: input.User.Id,
                orderId: order.Id,
                context: _context);

            if (movementResult.IsFailure)
            {
                return Result.Failure<CheckoutOrderDto>(movementResult.Error);
            }
        }

        // 5. Remove checked-out lines from SourceCart
        if (input.SourceCart is not null)
        {
            foreach (var line in input.Lines)
            {
                var existingItem = input.SourceCart.Items.FirstOrDefault(i => i.ProductId == line.Product.Id);
                if (existingItem is not null)
                {
                    input.SourceCart.RemoveItem(line.Product.Id);
                }
            }
        }

        // 6. Create PaymentAttempt for online payment (Stripe) within initial DB transaction
        PaymentAttempt? stripeAttempt = null;
        if (input.PaymentMethod == PaymentMethod.Stripe)
        {
            var nowUtc = DateTime.UtcNow;
            var expiresAtUtc = nowUtc.Add(_stripeGateway!.CheckoutSessionLifetime);
            var providerIdempotencyKey = $"stripe-{order.Id:N}";
            var attemptResult = order.CreatePaymentAttempt(providerIdempotencyKey, expiresAtUtc, nowUtc: nowUtc);
            if (attemptResult.IsFailure)
            {
                return Result.Failure<CheckoutOrderDto>(attemptResult.Error);
            }
            stripeAttempt = attemptResult.Value;
        }

        // 7. Add Order and execute single SaveChangesAsync
        _context.Orders.Add(order);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _context.ClearTrackedChanges();
            return await ReloadAndReplayExistingOrderAsync(input.User.Id, input.IdempotencyKey, cancellationToken, isConcurrency: true);
        }
        catch (DbUpdateException ex)
            when (_context.IsUniqueViolation(ex, "UX_Orders_UserId_CheckoutIdempotencyKey"))
        {
            _context.ClearTrackedChanges();
            return await ReloadAndReplayExistingOrderAsync(input.User.Id, input.IdempotencyKey, cancellationToken, isConcurrency: false);
        }

        // 8. If COD, return success immediately
        if (input.PaymentMethod == PaymentMethod.COD)
        {
            return Result.Success(CheckoutMappingExtensions.MapToDto(order));
        }

        // 9. If Stripe, invoke Stripe gateway outside database transaction
        return await InitiateStripeCheckoutAsync(order, stripeAttempt!, cancellationToken);
    }

    public async Task<Result<CheckoutOrderDto>> ReplayExistingOrderAsync(
        Order existingOrder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(existingOrder);

        if (existingOrder.PaymentMethod == PaymentMethod.COD)
        {
            return Result.Success(CheckoutMappingExtensions.MapToDto(existingOrder));
        }

        if (existingOrder.PaymentMethod == PaymentMethod.Stripe)
        {
            var paymentCheck = ValidatePaymentMethodAvailability(existingOrder.PaymentMethod);
            if (paymentCheck.IsFailure)
            {
                return Result.Failure<CheckoutOrderDto>(paymentCheck.Error);
            }

            var attempt = existingOrder.PaymentAttempts.OrderByDescending(p => p.CreatedAt).FirstOrDefault();
            if (attempt is null)
            {
                return Result.Failure<CheckoutOrderDto>(Error.Conflict(
                    "Checkout.IdempotencyConflict",
                    "Payment attempt not found for existing order."));
            }

            if (attempt.Status == PaymentAttemptStatus.Succeeded)
            {
                return Result.Success(CheckoutMappingExtensions.MapToDto(existingOrder, null));
            }

            if (attempt.Status == PaymentAttemptStatus.Pending)
            {
                var stripeGateway = _stripeGateway!;
                Result<StripeCheckoutSessionResult> sessionResult;
                try
                {
                    sessionResult = await stripeGateway.EnsureCheckoutSessionAsync(
                        new CreateStripeCheckoutSessionRequest(
                            existingOrder.Id,
                            attempt.Id,
                            existingOrder.OrderNumber,
                            attempt.Amount,
                            attempt.Currency,
                            existingOrder.CustomerEmail,
                            attempt.IdempotencyKey,
                            attempt.ExpiresAt
                        ),
                        attempt.ProviderReference,
                        cancellationToken
                    );
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    return Result.Failure<CheckoutOrderDto>(Error.Unavailable(
                        "Payment.GatewayUnavailable",
                        "Stripe payment gateway is temporarily unavailable. Please retry."));
                }

                if (sessionResult.IsFailure)
                {
                    return Result.Failure<CheckoutOrderDto>(sessionResult.Error);
                }

                if (string.IsNullOrWhiteSpace(attempt.ProviderReference))
                {
                    var setRefResult = attempt.SetProviderReference(sessionResult.Value.SessionId);
                    if (setRefResult.IsFailure)
                    {
                        return Result.Failure<CheckoutOrderDto>(setRefResult.Error);
                    }

                    await _context.SaveChangesAsync(cancellationToken);
                }

                var paymentAction = new PaymentActionDto(
                    Type: "Redirect",
                    Url: sessionResult.Value.Url,
                    ExpiresAt: sessionResult.Value.ExpiresAt
                );

                return Result.Success(CheckoutMappingExtensions.MapToDto(existingOrder, paymentAction));
            }

            return Result.Failure<CheckoutOrderDto>(Error.Conflict(
                "Checkout.IdempotencyConflict",
                "The previous payment attempt has ended."));
        }

        return Result.Success(CheckoutMappingExtensions.MapToDto(existingOrder));
    }

    private async Task<Result<CheckoutOrderDto>> InitiateStripeCheckoutAsync(
        Order order,
        PaymentAttempt attempt,
        CancellationToken cancellationToken)
    {
        Result<StripeCheckoutSessionResult> sessionResult;
        try
        {
            sessionResult = await _stripeGateway!.EnsureCheckoutSessionAsync(
                new CreateStripeCheckoutSessionRequest(
                    order.Id,
                    attempt.Id,
                    order.OrderNumber,
                    attempt.Amount,
                    attempt.Currency,
                    order.CustomerEmail,
                    attempt.IdempotencyKey,
                    attempt.ExpiresAt
                ),
                attempt.ProviderReference,
                cancellationToken
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Result.Failure<CheckoutOrderDto>(Error.Unavailable(
                "Payment.GatewayUnavailable",
                "Stripe payment gateway is temporarily unavailable. Please retry."));
        }

        if (sessionResult.IsFailure)
        {
            // Order and Pending payment attempt are already saved in DB; return 503 so client can retry with same key
            return Result.Failure<CheckoutOrderDto>(sessionResult.Error);
        }

        var setRefResult = attempt.SetProviderReference(sessionResult.Value.SessionId);
        if (setRefResult.IsFailure)
        {
            return Result.Failure<CheckoutOrderDto>(setRefResult.Error);
        }

        await _context.SaveChangesAsync(cancellationToken);

        var paymentAction = new PaymentActionDto(
            Type: "Redirect",
            Url: sessionResult.Value.Url,
            ExpiresAt: sessionResult.Value.ExpiresAt
        );

        return Result.Success(CheckoutMappingExtensions.MapToDto(order, paymentAction));
    }

    private async Task<Result<CheckoutOrderDto>> ReloadAndReplayExistingOrderAsync(
        Guid userId,
        string normalizedKey,
        CancellationToken cancellationToken,
        bool isConcurrency)
    {
        var replayedOrder = await _context.Orders
            .Include(o => o.Items)
            .Include(o => o.PaymentAttempts)
            .FirstOrDefaultAsync(o => o.UserId == userId && o.CheckoutIdempotencyKey == normalizedKey, cancellationToken);

        if (replayedOrder is not null)
        {
            return await ReplayExistingOrderAsync(replayedOrder, cancellationToken);
        }

        if (isConcurrency)
        {
            return Result.Failure<CheckoutOrderDto>(Error.Conflict(
                "Checkout.InventoryChanged",
                "Inventory or cart version changed during checkout. Please try again."));
        }

        return Result.Failure<CheckoutOrderDto>(Error.Conflict(
            "Checkout.IdempotencyConflict",
            "An idempotency conflict occurred during checkout."));
    }
}
