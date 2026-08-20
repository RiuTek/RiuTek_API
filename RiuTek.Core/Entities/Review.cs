using RiuTek.Core.Common;

namespace RiuTek.Core.Entities;

public class Review : BaseEntity, IAggregateRoot
{
    public Guid ProductId { get; set; }
    public Guid UserId { get; set; }
    public Guid OrderId { get; set; }
    public int Rating { get; set; } // 1 - 5 stars
    public string? Title { get; set; }
    public string? Content { get; set; }
    public List<string> Images { get; set; } = [];
    public string? StaffReply { get; set; }
    public DateTime? StaffReplyAt { get; set; }

    // Navigation properties
    public Product Product { get; set; } = null!;
    public User User { get; set; } = null!;
    public Order Order { get; set; } = null!;

    protected Review() { }

    public Review(
        Guid productId,
        Guid userId,
        Guid orderId,
        int rating,
        string? title = null,
        string? content = null,
        List<string>? images = null)
    {
        if (rating is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(rating), "Rating must be between 1 and 5 stars.");
        }

        ProductId = productId;
        UserId = userId;
        OrderId = orderId;
        Rating = rating;
        Title = title;
        Content = content;
        Images = images ?? [];
    }

    public void AddStaffReply(string reply)
    {
        StaffReply = reply;
        StaffReplyAt = DateTime.UtcNow;
    }
}
