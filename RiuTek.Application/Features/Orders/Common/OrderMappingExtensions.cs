using System.Linq.Expressions;
using RiuTek.Application.DTOs;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Features.Orders.Common;

public static class OrderMappingExtensions
{
    public static readonly Expression<Func<Order, OrderSummaryDto>> ToSummaryDto = o => new OrderSummaryDto(
        o.Id,
        o.OrderNumber,
        o.Status,
        o.PaymentMethod,
        o.PaymentStatus,
        o.Currency,
        o.TotalAmount,
        o.DiscountAmount,
        o.FinalAmount,
        o.Items.Count,
        o.CreatedAt
    );

    public static readonly Expression<Func<Order, OrderDetailDto>> ToDetailDto = o => new OrderDetailDto(
        o.Id,
        o.OrderNumber,
        o.Status,
        o.PaymentMethod,
        o.PaymentStatus,
        o.Currency,
        o.CustomerName,
        o.CustomerEmail,
        o.CustomerPhone,
        o.ShippingAddress,
        o.TotalAmount,
        o.DiscountAmount,
        o.FinalAmount,
        o.Notes,
        o.CreatedAt,
        o.Items.OrderBy(i => i.Id).Select(i => new OrderItemDto(
            i.ProductId,
            i.ProductName,
            i.ProductSku,
            i.UnitPrice,
            i.Quantity,
            i.TotalPrice
        )).ToList()
    );
}
