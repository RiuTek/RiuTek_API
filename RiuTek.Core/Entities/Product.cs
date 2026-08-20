using Pgvector;
using RiuTek.Core.Common;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;

namespace RiuTek.Core.Entities;

public class Product : BaseEntity, IAggregateRoot
{
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal? OriginalPrice { get; set; }
    public int StockQuantity { get; set; }
    public bool IsActive { get; set; } = true;
    public string ImageUrl { get; set; } = string.Empty;
    public List<string> AdditionalImages { get; set; } = [];
    public ComponentType ComponentType { get; set; }
    
    // Stored as JSONB in PostgreSQL
    public ComponentSpecification Specifications { get; set; } = null!;

    // Stored as vector(1536) in PostgreSQL for AI semantic search
    public Vector? Embedding { get; set; }

    // Navigation properties
    public Category Category { get; set; } = null!;
    public ICollection<PCBuildItem> PCBuildItems { get; set; } = new List<PCBuildItem>();
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public ICollection<Comment> Comments { get; set; } = new List<Comment>();
    public ICollection<Wishlist> Wishlists { get; set; } = new List<Wishlist>();

    protected Product() { }

    public Product(
        Guid categoryId,
        string name,
        string slug,
        string sku,
        string brand,
        decimal price,
        int stockQuantity,
        string imageUrl,
        ComponentType componentType,
        ComponentSpecification specifications,
        decimal? originalPrice = null)
    {
        CategoryId = categoryId;
        Name = name;
        Slug = slug;
        Sku = sku;
        Brand = brand;
        Price = price;
        StockQuantity = stockQuantity;
        ImageUrl = imageUrl;
        ComponentType = componentType;
        Specifications = specifications;
        OriginalPrice = originalPrice;
        IsActive = true;
    }
}
