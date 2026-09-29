using System.Text.Json.Serialization;
using RiuTek.Core.Enums;

namespace RiuTek.API.Contracts;

public record CheckoutQuoteRequest(Guid AddressId);

public record CheckoutCartRequest(
    Guid AddressId,
    int ExpectedCartVersion,
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    PaymentMethod PaymentMethod,
    string? Notes
);
