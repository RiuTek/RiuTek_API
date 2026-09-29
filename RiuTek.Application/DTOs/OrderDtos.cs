using System.Text.Json.Serialization;
using RiuTek.Core.Enums;

namespace RiuTek.Application.DTOs;

public record CheckoutQuoteDto(
    int CartVersion,
    Guid AddressId,
    string ReceiverName,
    string PhoneNumber,
    string ShippingAddress,
    IReadOnlyList<CheckoutQuoteItemDto> Items,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal FinalAmount,
    bool CanCheckout,
    IReadOnlyList<string> Issues
);

public record CheckoutQuoteItemDto(
    Guid ProductId,
    string Name,
    string Sku,
    string ImageUrl,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal,
    int StockQuantity,
    bool IsActive,
    bool CanPurchase,
    string? IssueCode
);

public record CheckoutOrderDto(
    Guid Id,
    string OrderNumber,
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    OrderStatus Status,
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    PaymentMethod PaymentMethod,
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    PaymentStatus PaymentStatus,
    string Currency,
    string CustomerName,
    string CustomerEmail,
    string CustomerPhone,
    string ShippingAddress,
    decimal TotalAmount,
    decimal DiscountAmount,
    decimal FinalAmount,
    string? Notes,
    DateTime CreatedAt,
    IReadOnlyList<CheckoutOrderItemDto> Items
);

public record CheckoutOrderItemDto(
    Guid ProductId,
    string ProductName,
    string ProductSku,
    decimal UnitPrice,
    int Quantity,
    decimal TotalPrice
);
