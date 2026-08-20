using RiuTek.Core.Common;

namespace RiuTek.Core.Entities;

public class Wishlist : BaseEntity, IAggregateRoot
{
    public Guid UserId { get; set; }
    public Guid ProductId { get; set; }

    // Navigation properties
    public User User { get; set; } = null!;
    public Product Product { get; set; } = null!;

    protected Wishlist() { }

    public Wishlist(Guid userId, Guid productId)
    {
        UserId = userId;
        ProductId = productId;
    }
}
