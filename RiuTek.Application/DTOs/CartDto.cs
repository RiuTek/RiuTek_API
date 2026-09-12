namespace RiuTek.Application.DTOs;

public record CartDto(
    Guid? CartId,
    int Version,
    List<CartItemDto> Items,
    decimal Subtotal,
    bool CanCheckout
);

public record CartItemDto(
    Guid ProductId,
    string Name,
    string Slug,
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
