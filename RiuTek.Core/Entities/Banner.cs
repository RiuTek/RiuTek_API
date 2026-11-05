using RiuTek.Core.Common;

namespace RiuTek.Core.Entities;

public class Banner : BaseEntity, IAggregateRoot
{
    public string Title { get; set; } = string.Empty;
    public string ImageObjectKey { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string TargetUrl { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }

    public Banner() { }

    public Banner(
        string title,
        string imageObjectKey,
        string imageUrl,
        string targetUrl,
        int displayOrder = 0,
        bool isActive = false)
    {
        Title = title;
        ImageObjectKey = imageObjectKey;
        ImageUrl = imageUrl;
        TargetUrl = targetUrl;
        DisplayOrder = displayOrder;
        IsActive = isActive;
    }
}
