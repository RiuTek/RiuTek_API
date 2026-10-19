using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;

namespace RiuTek.Application.DTOs;

public record PCBuildItemInputDto(Guid ProductId, int Quantity);

public record PCBuildPreviewItemDto(
    Guid ProductId,
    string Name,
    string Slug,
    string Sku,
    string ImageUrl,
    ComponentType ComponentType,
    int Quantity,
    decimal CurrentUnitPrice,
    decimal LineTotal,
    int StockQuantity,
    bool IsActive,
    bool CanPurchase,
    string? IssueCode,
    ComponentSpecification? Specifications
);

public record PCBuildPreviewResultDto(
    decimal TotalPrice,
    int EstimatedWattage,
    int RecommendedPsuWattage,
    bool IsCompatible,
    bool IsCompleteSystem,
    List<string> MissingComponents,
    List<CompatibilityIssueDto> Issues,
    bool CanPurchaseAll,
    List<PCBuildPreviewItemDto> Items
);

public record MyPCBuildItemDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductSlug,
    string ProductSku,
    string ProductImageUrl,
    ComponentType ComponentType,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    decimal CurrentPrice,
    int StockQuantity,
    bool IsActive,
    bool CanPurchase,
    ComponentSpecification? Specifications
);

public record MyPCBuildDto(
    Guid Id,
    string Name,
    string? Description,
    decimal TotalPrice,
    int EstimatedWattage,
    int RecommendedPsuWattage,
    bool IsCompatible,
    bool IsCompleteSystem,
    List<string> MissingComponents,
    List<CompatibilityIssueDto> Issues,
    bool CanPurchaseAll,
    PCBuildStatus Status,
    List<MyPCBuildItemDto> Items,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record MyPCBuildSummaryDto(
    Guid Id,
    string Name,
    string? Description,
    decimal TotalPrice,
    int EstimatedWattage,
    bool IsCompatible,
    bool CanPurchaseAll,
    int ItemCount,
    PCBuildStatus Status,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);
