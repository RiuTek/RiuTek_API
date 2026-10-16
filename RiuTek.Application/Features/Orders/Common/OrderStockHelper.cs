using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Features.Orders.Common;

public static class OrderStockHelper
{
    public static async Task<Result> RestoreCancelledOrderStockAsync(
        Order order,
        IApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        var quantitiesByProduct = new Dictionary<Guid, int>();
        try
        {
            foreach (var item in order.Items)
            {
                quantitiesByProduct[item.ProductId] = checked(
                    quantitiesByProduct.GetValueOrDefault(item.ProductId) + item.Quantity);
            }
        }
        catch (OverflowException)
        {
            return Result.Failure(Error.Conflict(
                "Order.StockOverflow",
                "Stock cannot be restored because the resulting quantity is too large."));
        }

        var productIds = quantitiesByProduct.Keys.ToList();
        var products = await context.Products
            .Where(product => productIds.Contains(product.Id))
            .ToListAsync(cancellationToken);

        if (products.Count != productIds.Count)
        {
            return Result.Failure(Error.Conflict(
                "Order.ProductMissing",
                "An order product no longer exists, so stock cannot be restored safely."));
        }

        var restoredStock = new Dictionary<Guid, int>();
        try
        {
            foreach (var product in products)
            {
                restoredStock[product.Id] = checked(
                    product.StockQuantity + quantitiesByProduct[product.Id]);
            }
        }
        catch (OverflowException)
        {
            return Result.Failure(Error.Conflict(
                "Order.StockOverflow",
                "Stock cannot be restored because the resulting quantity is too large."));
        }

        foreach (var product in products)
        {
            product.StockQuantity = restoredStock[product.Id];
        }

        return Result.Success();
    }
}
