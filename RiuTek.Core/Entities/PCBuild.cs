using RiuTek.Core.Common;
using RiuTek.Core.Enums;

namespace RiuTek.Core.Entities;

public class PCBuild : BaseEntity, IAggregateRoot
{
    public Guid? UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal TotalPrice { get; set; }
    public int EstimatedWattage { get; set; }
    public bool IsCompatible { get; set; } = true;
    public List<string> CompatibilityNotes { get; set; } = [];
    public bool IsAiGenerated { get; set; }
    public string? AiRationale { get; set; }
    public PCBuildStatus Status { get; set; } = PCBuildStatus.Draft;

    // Navigation properties
    public User? User { get; set; }
    public ICollection<PCBuildItem> Items { get; set; } = new List<PCBuildItem>();

    protected PCBuild() { }

    public PCBuild(
        string name,
        Guid? userId = null,
        string? description = null,
        bool isAiGenerated = false,
        string? aiRationale = null)
    {
        Name = name;
        UserId = userId;
        Description = description;
        IsAiGenerated = isAiGenerated;
        AiRationale = aiRationale;
        Status = PCBuildStatus.Draft;
    }

    public void RecalculateTotals()
    {
        TotalPrice = Items.Sum(i => i.UnitPrice * i.Quantity);
    }
}

public class PCBuildItem : BaseEntity
{
    public Guid PCBuildId { get; set; }
    public Guid ProductId { get; set; }
    public ComponentType ComponentType { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }

    // Navigation properties
    public PCBuild PCBuild { get; set; } = null!;
    public Product Product { get; set; } = null!;

    protected PCBuildItem() { }

    public PCBuildItem(Guid pcBuildId, Guid productId, ComponentType componentType, decimal unitPrice, int quantity = 1)
    {
        PCBuildId = pcBuildId;
        ProductId = productId;
        ComponentType = componentType;
        UnitPrice = unitPrice;
        Quantity = quantity;
    }
}
