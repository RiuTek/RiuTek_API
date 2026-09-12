using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Carts.Common;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Carts.Commands;

public record SetCartItemQuantityCommand(Guid ProductId, int Quantity) : IRequest<Result<CartDto>>;

public class SetCartItemQuantityCommandValidator : AbstractValidator<SetCartItemQuantityCommand>
{
    public SetCartItemQuantityCommandValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("ProductId must not be empty.");

        RuleFor(x => x.Quantity)
            .InclusiveBetween(1, 99).WithMessage("Quantity must be between 1 and 99.");
    }
}

public class SetCartItemQuantityCommandHandler : IRequestHandler<SetCartItemQuantityCommand, Result<CartDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public SetCartItemQuantityCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<CartDto>> Handle(
        SetCartItemQuantityCommand request,
        CancellationToken cancellationToken)
    {
        var authResult = await CartAuthHelper.ValidateActiveUserAsync(_currentUserService, _context, cancellationToken);
        if (authResult.IsFailure)
        {
            return authResult.Error;
        }

        var userId = authResult.Value;

        var cart = await _context.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (cart is null)
        {
            return Error.NotFound("Cart.ItemNotFound", "Item was not found in cart.");
        }

        var existingItem = cart.Items.FirstOrDefault(i => i.ProductId == request.ProductId);
        if (existingItem is null)
        {
            return Error.NotFound("Cart.ItemNotFound", "Item was not found in cart.");
        }

        // Idempotent: same quantity causes no change or save
        if (existingItem.Quantity == request.Quantity)
        {
            var pIds = cart.Items.Select(i => i.ProductId);
            var pMap = await CartResponseBuilder.GetProductsMapAsync(_context, pIds, cancellationToken);
            return Result<CartDto>.Success(CartResponseBuilder.Build(cart, pMap));
        }

        // Increasing quantity requires checking product active status and stock
        if (request.Quantity > existingItem.Quantity)
        {
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
                return Error.Conflict("Cart.ProductInactive", "Product is not active and quantity cannot be increased.");
            }

            if (request.Quantity > product.StockQuantity)
            {
                return Error.Conflict(
                    "Cart.InsufficientStock",
                    $"Requested quantity ({request.Quantity}) exceeds available stock ({product.StockQuantity}).");
            }
        }
        // Decreasing quantity is allowed even if product is inactive or stock is low

        cart.SetItemQuantity(request.ProductId, request.Quantity);

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

        var productIds = cart.Items.Select(i => i.ProductId);
        var productsMap = await CartResponseBuilder.GetProductsMapAsync(_context, productIds, cancellationToken);

        return Result<CartDto>.Success(CartResponseBuilder.Build(cart, productsMap));
    }
}
