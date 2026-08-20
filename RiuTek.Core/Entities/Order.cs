using RiuTek.Core.Common;
using RiuTek.Core.Enums;

namespace RiuTek.Core.Entities;

public class Order : BaseEntity, IAggregateRoot
{
    public string OrderNumber { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string ShippingAddress { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal FinalAmount { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Stripe;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public string? StripePaymentIntentId { get; set; }
    public string? VNPayTransactionNo { get; set; }
    public string? Notes { get; set; }

    // Navigation properties
    public User? User { get; set; }
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();

    protected Order() { }

    public Order(
        string orderNumber,
        string customerName,
        string customerEmail,
        string customerPhone,
        string shippingAddress,
        PaymentMethod paymentMethod,
        Guid? userId = null,
        string? notes = null)
    {
        OrderNumber = orderNumber;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
        CustomerPhone = customerPhone;
        ShippingAddress = shippingAddress;
        PaymentMethod = paymentMethod;
        UserId = userId;
        Notes = notes;
        Status = OrderStatus.Pending;
        PaymentStatus = PaymentStatus.Pending;
    }

    public void CalculateTotals()
    {
        TotalAmount = Items.Sum(i => i.TotalPrice);
        FinalAmount = Math.Max(0, TotalAmount - DiscountAmount);
    }
}

public class OrderItem : BaseEntity
{
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? PCBuildId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductSku { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal TotalPrice { get; set; }

    // Navigation properties
    public Order Order { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public PCBuild? PCBuild { get; set; }

    protected OrderItem() { }

    public OrderItem(
        Guid orderId,
        Guid productId,
        string productName,
        string productSku,
        decimal unitPrice,
        int quantity = 1,
        Guid? pcBuildId = null)
    {
        OrderId = orderId;
        ProductId = productId;
        ProductName = productName;
        ProductSku = productSku;
        UnitPrice = unitPrice;
        Quantity = quantity;
        TotalPrice = unitPrice * quantity;
        PCBuildId = pcBuildId;
    }
}
