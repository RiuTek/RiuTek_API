using RiuTek.Core.Common;

namespace RiuTek.Core.Entities;

public class Review : BaseEntity, IAggregateRoot
{
    public Guid ProductId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid OrderId { get; private set; }
    public int Rating { get; private set; } // 1 - 5 stars
    public string? Title { get; private set; }
    public string? Content { get; private set; }
    public List<string> Images { get; private set; } = [];
    public string? StaffReply { get; private set; }
    public DateTime? StaffReplyAt { get; private set; }

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
        if (productId == Guid.Empty)
        {
            throw new ArgumentException("ProductId cannot be empty.", nameof(productId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId cannot be empty.", nameof(userId));
        }

        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("OrderId cannot be empty.", nameof(orderId));
        }

        if (rating is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(rating), "Rating must be between 1 and 5 stars.");
        }

        var normalizedTitle = NormalizeText(title, 200);
        var normalizedContent = NormalizeText(content, 2000);

        ProductId = productId;
        UserId = userId;
        OrderId = orderId;
        Rating = rating;
        Title = normalizedTitle;
        Content = normalizedContent;
        Images = NormalizeImages(images);
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = null;
    }

    private static List<string> NormalizeImages(IEnumerable<string>? images)
    {
        if (images is null) return [];

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var img in images)
        {
            if (string.IsNullOrWhiteSpace(img)) continue;

            var trimmed = img.Trim();
            if (trimmed.Length == 0) continue;

            if (seen.Add(trimmed))
            {
                result.Add(trimmed);
            }
        }

        return result;
    }

    public Result UpdateReview(int rating, string? title, string? content)
    {
        if (rating is < 1 or > 5)
        {
            return Result.Failure(Error.Validation("Review.InvalidRating", "Rating must be between 1 and 5 stars."));
        }

        var trimmedTitle = title?.Trim();
        if (trimmedTitle != null && trimmedTitle.Length > 200)
        {
            return Result.Failure(Error.Validation("Review.InvalidTitle", "Title cannot exceed 200 characters."));
        }

        var trimmedContent = content?.Trim();
        if (trimmedContent != null && trimmedContent.Length > 2000)
        {
            return Result.Failure(Error.Validation("Review.InvalidContent", "Content cannot exceed 2000 characters."));
        }

        Rating = rating;
        Title = string.IsNullOrWhiteSpace(trimmedTitle) ? null : trimmedTitle;
        Content = string.IsNullOrWhiteSpace(trimmedContent) ? null : trimmedContent;
        UpdatedAt = DateTime.UtcNow;

        return Result.Success();
    }

    public Result SetStaffReply(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
        {
            return Result.Failure(Error.Validation("Review.InvalidReply", "Reply cannot be empty."));
        }

        var trimmed = reply.Trim();
        if (trimmed.Length > 2000)
        {
            return Result.Failure(Error.Validation("Review.InvalidReply", "Reply cannot exceed 2000 characters."));
        }

        StaffReply = trimmed;
        StaffReplyAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;

        return Result.Success();
    }

    public Result ClearStaffReply()
    {
        StaffReply = null;
        StaffReplyAt = null;
        UpdatedAt = DateTime.UtcNow;

        return Result.Success();
    }

    private static string? NormalizeText(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException($"Text cannot exceed {maxLength} characters.");
        }
        return trimmed;
    }
}
