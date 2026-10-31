using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Orders.Common;
using RiuTek.Core.Common;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Orders.Commands;

public record BuyNowProductCommand(
    Guid ProductId,
    Guid AddressId,
    int Quantity,
    PaymentMethod PaymentMethod,
    string? Notes,
    string IdempotencyKey)
    : IRequest<Result<CheckoutOrderDto>>;

public class BuyNowProductCommandValidator : AbstractValidator<BuyNowProductCommand>
{
    public BuyNowProductCommandValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty()
            .WithMessage("ProductId is required.");

        RuleFor(x => x.AddressId)
            .NotEmpty()
            .WithMessage("AddressId is required.");

        RuleFor(x => x.Quantity)
            .InclusiveBetween(1, 99)
            .WithMessage("Quantity must be between 1 and 99.");

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

public class BuyNowProductCommandHandler : IRequestHandler<BuyNowProductCommand, Result<CheckoutOrderDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly CheckoutOrderProcessor _orderProcessor;

    public BuyNowProductCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        CheckoutOrderProcessor orderProcessor)
    {
        _context = context;
        _currentUserService = currentUserService;
        _orderProcessor = orderProcessor;
    }

    public async Task<Result<CheckoutOrderDto>> Handle(
        BuyNowProductCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Authenticate active user
        var authResult = await OrderAuthHelper.ValidateActiveUserAsync(_currentUserService, _context, cancellationToken);
        if (authResult.IsFailure)
        {
            return Result.Failure<CheckoutOrderDto>(authResult.Error);
        }

        var user = authResult.Value;

        // 2. Validate Payment Method availability
        var paymentCheck = _orderProcessor.ValidatePaymentMethodAvailability(request.PaymentMethod);
        if (paymentCheck.IsFailure)
        {
            return Result.Failure<CheckoutOrderDto>(paymentCheck.Error);
        }

        // 3. Normalize idempotency key
        var normalizedKey = request.IdempotencyKey.Trim();

        // 4. Before reading source, query existing Order by (UserId, CheckoutIdempotencyKey)
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

        // 6. Query tracked Product by ProductId
        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken);

        if (product is null)
        {
            return Result.Failure<CheckoutOrderDto>(Error.NotFound(
                "Checkout.ProductNotFound",
                $"Product {request.ProductId} was not found."));
        }

        // 7. Create single line (PCBuildId = null) and delegate to processor with SourceCart = null
        var line = new CheckoutOrderLine(product, request.Quantity, PCBuildId: null);
        var input = new PlaceCheckoutOrderInput(
            User: user,
            Address: address,
            PaymentMethod: request.PaymentMethod,
            Notes: request.Notes,
            IdempotencyKey: normalizedKey,
            Lines: [line],
            SourceCart: null
        );

        return await _orderProcessor.PlaceOrderAsync(input, cancellationToken);
    }
}
