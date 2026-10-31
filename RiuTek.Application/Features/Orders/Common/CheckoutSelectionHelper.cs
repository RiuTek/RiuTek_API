using RiuTek.Core.Common;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Features.Orders.Common;

public static class CheckoutSelectionHelper
{
    public static Result<IReadOnlyList<CartItem>> ResolveSelectedItems(
        Cart? cart,
        IReadOnlyList<Guid>? selectedProductIds)
    {
        if (selectedProductIds is null)
        {
            if (cart is null || cart.Items.Count == 0)
            {
                return Result.Success<IReadOnlyList<CartItem>>([]);
            }

            return Result.Success<IReadOnlyList<CartItem>>(cart.Items.ToList());
        }

        if (cart is null || cart.Items.Count == 0)
        {
            return Result.Failure<IReadOnlyList<CartItem>>(Error.Conflict(
                "Checkout.SelectionChanged",
                "One or more selected products are no longer in the cart."));
        }

        var cartItemsByProductId = cart.Items.ToDictionary(i => i.ProductId);
        var selectedItems = new List<CartItem>(selectedProductIds.Count);

        foreach (var productId in selectedProductIds)
        {
            if (!cartItemsByProductId.TryGetValue(productId, out var item))
            {
                return Result.Failure<IReadOnlyList<CartItem>>(Error.Conflict(
                    "Checkout.SelectionChanged",
                    "One or more selected products are no longer in the cart."));
            }

            selectedItems.Add(item);
        }

        return Result.Success<IReadOnlyList<CartItem>>(selectedItems);
    }
}
