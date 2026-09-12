using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Features.Carts.Common;

public record ProductHydrationInfo(
    Guid Id,
    string Name,
    string Slug,
    string Sku,
    string ImageUrl,
    decimal Price,
    int StockQuantity,
    bool IsActive
);

public static class CartResponseBuilder
{
    public static async Task<Dictionary<Guid, ProductHydrationInfo>> GetProductsMapAsync(
        RiuTek.Application.Common.Interfaces.IApplicationDbContext context,
        IEnumerable<Guid> productIds,
        CancellationToken cancellationToken)
    {
        var distinctIds = productIds.Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            return new Dictionary<Guid, ProductHydrationInfo>();
        }

        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToDictionaryAsync(
            context.Products
                .AsNoTracking()
                .Where(p => distinctIds.Contains(p.Id))
                .Select(p => new ProductHydrationInfo(
                    p.Id,
                    p.Name,
                    p.Slug,
                    p.Sku,
                    p.ImageUrl,
                    p.Price,
                    p.StockQuantity,
                    p.IsActive
                )),
            p => p.Id,
            cancellationToken);
    }

    public static CartDto BuildEmpty()
    {
        return new CartDto(
            CartId: null,
            Version: 0,
            Items: [],
            Subtotal: 0m,
            CanCheckout: false
        );
    }

    public static CartDto Build(Cart? cart, IReadOnlyDictionary<Guid, ProductHydrationInfo> productsMap)
    {
        if (cart is null)
        {
            return BuildEmpty();
        }

        var items = new List<CartItemDto>(cart.Items.Count);

        foreach (var item in cart.Items)
        {
            if (productsMap.TryGetValue(item.ProductId, out var product))
            {
                var unitPrice = product.Price;
                var lineTotal = unitPrice * item.Quantity;
                var canPurchase = product.IsActive && product.StockQuantity >= item.Quantity;

                string? issueCode = null;
                if (!product.IsActive)
                {
                    issueCode = "INACTIVE";
                }
                else if (product.StockQuantity <= 0)
                {
                    issueCode = "OUT_OF_STOCK";
                }
                else if (product.StockQuantity < item.Quantity)
                {
                    issueCode = "INSUFFICIENT_STOCK";
                }

                items.Add(new CartItemDto(
                    ProductId: item.ProductId,
                    Name: product.Name,
                    Slug: product.Slug,
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
                items.Add(new CartItemDto(
                    ProductId: item.ProductId,
                    Name: "Unknown Product",
                    Slug: string.Empty,
                    Sku: string.Empty,
                    ImageUrl: string.Empty,
                    UnitPrice: 0m,
                    Quantity: item.Quantity,
                    LineTotal: 0m,
                    StockQuantity: 0,
                    IsActive: false,
                    CanPurchase: false,
                    IssueCode: "PRODUCT_NOT_FOUND"
                ));
            }
        }

        // Deterministic ordering: Product.Name then ProductId
        var sortedItems = items
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.ProductId)
            .ToList();

        var subtotal = sortedItems.Sum(i => i.LineTotal);
        var canCheckout = sortedItems.Count > 0 && sortedItems.All(i => i.CanPurchase);

        return new CartDto(
            CartId: cart.Id,
            Version: cart.Version,
            Items: sortedItems,
            Subtotal: subtotal,
            CanCheckout: canCheckout
        );
    }
}
