using RiuTek.Core.Common;

namespace RiuTek.Core.Entities;

public class Comment : BaseEntity, IAggregateRoot
{
    public Guid ProductId { get; set; }
    public Guid UserId { get; set; }
    public string Content { get; set; } = string.Empty;
    public Guid? ParentCommentId { get; set; }
    public bool IsStaffAnswer { get; set; }

    // Navigation properties
    public Product Product { get; set; } = null!;
    public User User { get; set; } = null!;
    public Comment? ParentComment { get; set; }
    public ICollection<Comment> Replies { get; set; } = new List<Comment>();

    protected Comment() { }

    public Comment(
        Guid productId,
        Guid userId,
        string content,
        Guid? parentCommentId = null,
        bool isStaffAnswer = false)
    {
        ProductId = productId;
        UserId = userId;
        Content = content;
        ParentCommentId = parentCommentId;
        IsStaffAnswer = isStaffAnswer;
    }
}
