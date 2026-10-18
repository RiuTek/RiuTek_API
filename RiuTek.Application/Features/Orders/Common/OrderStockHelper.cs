using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Products.Common;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Orders.Common;

public static class OrderStockHelper
{
    public static async Task<Result> RestoreCancelledOrderStockAsync(
        Order order,
        IApplicationDbContext context,
        Guid? actorUserId,
        StockMovementReason reason,
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

        foreach (var product in products)
        {
            var qty = quantitiesByProduct[product.Id];
            var movementResult = StockMovementHelper.ApplyStockMovement(
                product,
                qty,
                reason,
                note: null,
                actorUserId: actorUserId,
                orderId: order.Id,
                context: context);

            if (movementResult.IsFailure)
            {
                return movementResult;
            }
        }

        return Result.Success();
    }
}
