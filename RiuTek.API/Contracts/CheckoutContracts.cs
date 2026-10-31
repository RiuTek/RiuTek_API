using System.Text.Json.Serialization;
using RiuTek.Core.Enums;

namespace RiuTek.API.Contracts;

public record CheckoutQuoteRequest(Guid AddressId, IReadOnlyList<Guid>? SelectedProductIds = null);

public record CheckoutCartRequest(
    Guid AddressId,
    int ExpectedCartVersion,
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    PaymentMethod PaymentMethod,
    string? Notes,
    IReadOnlyList<Guid>? SelectedProductIds = null
);

public record BuyNowProductRequest(
    Guid AddressId,
    int Quantity,
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    PaymentMethod PaymentMethod,
    string? Notes
);

public record BuyNowPCBuildRequest(
    Guid AddressId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    PaymentMethod PaymentMethod,
    string? Notes = null
);

public record UpdateOrderStatusRequest(
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    OrderStatus ExpectedStatus,
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    OrderStatus NewStatus
);
