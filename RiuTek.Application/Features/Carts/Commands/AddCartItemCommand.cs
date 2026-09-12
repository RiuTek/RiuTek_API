using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Carts.Common;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Features.Carts.Commands;

public record AddCartItemCommand(Guid ProductId, int Quantity) : IRequest<Result<CartDto>>;

public class AddCartItemCommandValidator : AbstractValidator<AddCartItemCommand>
{
    public AddCartItemCommandValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("ProductId must not be empty.");

        RuleFor(x => x.Quantity)
            .InclusiveBetween(1, 99).WithMessage("Quantity must be between 1 and 99.");
    }
}

public class AddCartItemCommandHandler : IRequestHandler<AddCartItemCommand, Result<CartDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public AddCartItemCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<CartDto>> Handle(
        AddCartItemCommand request,
        CancellationToken cancellationToken)
    {
        var authResult = await CartAuthHelper.ValidateActiveUserAsync(_currentUserService, _context, cancellationToken);
        if (authResult.IsFailure)
        {
            return authResult.Error;
        }

        var userId = authResult.Value;

        // Query target product catalog information
        var product = await _context.Products
            .AsNoTracking()
            .Where(p => p.Id == request.ProductId)
            .Select(p => new { p.Id, p.IsActive, p.StockQuantity })
            .FirstOrDefaultAsync(cancellationToken);

        if (product is null)
        {
            return Error.NotFound("Cart.ProductNotFound", "Product was not found.");
        }

        if (!product.IsActive)
        {
            return Error.Conflict("Cart.ProductInactive", "Product is not active and cannot be added to the cart.");
        }

        if (product.StockQuantity <= 0)
        {
            return Error.Conflict("Cart.InsufficientStock", "Product is out of stock.");
        }

        // Load tracked Cart for user
        var cart = await _context.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (cart is null)
        {
            if (request.Quantity > product.StockQuantity)
            {
                return Error.Conflict(
                    "Cart.InsufficientStock",
                    $"Requested quantity ({request.Quantity}) exceeds available stock ({product.StockQuantity}).");
            }

            cart = new Cart(userId);
            cart.AddItem(request.ProductId, request.Quantity);
            _context.Carts.Add(cart);
        }
        else
        {
            var existingItem = cart.Items.FirstOrDefault(i => i.ProductId == request.ProductId);
            if (existingItem is not null)
            {
                var resultingQuantity = existingItem.Quantity + request.Quantity;
                if (resultingQuantity > 99)
                {
                    return Error.Validation(
                        "Cart.MaxQuantityExceeded",
                        "Total item quantity in cart cannot exceed 99.");
                }

                if (resultingQuantity > product.StockQuantity)
                {
                    return Error.Conflict(
                        "Cart.InsufficientStock",
                        $"Total requested quantity ({resultingQuantity}) exceeds available stock ({product.StockQuantity}).");
                }
            }
            else
            {
                if (cart.Items.Count >= 50)
                {
                    return Error.Validation(
                        "Cart.MaxItemsExceeded",
                        "Cart cannot contain more than 50 distinct items.");
                }

                if (request.Quantity > product.StockQuantity)
                {
                    return Error.Conflict(
                        "Cart.InsufficientStock",
                        $"Requested quantity ({request.Quantity}) exceeds available stock ({product.StockQuantity}).");
                }
            }

            cart.AddItem(request.ProductId, request.Quantity);
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict(
                "Cart.ConcurrencyConflict",
                "The cart was modified by another request. Please refresh and try again.");
        }
        catch (DbUpdateException ex) when (_context.IsUniqueViolation(ex, "IX_Carts_UserId"))
        {
            return Error.Conflict(
                "Cart.DuplicateCartCreation",
                "Cart for this user is currently being initialized by a concurrent request.");
        }

        var productIds = cart.Items.Select(i => i.ProductId);
        var productsMap = await CartResponseBuilder.GetProductsMapAsync(_context, productIds, cancellationToken);

        return Result<CartDto>.Success(CartResponseBuilder.Build(cart, productsMap));
    }
}
