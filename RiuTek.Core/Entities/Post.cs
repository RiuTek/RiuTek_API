using RiuTek.Core.Common;

namespace RiuTek.Core.Entities;

public class Post : BaseEntity, IAggregateRoot
{
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    
    public Guid AuthorId { get; set; }
    public User Author { get; set; } = null!;

    public int ViewCount { get; set; }
    public bool IsPublished { get; set; }
    public bool IsFeatured { get; set; }
    public DateTime? PublishedAt { get; set; }

    public ICollection<PostComment> Comments { get; set; } = new List<PostComment>();
}
