using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Orders.Common;
using RiuTek.Application.Features.PCBuilds.Common;
using RiuTek.Core.Common;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Orders.Commands;

public record BuyNowPCBuildCommand(
    Guid PCBuildId,
    Guid AddressId,
    PaymentMethod PaymentMethod,
    string? Notes,
    string IdempotencyKey)
    : IRequest<Result<CheckoutOrderDto>>;

public class BuyNowPCBuildCommandValidator : AbstractValidator<BuyNowPCBuildCommand>
{
    public BuyNowPCBuildCommandValidator()
    {
        RuleFor(x => x.PCBuildId)
            .NotEmpty()
            .WithMessage("PCBuildId is required.");

        RuleFor(x => x.AddressId)
            .NotEmpty()
            .WithMessage("AddressId is required.");

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

public class BuyNowPCBuildCommandHandler : IRequestHandler<BuyNowPCBuildCommand, Result<CheckoutOrderDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly CheckoutOrderProcessor _orderProcessor;

    public BuyNowPCBuildCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        CheckoutOrderProcessor orderProcessor)
    {
        _context = context;
        _currentUserService = currentUserService;
        _orderProcessor = orderProcessor;
    }

    public async Task<Result<CheckoutOrderDto>> Handle(
        BuyNowPCBuildCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Authenticate active customer
        var authResult = await PCBuildAuthHelper.ValidateActiveCustomerAsync(_currentUserService, _context, cancellationToken);
        if (authResult.IsFailure)
        {
            return Result.Failure<CheckoutOrderDto>(authResult.Error);
        }

        var customer = authResult.Value;

        // 2. Validate Payment Method availability before any mutations
        var paymentCheck = _orderProcessor.ValidatePaymentMethodAvailability(request.PaymentMethod);
        if (paymentCheck.IsFailure)
        {
            return Result.Failure<CheckoutOrderDto>(paymentCheck.Error);
        }

        // 3. Normalize idempotency key
        var normalizedKey = request.IdempotencyKey.Trim();

        // 4. Query existing Order by (UserId, CheckoutIdempotencyKey) before reading Address or PCBuild
        var existingOrder = await _context.Orders
            .Include(o => o.Items)
            .Include(o => o.PaymentAttempts)
            .FirstOrDefaultAsync(o => o.UserId == customer.Id && o.CheckoutIdempotencyKey == normalizedKey, cancellationToken);

        if (existingOrder is not null)
        {
            return await _orderProcessor.ReplayExistingOrderAsync(existingOrder, cancellationToken);
        }

        // 5. Query address by (AddressId, UserId)
        var address = await _context.UserAddresses
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.AddressId && a.UserId == customer.Id, cancellationToken);

        if (address is null)
        {
            return Result.Failure<CheckoutOrderDto>(Error.NotFound(
                "Checkout.AddressNotFound",
                "Shipping address was not found."));
        }

        // 6. Query saved PC Build by (PCBuildId, UserId) with Items
        var pcBuild = await _context.PCBuilds
            .AsNoTracking()
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == request.PCBuildId && b.UserId == customer.Id, cancellationToken);

        if (pcBuild is null)
        {
            return Result.Failure<CheckoutOrderDto>(Error.NotFound(
                "PCBuild.NotFound",
                $"PC build with Id '{request.PCBuildId}' was not found."));
        }

        // 7. Validate build is not empty
        if (pcBuild.Items.Count == 0)
        {
            return Result.Failure<CheckoutOrderDto>(Error.Conflict(
                "PCBuild.EmptyBuild",
                "Cấu hình PC không có linh kiện nào."));
        }

        // 8. Fetch current Products as tracked entities (processor updates stock)
        var productIds = pcBuild.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _context.Products
            .Where(p => productIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        var productMap = products.ToDictionary(p => p.Id);
        if (productIds.Any(id => !productMap.ContainsKey(id)))
        {
            return Result.Failure<CheckoutOrderDto>(Error.NotFound(
                "Checkout.ProductNotFound",
                "One or more products in the PC build were not found."));
        }

        // 9. Build lines deterministically ordered by ComponentType, then ProductId
        var orderedItems = pcBuild.Items
            .OrderBy(i => i.ComponentType)
            .ThenBy(i => i.ProductId)
            .ToList();

        var lines = orderedItems.Select(item => new CheckoutOrderLine(
            Product: productMap[item.ProductId],
            Quantity: item.Quantity,
            PCBuildId: pcBuild.Id
        )).ToList();

        // 10. Delegate to CheckoutOrderProcessor with CartToClear: null
        var input = new PlaceCheckoutOrderInput(
            User: customer,
            Address: address,
            PaymentMethod: request.PaymentMethod,
            Notes: request.Notes,
            IdempotencyKey: normalizedKey,
            Lines: lines,
            CartToClear: null
        );

        return await _orderProcessor.PlaceOrderAsync(input, cancellationToken);
    }
}
