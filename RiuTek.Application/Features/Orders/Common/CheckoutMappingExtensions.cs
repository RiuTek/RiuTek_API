using RiuTek.Application.DTOs;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Features.Orders.Common;

public static class CheckoutMappingExtensions
{
    public static string FormatShippingAddress(UserAddress address) =>
        $"{address.AddressLine}, {address.Ward}, {address.District}, {address.City}";

    public static CheckoutOrderDto MapToDto(Order order)
    {
        var items = order.Items.Select(i => new OrderItemDto(
            ProductId: i.ProductId,
            ProductName: i.ProductName,
            ProductSku: i.ProductSku,
            UnitPrice: i.UnitPrice,
            Quantity: i.Quantity,
            TotalPrice: i.TotalPrice
        )).ToList();

        return new CheckoutOrderDto(
            Id: order.Id,
            OrderNumber: order.OrderNumber,
            Status: order.Status,
            PaymentMethod: order.PaymentMethod,
            PaymentStatus: order.PaymentStatus,
            Currency: order.Currency,
            CustomerName: order.CustomerName,
            CustomerEmail: order.CustomerEmail,
            CustomerPhone: order.CustomerPhone,
            ShippingAddress: order.ShippingAddress,
            TotalAmount: order.TotalAmount,
            DiscountAmount: order.DiscountAmount,
            FinalAmount: order.FinalAmount,
            Notes: order.Notes,
            CreatedAt: order.CreatedAt,
            Items: items
        );
    }
}
