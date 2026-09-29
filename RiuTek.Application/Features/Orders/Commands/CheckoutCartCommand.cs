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

    public CheckoutCartCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
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

        if (request.PaymentMethod != PaymentMethod.COD)
        {
            return Result.Failure<CheckoutOrderDto>(Error.Validation(
                "Checkout.PaymentMethodNotAvailable",
                "Only COD payment method is available in this phase."));
        }

        // 2. Normalize idempotency key
        var normalizedKey = request.IdempotencyKey.Trim();

        // 3. Before reading cart, query Order by (UserId, CheckoutIdempotencyKey)
        var existingOrder = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.UserId == user.Id && o.CheckoutIdempotencyKey == normalizedKey, cancellationToken);

        if (existingOrder is not null)
        {
            return Result.Success(CheckoutMappingExtensions.MapToDto(existingOrder));
        }

        // 4. Query address by (AddressId, UserId)
        var address = await _context.UserAddresses
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.AddressId && a.UserId == user.Id, cancellationToken);

        if (address is null)
        {
            return Result.Failure<CheckoutOrderDto>(Error.NotFound(
                "Checkout.AddressNotFound",
                "Shipping address was not found."));
        }

        // 5. Load tracked Cart + Items, check non-empty and ExpectedCartVersion
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

        // 6. Load all products of cart as tracked entities in a single query
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

        // 7. Validate all items before any mutation
        foreach (var cartItem in cart.Items)
        {
            if (!productMap.TryGetValue(cartItem.ProductId, out var product))
            {
                return Result.Failure<CheckoutOrderDto>(Error.NotFound(
                    "Checkout.ProductNotFound",
                    $"Product {cartItem.ProductId} was not found."));
            }

            if (!product.IsActive)
            {
                return Result.Failure<CheckoutOrderDto>(Error.Conflict(
                    "Checkout.ProductInactive",
                    $"Product '{product.Name}' is no longer active."));
            }

            if (product.Price <= 0)
            {
                return Result.Failure<CheckoutOrderDto>(Error.Conflict(
                    "Checkout.InvalidPrice",
                    $"Product '{product.Name}' has an invalid price."));
            }

            if (product.StockQuantity < cartItem.Quantity)
            {
                return Result.Failure<CheckoutOrderDto>(Error.Conflict(
                    "Checkout.InsufficientStock",
                    $"Insufficient stock for product '{product.Name}'. Available: {product.StockQuantity}, Requested: {cartItem.Quantity}."));
            }
        }

        // 8. Create Order COD
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
            paymentMethod: PaymentMethod.COD,
            notes: notes
        );

        foreach (var cartItem in cart.Items)
        {
            var product = productMap[cartItem.ProductId];
            var addResult = order.AddItem(
                productId: product.Id,
                productName: product.Name,
                productSku: product.Sku,
                unitPrice: product.Price,
                quantity: cartItem.Quantity
            );

            if (addResult.IsFailure)
            {
                return Result.Failure<CheckoutOrderDto>(addResult.Error);
            }
        }

        // 9. Decrement Product.StockQuantity for each item
        foreach (var cartItem in cart.Items)
        {
            var product = productMap[cartItem.ProductId];
            product.StockQuantity -= cartItem.Quantity;
        }

        // 10. Clear cart
        cart.Clear();

        // 11. Add Order and single SaveChangesAsync
        _context.Orders.Add(order);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return Result.Success(CheckoutMappingExtensions.MapToDto(order));
        }
        catch (DbUpdateConcurrencyException)
        {
            var replayedOrder = await _context.Orders
                .AsNoTracking()
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.UserId == user.Id && o.CheckoutIdempotencyKey == normalizedKey, cancellationToken);

            if (replayedOrder is not null)
            {
                return Result.Success(CheckoutMappingExtensions.MapToDto(replayedOrder));
            }

            return Result.Failure<CheckoutOrderDto>(Error.Conflict(
                "Checkout.InventoryChanged",
                "Inventory or cart version changed during checkout. Please try again."));
        }
        catch (DbUpdateException ex)
            when (_context.IsUniqueViolation(
                ex,
                "UX_Orders_UserId_CheckoutIdempotencyKey"))
        {
            var replayedOrder = await _context.Orders
                .AsNoTracking()
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.UserId == user.Id && o.CheckoutIdempotencyKey == normalizedKey, cancellationToken);

            if (replayedOrder is not null)
            {
                return Result.Success(CheckoutMappingExtensions.MapToDto(replayedOrder));
            }

            return Result.Failure<CheckoutOrderDto>(Error.Conflict(
                "Checkout.IdempotencyConflict",
                "An idempotency conflict occurred during checkout."));
        }
    }
}
