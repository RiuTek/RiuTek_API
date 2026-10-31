using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Orders.Common;
using RiuTek.Application.Features.Products.Common;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Orders.Commands;

public record CheckoutCartCommand(
    Guid AddressId,
    int ExpectedCartVersion,
    PaymentMethod PaymentMethod,
    string? Notes,
    string IdempotencyKey,
    IReadOnlyList<Guid>? SelectedProductIds = null
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

        When(x => x.SelectedProductIds != null, () =>
        {
            RuleFor(x => x.SelectedProductIds!)
                .Must(ids => ids.Count > 0)
                .WithMessage("SelectedProductIds cannot be empty.")
                .Must(ids => ids.Count <= 50)
                .WithMessage("SelectedProductIds cannot contain more than 50 items.")
                .Must(ids => ids.All(id => id != Guid.Empty))
                .WithMessage("SelectedProductIds cannot contain empty GUID.")
                .Must(ids => ids.Distinct().Count() == ids.Count)
                .WithMessage("SelectedProductIds cannot contain duplicate product IDs.");
        });
    }
}

public class CheckoutCartCommandHandler : IRequestHandler<CheckoutCartCommand, Result<CheckoutOrderDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly CheckoutOrderProcessor _orderProcessor;

    public CheckoutCartCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        CheckoutOrderProcessor orderProcessor)
    {
        _context = context;
        _currentUserService = currentUserService;
        _orderProcessor = orderProcessor;
    }

    public CheckoutCartCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        CheckoutOrderProcessor orderProcessor,
        IStripePaymentGateway? stripeGateway)
        : this(context, currentUserService, orderProcessor)
    {
    }

    public CheckoutCartCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IStripePaymentGateway? stripeGateway = null)
        : this(context, currentUserService, new CheckoutOrderProcessor(context, stripeGateway))
    {
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
        var paymentCheck = _orderProcessor.ValidatePaymentMethodAvailability(request.PaymentMethod);
        if (paymentCheck.IsFailure)
        {
            return Result.Failure<CheckoutOrderDto>(paymentCheck.Error);
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
            return await _orderProcessor.ReplayExistingOrderAsync(existingOrder, cancellationToken);
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
            if (request.SelectedProductIds is not null)
            {
                return Result.Failure<CheckoutOrderDto>(Error.Conflict(
                    "Checkout.SelectionChanged",
                    "One or more selected products are no longer in the cart."));
            }

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

        // 7. Resolve selection
        var selectionResult = CheckoutSelectionHelper.ResolveSelectedItems(cart, request.SelectedProductIds);
        if (selectionResult.IsFailure)
        {
            return Result.Failure<CheckoutOrderDto>(selectionResult.Error);
        }

        var selectedItems = selectionResult.Value;
        if (selectedItems.Count == 0)
        {
            return Result.Failure<CheckoutOrderDto>(Error.Conflict(
                "Checkout.EmptyCart",
                "Cart is empty."));
        }

        // 8. Load products of selected items as tracked entities
        var productIds = selectedItems.Select(i => i.ProductId).Distinct().ToList();
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

        // 9. Validate selected items before any mutation
        var validationResult = ValidateCartItems(selectedItems, productMap);
        if (validationResult.IsFailure)
        {
            return Result.Failure<CheckoutOrderDto>(validationResult.Error);
        }

        // 10. Convert selected items to CheckoutOrderLine (PCBuildId = null for cart checkout)
        var lines = selectedItems
            .Select(i => new CheckoutOrderLine(productMap[i.ProductId], i.Quantity, PCBuildId: null))
            .ToList();

        // 11. Delegate order creation and execution to shared processor
        var input = new PlaceCheckoutOrderInput(
            User: user,
            Address: address,
            PaymentMethod: request.PaymentMethod,
            Notes: request.Notes,
            IdempotencyKey: normalizedKey,
            Lines: lines,
            SourceCart: cart
        );

        return await _orderProcessor.PlaceOrderAsync(input, cancellationToken);
    }

    private static Result ValidateCartItems(IReadOnlyCollection<CartItem> items, Dictionary<Guid, Product> productMap)
    {
        foreach (var cartItem in items)
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
}
