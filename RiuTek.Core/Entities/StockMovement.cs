using RiuTek.Core.Common;
using RiuTek.Core.Enums;

namespace RiuTek.Core.Entities;

public class StockMovement : BaseEntity
{
    public Guid ProductId { get; protected set; }
    public int QuantityDelta { get; protected set; }
    public int StockBefore { get; protected set; }
    public int StockAfter { get; protected set; }
    public StockMovementReason Reason { get; protected set; }
    public string? Note { get; protected set; }
    public Guid? ActorUserId { get; protected set; }
    public Guid? OrderId { get; protected set; }

    // Navigation properties
    public Product Product { get; protected set; } = null!;
    public User? ActorUser { get; protected set; }
    public Order? Order { get; protected set; }

    protected StockMovement() { }

    public StockMovement(
        Guid productId,
        int quantityDelta,
        int stockBefore,
        int stockAfter,
        StockMovementReason reason,
        string? note = null,
        Guid? actorUserId = null,
        Guid? orderId = null)
    {
        if (productId == Guid.Empty)
        {
            throw new ArgumentException("ProductId không được để trống.", nameof(productId));
        }

        if (quantityDelta == 0)
        {
            throw new ArgumentException("QuantityDelta không được bằng 0.", nameof(quantityDelta));
        }

        if (stockBefore < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stockBefore), "StockBefore không được âm.");
        }

        if (stockAfter < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stockAfter), "StockAfter không được âm.");
        }

        if ((long)stockBefore + quantityDelta != (long)stockAfter)
        {
            throw new InvalidOperationException($"StockAfter ({stockAfter}) không khớp với StockBefore ({stockBefore}) + QuantityDelta ({quantityDelta}).");
        }

        if (!Enum.IsDefined(typeof(StockMovementReason), reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), "Lý do biến động kho không hợp lệ.");
        }

        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmedNote != null && trimmedNote.Length > 500)
        {
            throw new ArgumentException("Note không được vượt quá 500 ký tự.", nameof(note));
        }

        if (reason is StockMovementReason.Checkout or StockMovementReason.OrderCancellation or StockMovementReason.PaymentExpiration)
        {
            if (orderId is null || orderId == Guid.Empty)
            {
                throw new ArgumentException($"OrderId là bắt buộc đối với biến động kho có lý do '{reason}'.", nameof(orderId));
            }
        }

        if (reason == StockMovementReason.ManualAdjustment)
        {
            if (actorUserId is null || actorUserId == Guid.Empty)
            {
                throw new ArgumentException("ActorUserId là bắt buộc đối với điều chỉnh kho thủ công (ManualAdjustment).", nameof(actorUserId));
            }

            if (string.IsNullOrWhiteSpace(trimmedNote) || trimmedNote.Length < 3)
            {
                throw new ArgumentException("Note là bắt buộc (tối thiểu 3 ký tự) đối với điều chỉnh kho thủ công (ManualAdjustment).", nameof(note));
            }
        }

        ProductId = productId;
        QuantityDelta = quantityDelta;
        StockBefore = stockBefore;
        StockAfter = stockAfter;
        Reason = reason;
        Note = trimmedNote;
        ActorUserId = actorUserId;
        OrderId = orderId;
    }
}
