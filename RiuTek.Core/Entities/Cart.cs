using RiuTek.Core.Common;

namespace RiuTek.Core.Entities;

public class Cart : BaseEntity, IAggregateRoot
{
    private readonly List<CartItem> _items = [];

    public Guid UserId { get; private set; }
    public int Version { get; private set; } = 1;

    // Navigation properties
    public User? User { get; private set; }
    public IReadOnlyCollection<CartItem> Items => _items.AsReadOnly();

    protected Cart() { }

    public Cart(Guid userId, DateTime? nowUtc = null)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId cannot be empty.", nameof(userId));
        }

        UserId = userId;
        Version = 1;
        var now = nowUtc ?? DateTime.UtcNow;
        CreatedAt = now;
        UpdatedAt = null;
    }

    public Result AddItem(Guid productId, int quantity, DateTime? nowUtc = null)
    {
        if (productId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("Cart.InvalidProductId", "Id sản phẩm không hợp lệ."));
        }

        if (quantity < 1 || quantity > 99)
        {
            return Result.Failure(Error.Validation("Cart.InvalidQuantity", "Số lượng sản phẩm phải từ 1 đến 99."));
        }

        var existingItem = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existingItem is not null)
        {
            if (existingItem.Quantity + quantity > 99)
            {
                return Result.Failure(Error.Validation(
                    "Cart.MaxQuantityExceeded",
                    "Tổng số lượng cho một sản phẩm trong giỏ hàng không được vượt quá 99."));
            }

            existingItem.UpdateQuantity(existingItem.Quantity + quantity, nowUtc);
        }
        else
        {
            if (_items.Count >= 50)
            {
                return Result.Failure(Error.Validation(
                    "Cart.MaxDistinctItemsExceeded",
                    "Giỏ hàng không được vượt quá 50 sản phẩm khác nhau."));
            }

            _items.Add(new CartItem(Id, productId, quantity, nowUtc));
        }

        Touch(nowUtc);
        return Result.Success();
    }

    public Result SetItemQuantity(Guid productId, int quantity, DateTime? nowUtc = null)
    {
        if (productId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("Cart.InvalidProductId", "Id sản phẩm không hợp lệ."));
        }

        if (quantity < 1 || quantity > 99)
        {
            return Result.Failure(Error.Validation("Cart.InvalidQuantity", "Số lượng sản phẩm phải từ 1 đến 99."));
        }

        var existingItem = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existingItem is null)
        {
            return Result.Failure(Error.NotFound("Cart.ItemNotFound", "Sản phẩm không có trong giỏ hàng."));
        }

        if (existingItem.Quantity == quantity)
        {
            // No-op: no state change, no version bump
            return Result.Success();
        }

        existingItem.UpdateQuantity(quantity, nowUtc);
        Touch(nowUtc);
        return Result.Success();
    }

    public Result RemoveItem(Guid productId, DateTime? nowUtc = null)
    {
        if (productId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("Cart.InvalidProductId", "Id sản phẩm không hợp lệ."));
        }

        var existingItem = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existingItem is null)
        {
            return Result.Failure(Error.NotFound("Cart.ItemNotFound", "Sản phẩm không có trong giỏ hàng."));
        }

        _items.Remove(existingItem);
        Touch(nowUtc);
        return Result.Success();
    }

    public Result Clear(DateTime? nowUtc = null)
    {
        if (_items.Count == 0)
        {
            // No-op: already empty, no state change, no version bump
            return Result.Success();
        }

        _items.Clear();
        Touch(nowUtc);
        return Result.Success();
    }

    private void Touch(DateTime? nowUtc)
    {
        UpdatedAt = nowUtc ?? DateTime.UtcNow;
        Version++;
    }
}
