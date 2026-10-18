using RiuTek.Core.Enums;

namespace RiuTek.Application.DTOs;

public record AdjustStockRequest(
    int ExpectedStockQuantity,
    int QuantityDelta,
    string Note
);

public record StockAdjustmentResultDto(
    Guid ProductId,
    string Sku,
    int StockBefore,
    int QuantityDelta,
    int StockAfter,
    Guid MovementId,
    StockMovementReason Reason,
    string? Note,
    DateTime CreatedAt
);

public record StockMovementDto(
    Guid Id,
    Guid ProductId,
    string Sku,
    int QuantityDelta,
    int StockBefore,
    int StockAfter,
    StockMovementReason Reason,
    string? Note,
    Guid? ActorUserId,
    string? ActorUserName,
    Guid? OrderId,
    DateTime CreatedAt
);
