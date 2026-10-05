using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Orders.Common;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Orders.Commands;

public record CheckoutCartCommand(
    Guid AddressId,
    int ExpectedCartVersion,
    PaymentMethod PaymentMethod,
    string? Notes,
    string IdempotencyKey
) : IRequest<Result<CheckoutOrderDto>>;

public class CheckoutCartCommandValidator : AbstractValidator<CheckoutCartCommand>
{
    public CheckoutCartCommandValidator()
    {
        RuleFor(x => x.AddressId)
            .NotEmpty()
            .WithMessage("AddressId is required.");

        RuleFor(x => x.ExpectedCartVersion)
            .GreaterThanOrEqualTo(1)
            .WithMessage("ExpectedCartVersion must be at least 1.");

        RuleFor(x => x.IdempotencyKey)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Idempotency-Key header is required.")
            .Must(k => k.Trim().Length is >= 16 and <= 128)
            .WithMessage("Idempotency-Key must be between 16 and 128 characters.");

        RuleFor(x => x.Notes)
            .Must(n => n == null || n.Trim().Length <= 1000)
            .WithMessage("Notes cannot exceed 1000 characters.");
    }
}

public class CheckoutCartCommandHandler : IRequestHandler<CheckoutCartCommand, Result<CheckoutOrderDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IStripePaymentGateway? _stripeGateway;

    public CheckoutCartCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IStripePaymentGateway? stripeGateway = null)
    {
        _context = context;
        _currentUserService = currentUserService;
        _stripeGateway = stripeGateway;
    }

    public async Task<Result<CheckoutOrderDto>> Handle(
        CheckoutCartCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Authenticate active user
        var authResult = await OrderAuthHelper.ValidateActiveUserAsync(_currentUserService, _context, cancellationToken);
        if (authResult.IsFailure)
        {
            return Result.Failure<CheckoutOrderDto>(authResult.Error);
        }

        var user = authResult.Value;

        // 2. Validate Payment Method
        if (request.PaymentMethod == PaymentMethod.Stripe)
        {
            if (_stripeGateway is null || !_stripeGateway.IsEnabled)
            {
                return Result.Failure<CheckoutOrderDto>(Error.Validation(
                    "Checkout.PaymentMethodNotAvailable",
                    "Stripe payment is not available at this time."));
            }
        }
        else if (request.PaymentMethod != PaymentMethod.COD)
        {
            return Result.Failure<CheckoutOrderDto>(Error.Validation(
                "Checkout.PaymentMethodNotAvailable",
                "Payment method is not available."));
        }

        // 3. Normalize idempotency key
        var normalizedKey = request.IdempotencyKey.Trim();

        // 4. Before reading cart, query existing Order by (UserId, CheckoutIdempotencyKey)
        var existingOrder = await _context.Orders
            .Include(o => o.Items)
            .Include(o => o.PaymentAttempts)
            .FirstOrDefaultAsync(o => o.UserId == user.Id && o.CheckoutIdempotencyKey == normalizedKey, cancellationToken);

        if (existingOrder is not null)
        {
            return await HandleExistingOrderAsync(existingOrder, cancellationToken);
        }

        // 5. Query address by (AddressId, UserId)
        var address = await _context.UserAddresses
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.AddressId && a.UserId == user.Id, cancellationToken);

        if (address is null)
        {
            return Result.Failure<CheckoutOrderDto>(Error.NotFound(
                "Checkout.AddressNotFound",
                "Shipping address was not found."));
        }

        // 6. Load tracked Cart + Items, check non-empty and ExpectedCartVersion
        var cart = await _context.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.UserId == user.Id, cancellationToken);

        if (cart is null || cart.Items.Count == 0)
        {
            return Result.Failure<CheckoutOrderDto>(Error.Conflict(
                "Checkout.EmptyCart",
                "Cart is empty."));
        }

        if (cart.Version != request.ExpectedCartVersion)
        {
            return Result.Failure<CheckoutOrderDto>(Error.Conflict(
                "Checkout.CartChanged",
                "Cart has been modified. Please refresh and try again."));
        }

        // 7. Load all products of cart as tracked entities
        var productIds = cart.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _context.Products
            .Where(p => productIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        if (products.Count != productIds.Count)
        {
            return Result.Failure<CheckoutOrderDto>(Error.NotFound(
                "Checkout.ProductNotFound",
                "One or more products in the cart were not found."));
        }

        var productMap = products.ToDictionary(p => p.Id);

        // 8. Validate all items before any mutation
        var validationResult = ValidateCartItems(cart, productMap);
        if (validationResult.IsFailure)
        {
            return Result.Failure<CheckoutOrderDto>(validationResult.Error);
        }

        // 9. Build Order entity and items
        var orderResult = BuildOrder(request, user, address, normalizedKey, cart, productMap);
        if (orderResult.IsFailure)
        {
            return Result.Failure<CheckoutOrderDto>(orderResult.Error);
        }
        var order = orderResult.Value;

        // 10. Decrement Product.StockQuantity for each item
        foreach (var cartItem in cart.Items)
        {
            var product = productMap[cartItem.ProductId];
            product.StockQuantity -= cartItem.Quantity;
        }

        // 11. Clear cart
        cart.Clear();

        // 12. Create PaymentAttempt for online payment (Stripe) within the initial DB transaction
        PaymentAttempt? stripeAttempt = null;
        if (request.PaymentMethod == PaymentMethod.Stripe)
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

        // 13. Add Order and execute single SaveChangesAsync
        _context.Orders.Add(order);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _context.ClearTrackedChanges();
            return await ReloadAndReplayExistingOrderAsync(user.Id, normalizedKey, cancellationToken, isConcurrency: true);
        }
        catch (DbUpdateException ex)
            when (_context.IsUniqueViolation(ex, "UX_Orders_UserId_CheckoutIdempotencyKey"))
        {
            _context.ClearTrackedChanges();
            return await ReloadAndReplayExistingOrderAsync(user.Id, normalizedKey, cancellationToken, isConcurrency: false);
        }

        // 14. If COD, return success immediately
        if (request.PaymentMethod == PaymentMethod.COD)
        {
            return Result.Success(CheckoutMappingExtensions.MapToDto(order));
        }

        // 15. If Stripe, invoke Stripe gateway outside the database transaction
        return await InitiateStripeCheckoutAsync(order, stripeAttempt!, cancellationToken);
    }

    private static Result ValidateCartItems(Cart cart, Dictionary<Guid, Product> productMap)
    {
        foreach (var cartItem in cart.Items)
        {
            if (!productMap.TryGetValue(cartItem.ProductId, out var product))
            {
                return Result.Failure(Error.NotFound(
                    "Checkout.ProductNotFound",
                    $"Product {cartItem.ProductId} was not found."));
            }

            if (!product.IsActive)
            {
                return Result.Failure(Error.Conflict(
                    "Checkout.ProductInactive",
                    $"Product '{product.Name}' is no longer active."));
            }

            if (product.Price <= 0)
            {
                return Result.Failure(Error.Conflict(
                    "Checkout.InvalidPrice",
                    $"Product '{product.Name}' has an invalid price."));
            }

            if (product.StockQuantity < cartItem.Quantity)
            {
                return Result.Failure(Error.Conflict(
                    "Checkout.InsufficientStock",
                    $"Insufficient stock for product '{product.Name}'. Available: {product.StockQuantity}, Requested: {cartItem.Quantity}."));
            }
        }

        return Result.Success();
    }

    private static Result<Order> BuildOrder(
        CheckoutCartCommand request,
        User user,
        UserAddress address,
        string normalizedKey,
        Cart cart,
        Dictionary<Guid, Product> productMap)
    {
        var orderNumber = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        var customerName = !string.IsNullOrWhiteSpace(address.ReceiverName) ? address.ReceiverName : user.FullName;
        var customerPhone = !string.IsNullOrWhiteSpace(address.PhoneNumber) ? address.PhoneNumber : (user.PhoneNumber ?? "0000000000");
        var customerEmail = user.Email;
        var formattedShippingAddress = CheckoutMappingExtensions.FormatShippingAddress(address);
        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();

        var order = new Order(
            orderNumber: orderNumber,
            userId: user.Id,
            checkoutIdempotencyKey: normalizedKey,
            customerName: customerName,
            customerEmail: customerEmail,
            customerPhone: customerPhone,
            shippingAddress: formattedShippingAddress,
            paymentMethod: request.PaymentMethod,
            notes: notes
        );

        foreach (var cartItem in cart.Items)
        {
            var product = productMap[cartItem.ProductId];
            var addItemResult = order.AddItem(
                productId: product.Id,
                productName: product.Name,
                productSku: product.Sku,
                unitPrice: product.Price,
                quantity: cartItem.Quantity
            );

            if (addItemResult.IsFailure)
            {
                return Result.Failure<Order>(addItemResult.Error);
            }
        }

        return Result.Success(order);
    }

    private async Task<Result<CheckoutOrderDto>> InitiateStripeCheckoutAsync(
        Order order,
        PaymentAttempt attempt,
        CancellationToken cancellationToken)
    {
        var sessionResult = await _stripeGateway!.EnsureCheckoutSessionAsync(
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

    private async Task<Result<CheckoutOrderDto>> HandleExistingOrderAsync(
        Order existingOrder,
        CancellationToken cancellationToken)
    {
        if (existingOrder.PaymentMethod == PaymentMethod.COD)
        {
            return Result.Success(CheckoutMappingExtensions.MapToDto(existingOrder));
        }

        if (existingOrder.PaymentMethod == PaymentMethod.Stripe)
        {
            if (_stripeGateway is null || !_stripeGateway.IsEnabled)
            {
                return Result.Failure<CheckoutOrderDto>(Error.Validation(
                    "Checkout.PaymentMethodNotAvailable",
                    "Stripe payment is not available at this time."));
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
                var sessionResult = await _stripeGateway.EnsureCheckoutSessionAsync(
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
            return await HandleExistingOrderAsync(replayedOrder, cancellationToken);
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
