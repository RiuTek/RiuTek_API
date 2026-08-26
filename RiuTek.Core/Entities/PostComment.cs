using RiuTek.Core.Common;

namespace RiuTek.Core.Entities;

public class PostComment : BaseEntity
{
    public Guid PostId { get; set; }
    public Post Post { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string Content { get; set; } = string.Empty;

    public Guid? ParentCommentId { get; set; }
    public PostComment? ParentComment { get; set; }
    public ICollection<PostComment> Replies { get; set; } = new List<PostComment>();
}
