using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Orders.Common;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Orders.Queries;

public record GetCheckoutQuoteQuery(Guid AddressId) : IRequest<Result<CheckoutQuoteDto>>;

public class GetCheckoutQuoteQueryValidator : AbstractValidator<GetCheckoutQuoteQuery>
{
    public GetCheckoutQuoteQueryValidator()
    {
        RuleFor(x => x.AddressId)
            .NotEmpty()
            .WithMessage("AddressId is required.");
    }
}

public class GetCheckoutQuoteQueryHandler : IRequestHandler<GetCheckoutQuoteQuery, Result<CheckoutQuoteDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetCheckoutQuoteQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<CheckoutQuoteDto>> Handle(
        GetCheckoutQuoteQuery request,
        CancellationToken cancellationToken)
    {
        var authResult = await OrderAuthHelper.ValidateActiveUserAsync(_currentUserService, _context, cancellationToken);
        if (authResult.IsFailure)
        {
            return Result.Failure<CheckoutQuoteDto>(authResult.Error);
        }

        var user = authResult.Value;

        var address = await _context.UserAddresses
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.AddressId && a.UserId == user.Id, cancellationToken);

        if (address is null)
        {
            return Result.Failure<CheckoutQuoteDto>(Error.NotFound("Checkout.AddressNotFound", "Shipping address was not found."));
        }

        var cart = await _context.Carts
            .AsNoTracking()
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.UserId == user.Id, cancellationToken);

        var formattedShippingAddress = CheckoutMappingExtensions.FormatShippingAddress(address);

        if (cart is null || cart.Items.Count == 0)
        {
            return Result.Success(new CheckoutQuoteDto(
                CartVersion: cart?.Version ?? 0,
                AddressId: address.Id,
                ReceiverName: address.ReceiverName,
                PhoneNumber: address.PhoneNumber,
                ShippingAddress: formattedShippingAddress,
                Items: [],
                Subtotal: 0m,
                DiscountAmount: 0m,
                FinalAmount: 0m,
                CanCheckout: false,
                Issues: ["EMPTY_CART"]
            ));
        }

        var productIds = cart.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _context.Products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var quoteItems = new List<CheckoutQuoteItemDto>(cart.Items.Count);
        var issues = new List<string>();

        foreach (var item in cart.Items)
        {
            if (products.TryGetValue(item.ProductId, out var product))
            {
                var unitPrice = product.Price;
                var lineTotal = unitPrice * item.Quantity;
                var canPurchase = product.IsActive && product.Price > 0 && product.StockQuantity >= item.Quantity;

                string? issueCode = null;
                if (!product.IsActive)
                {
                    issueCode = "PRODUCT_INACTIVE";
                }
                else if (product.Price <= 0)
                {
                    issueCode = "INVALID_PRICE";
                }
                else if (product.StockQuantity <= 0)
                {
                    issueCode = "OUT_OF_STOCK";
                }
                else if (product.StockQuantity < item.Quantity)
                {
                    issueCode = "INSUFFICIENT_STOCK";
                }

                if (issueCode is not null && !issues.Contains(issueCode))
                {
                    issues.Add(issueCode);
                }

                quoteItems.Add(new CheckoutQuoteItemDto(
                    ProductId: item.ProductId,
                    Name: product.Name,
                    Sku: product.Sku,
                    ImageUrl: product.ImageUrl,
                    UnitPrice: unitPrice,
                    Quantity: item.Quantity,
                    LineTotal: lineTotal,
                    StockQuantity: product.StockQuantity,
                    IsActive: product.IsActive,
                    CanPurchase: canPurchase,
                    IssueCode: issueCode
                ));
            }
            else
            {
                const string issueCode = "PRODUCT_NOT_FOUND";
                if (!issues.Contains(issueCode))
                {
                    issues.Add(issueCode);
                }

                quoteItems.Add(new CheckoutQuoteItemDto(
                    ProductId: item.ProductId,
                    Name: "Unknown Product",
                    Sku: string.Empty,
                    ImageUrl: string.Empty,
                    UnitPrice: 0m,
                    Quantity: item.Quantity,
                    LineTotal: 0m,
                    StockQuantity: 0,
                    IsActive: false,
                    CanPurchase: false,
                    IssueCode: issueCode
                ));
            }
        }

        var sortedItems = quoteItems
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.ProductId)
            .ToList();

        var subtotal = sortedItems.Sum(i => i.LineTotal);
        var canCheckout = sortedItems.Count > 0 && sortedItems.All(i => i.CanPurchase);

        return Result.Success(new CheckoutQuoteDto(
            CartVersion: cart.Version,
            AddressId: address.Id,
            ReceiverName: address.ReceiverName,
            PhoneNumber: address.PhoneNumber,
            ShippingAddress: formattedShippingAddress,
            Items: sortedItems,
            Subtotal: subtotal,
            DiscountAmount: 0m,
            FinalAmount: subtotal,
            CanCheckout: canCheckout,
            Issues: issues
        ));
    }
}
