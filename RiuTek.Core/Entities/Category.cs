using RiuTek.Core.Common;
using RiuTek.Core.Enums;

namespace RiuTek.Core.Entities;

public class Category : BaseEntity, IAggregateRoot
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public ComponentType ComponentType { get; set; }
    public string? Description { get; set; }
    public Guid? ParentId { get; set; }

    // Navigation properties
    public Category? Parent { get; set; }
    public ICollection<Category> SubCategories { get; set; } = new List<Category>();
    public ICollection<Product> Products { get; set; } = new List<Product>();

    protected Category() { }

    public Category(string name, string slug, ComponentType componentType, string? description = null, Guid? parentId = null)
    {
        Name = name;
        Slug = slug;
        ComponentType = componentType;
        Description = description;
        ParentId = parentId;
    }
}
