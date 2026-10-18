using RiuTek.Application.Common.Interfaces;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Products.Common;

public static class StockMovementHelper
{
    public static Result<StockMovement> ApplyStockMovement(
        Product product,
        int quantityDelta,
        StockMovementReason reason,
        string? note,
        Guid? actorUserId,
        Guid? orderId,
        IApplicationDbContext context)
    {
        if (product is null)
        {
            return Result.Failure<StockMovement>(Error.NotFound(
                "Inventory.ProductNotFound",
                "Sản phẩm không tồn tại."));
        }

        if (quantityDelta == 0)
        {
            return Result.Failure<StockMovement>(Error.Validation(
                "Inventory.InvalidDelta",
                "Biến động tồn kho (QuantityDelta) không được bằng 0."));
        }

        if (!Enum.IsDefined(typeof(StockMovementReason), reason))
        {
            return Result.Failure<StockMovement>(Error.Validation(
                "Inventory.InvalidReason",
                "Lý do biến động kho không hợp lệ."));
        }

        var stockBefore = product.StockQuantity;
        var targetStock = (long)stockBefore + quantityDelta;

        if (targetStock < 0)
        {
            return Result.Failure<StockMovement>(Error.Conflict(
                "Inventory.InsufficientStock",
                $"Số lượng tồn kho không đủ để thực hiện thao tác (Hiện tại: {stockBefore}, Biến động: {quantityDelta})."));
        }

        if (targetStock > int.MaxValue)
        {
            return Result.Failure<StockMovement>(Error.Conflict(
                "Inventory.StockOverflow",
                "Số lượng tồn kho sau biến động vượt quá giới hạn tối đa cho phép."));
        }

        var stockAfter = (int)targetStock;
        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        if (reason == StockMovementReason.ManualAdjustment)
        {
            if (actorUserId is null || actorUserId == Guid.Empty)
            {
                return Result.Failure<StockMovement>(Error.Validation(
                    "Inventory.InvalidActor",
                    "Người thực hiện (ActorUserId) là bắt buộc đối với điều chỉnh tồn kho thủ công."));
            }

            if (string.IsNullOrWhiteSpace(trimmedNote) || trimmedNote.Length < 3 || trimmedNote.Length > 500)
            {
                return Result.Failure<StockMovement>(Error.Validation(
                    "Inventory.InvalidNote",
                    "Ghi chú lý do điều chỉnh tồn kho là bắt buộc và phải từ 3 đến 500 ký tự."));
            }
        }

        if (reason is StockMovementReason.Checkout or StockMovementReason.OrderCancellation or StockMovementReason.PaymentExpiration)
        {
            if (orderId is null || orderId == Guid.Empty)
            {
                return Result.Failure<StockMovement>(Error.Validation(
                    "Inventory.InvalidOrder",
                    $"OrderId là bắt buộc đối với biến động tồn kho có lý do '{reason}'."));
            }
        }

        var movement = new StockMovement(
            productId: product.Id,
            quantityDelta: quantityDelta,
            stockBefore: stockBefore,
            stockAfter: stockAfter,
            reason: reason,
            note: trimmedNote,
            actorUserId: actorUserId,
            orderId: orderId);

        product.StockQuantity = stockAfter;
        context.StockMovements.Add(movement);

        return Result.Success(movement);
    }
}
