using RiuTek.Core.Common;
using RiuTek.Core.Enums;

namespace RiuTek.Core.Entities;

public class PCBuild : BaseEntity, IAggregateRoot
{
    private readonly List<PCBuildItem> _items = [];

    public Guid? UserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public decimal TotalPrice { get; private set; }
    public int EstimatedWattage { get; private set; }
    public bool IsCompatible { get; private set; } = true;
    public List<string> CompatibilityNotes { get; private set; } = [];
    public bool IsAiGenerated { get; private set; }
    public string? AiRationale { get; private set; }
    public PCBuildStatus Status { get; private set; } = PCBuildStatus.Saved;

    // Navigation properties
    public User? User { get; private set; }
    public IReadOnlyCollection<PCBuildItem> Items => _items.AsReadOnly();

    protected PCBuild() { }

    public PCBuild(
        string name,
        Guid? userId = null,
        string? description = null,
        bool isAiGenerated = false,
        string? aiRationale = null)
    {
        if (userId.HasValue && userId.Value == Guid.Empty)
        {
            throw new ArgumentException("UserId cannot be empty.", nameof(userId));
        }

        Name = NormalizeName(name);
        UserId = userId;
        Description = NormalizeDescription(description);
        IsAiGenerated = isAiGenerated;
        AiRationale = isAiGenerated ? aiRationale?.Trim() : null;
        Status = PCBuildStatus.Saved;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = null;
    }

    public static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Build name cannot be empty.", nameof(name));
        }

        var trimmed = name.Trim();
        if (trimmed.Length > 200)
        {
            throw new ArgumentException("Build name cannot exceed 200 characters.", nameof(name));
        }

        return trimmed;
    }

    public static string? NormalizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var trimmed = description.Trim();
        if (trimmed.Length > 1000)
        {
            throw new ArgumentException("Build description cannot exceed 1000 characters.", nameof(description));
        }

        return trimmed;
    }

    public void UpdateDetails(string name, string? description)
    {
        Name = NormalizeName(name);
        Description = NormalizeDescription(description);
        UpdatedAt = DateTime.UtcNow;
    }

    public void ApplyCompatibilitySnapshot(bool isCompatible, int estimatedWattage, IEnumerable<string> notes)
    {
        IsCompatible = isCompatible;
        EstimatedWattage = estimatedWattage;
        CompatibilityNotes = notes?.ToList() ?? [];
        UpdatedAt = DateTime.UtcNow;
    }

    public void AddItem(PCBuildItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
        RecalculateTotals();
        UpdatedAt = DateTime.UtcNow;
    }

    public void ReplaceItems(IEnumerable<PCBuildItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items.Clear();
        foreach (var item in items)
        {
            _items.Add(item);
        }
        RecalculateTotals();
        UpdatedAt = DateTime.UtcNow;
    }

    public void RecalculateTotals()
    {
        checked
        {
            decimal total = 0;
            foreach (var item in _items)
            {
                total += item.UnitPrice * item.Quantity;
            }
            TotalPrice = total;
        }
    }
}

public class PCBuildItem : BaseEntity
{
    public Guid PCBuildId { get; private set; }
    public Guid ProductId { get; private set; }
    public ComponentType ComponentType { get; private set; }
    public int Quantity { get; private set; } = 1;
    public decimal UnitPrice { get; private set; }

    // Navigation properties
    public PCBuild PCBuild { get; private set; } = null!;
    public Product Product { get; set; } = null!;

    protected PCBuildItem() { }

    public PCBuildItem(Guid pcBuildId, Guid productId, ComponentType componentType, decimal unitPrice, int quantity = 1)
    {
        if (pcBuildId == Guid.Empty)
        {
            throw new ArgumentException("PCBuildId cannot be empty.", nameof(pcBuildId));
        }

        if (productId == Guid.Empty)
        {
            throw new ArgumentException("ProductId cannot be empty.", nameof(productId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than 0.");
        }

        if (unitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "UnitPrice cannot be negative.");
        }

        PCBuildId = pcBuildId;
        ProductId = productId;
        ComponentType = componentType;
        UnitPrice = unitPrice;
        Quantity = quantity;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = null;
    }

    public void UpdateQuantity(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than 0.");
        }

        Quantity = quantity;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateUnitPrice(decimal unitPrice)
    {
        if (unitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "UnitPrice cannot be negative.");
        }

        UnitPrice = unitPrice;
        UpdatedAt = DateTime.UtcNow;
    }
}
