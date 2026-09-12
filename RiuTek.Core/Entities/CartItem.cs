using RiuTek.Core.Common;

namespace RiuTek.Core.Entities;

public class CartItem : BaseEntity
{
    public Guid CartId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }

    // Navigation properties
    public Cart? Cart { get; private set; }
    public Product? Product { get; private set; }

    protected CartItem() { }

    internal CartItem(Guid cartId, Guid productId, int quantity, DateTime? nowUtc = null)
    {
        if (cartId == Guid.Empty)
        {
            throw new ArgumentException("CartId cannot be empty.", nameof(cartId));
        }

        if (productId == Guid.Empty)
        {
            throw new ArgumentException("ProductId cannot be empty.", nameof(productId));
        }

        if (quantity < 1 || quantity > 99)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be between 1 and 99.");
        }

        Id = Guid.Empty;
        CartId = cartId;
        ProductId = productId;
        Quantity = quantity;
        var now = nowUtc ?? DateTime.UtcNow;
        CreatedAt = now;
        UpdatedAt = null;
    }

    internal void UpdateQuantity(int quantity, DateTime? nowUtc = null)
    {
        if (quantity < 1 || quantity > 99)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be between 1 and 99.");
        }

        Quantity = quantity;
        UpdatedAt = nowUtc ?? DateTime.UtcNow;
    }
}
