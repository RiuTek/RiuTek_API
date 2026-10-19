using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Carts.Common;
using RiuTek.Application.Features.PCBuilds.Common;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Features.Carts.Commands;

public record AddPCBuildToCartCommand(Guid PCBuildId) : IRequest<Result<CartDto>>;

public class AddPCBuildToCartCommandValidator : AbstractValidator<AddPCBuildToCartCommand>
{
    public AddPCBuildToCartCommandValidator()
    {
        RuleFor(x => x.PCBuildId)
            .NotEmpty().WithMessage("PCBuildId must not be empty.");
    }
}

public class AddPCBuildToCartCommandHandler : IRequestHandler<AddPCBuildToCartCommand, Result<CartDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public AddPCBuildToCartCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<CartDto>> Handle(
        AddPCBuildToCartCommand request,
        CancellationToken cancellationToken)
    {
        var authResult = await PCBuildAuthHelper.ValidateActiveCustomerAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (authResult.IsFailure)
        {
            return Result.Failure<CartDto>(authResult.Error);
        }

        var customer = authResult.Value;

        var pcBuild = await _context.PCBuilds
            .AsNoTracking()
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == request.PCBuildId && b.UserId == customer.Id, cancellationToken);

        if (pcBuild is null)
        {
            return Result.Failure<CartDto>(Error.NotFound(
                "PCBuild.NotFound",
                $"PC build with Id '{request.PCBuildId}' was not found."));
        }

        if (pcBuild.Items.Count == 0)
        {
            return Result.Failure<CartDto>(Error.Conflict(
                "PCBuild.EmptyBuild",
                "Cấu hình PC không có linh kiện nào."));
        }

        var buildProductIds = pcBuild.Items.Select(i => i.ProductId).Distinct().ToList();

        var products = await _context.Products
            .AsNoTracking()
            .Where(p => buildProductIds.Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                p.ComponentType,
                p.IsActive,
                p.StockQuantity
            })
            .ToListAsync(cancellationToken);

        var productMap = products.ToDictionary(p => p.Id);

        foreach (var pid in buildProductIds)
        {
            if (!productMap.ContainsKey(pid))
            {
                return Result.Failure<CartDto>(Error.NotFound(
                    "Cart.ProductNotFound",
                    "One or more products in the PC build were not found."));
            }
        }

        foreach (var item in pcBuild.Items)
        {
            var product = productMap[item.ProductId];
            if (!product.IsActive)
            {
                return Result.Failure<CartDto>(Error.Conflict(
                    "Cart.ProductInactive",
                    "One or more products are inactive and cannot be added to the cart."));
            }
        }

        var cart = await _context.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.UserId == customer.Id, cancellationToken);

        var currentCartQuantities = cart?.Items.ToDictionary(i => i.ProductId, i => i.Quantity)
            ?? new Dictionary<Guid, int>();

        var buildItemQuantities = pcBuild.Items
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        foreach (var (productId, buildQty) in buildItemQuantities)
        {
            var currentQty = currentCartQuantities.GetValueOrDefault(productId, 0);
            var resultingQty = currentQty + buildQty;
            if (resultingQty > 99)
            {
                return Result.Failure<CartDto>(Error.Validation(
                    "Cart.MaxQuantityExceeded",
                    "Total item quantity in cart cannot exceed 99."));
            }
        }

        var allProductIds = currentCartQuantities.Keys.Union(buildItemQuantities.Keys).ToHashSet();
        if (allProductIds.Count > 50)
        {
            return Result.Failure<CartDto>(Error.Validation(
                "Cart.MaxItemsExceeded",
                "Cart cannot contain more than 50 distinct items."));
        }

        foreach (var (productId, buildQty) in buildItemQuantities)
        {
            var product = productMap[productId];
            var currentQty = currentCartQuantities.GetValueOrDefault(productId, 0);
            var resultingQty = currentQty + buildQty;
            if (resultingQty > product.StockQuantity)
            {
                return Result.Failure<CartDto>(Error.Conflict(
                    "Cart.InsufficientStock",
                    $"Requested quantity ({resultingQty}) exceeds available stock ({product.StockQuantity})."));
            }
        }

        if (cart is null)
        {
            cart = new Cart(customer.Id);
            _context.Carts.Add(cart);
        }

        var sortedBuildItems = pcBuild.Items
            .OrderBy(i => i.ComponentType)
            .ThenBy(i => i.ProductId)
            .ToList();

        foreach (var item in sortedBuildItems)
        {
            var addResult = cart.AddItem(item.ProductId, item.Quantity);
            if (addResult.IsFailure)
            {
                return Result.Failure<CartDto>(addResult.Error);
            }
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<CartDto>(Error.Conflict(
                "Cart.ConcurrencyConflict",
                "The cart was modified by another request. Please refresh and try again."));
        }
        catch (DbUpdateException ex) when (_context.IsUniqueViolation(ex, "IX_Carts_UserId"))
        {
            return Result.Failure<CartDto>(Error.Conflict(
                "Cart.DuplicateCartCreation",
                "Cart for this user is currently being initialized by a concurrent request."));
        }

        var cartProductIds = cart.Items.Select(i => i.ProductId);
        var productsMap = await CartResponseBuilder.GetProductsMapAsync(_context, cartProductIds, cancellationToken);

        return Result.Success(CartResponseBuilder.Build(cart, productsMap));
    }
}
