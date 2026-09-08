using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;

namespace RiuTek.Application.DTOs;

public record ProductDto(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    string Name,
    string Slug,
    string Sku,
    string Brand,
    decimal Price,
    decimal? OriginalPrice,
    int StockQuantity,
    bool IsActive,
    string ImageUrl,
    List<string> AdditionalImages,
    ComponentType ComponentType,
    ComponentSpecification Specifications,
    DateTime CreatedAt
);

public record ProductSummaryDto(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    string Name,
    string Slug,
    string Sku,
    string Brand,
    decimal Price,
    decimal? OriginalPrice,
    int StockQuantity,
    bool IsActive,
    string ImageUrl,
    ComponentType ComponentType,
    DateTime CreatedAt
);

public record CategoryDto(
    Guid Id,
    string Name,
    string Slug,
    ComponentType ComponentType,
    string? Description,
    Guid? ParentId,
    List<CategoryDto> SubCategories
);

public record PCBuildDto(
    Guid Id,
    Guid? UserId,
    string Name,
    string? Description,
    decimal TotalPrice,
    int EstimatedWattage,
    bool IsCompatible,
    List<string> CompatibilityNotes,
    bool IsAiGenerated,
    string? AiRationale,
    PCBuildStatus Status,
    List<PCBuildItemDto> Items,
    DateTime CreatedAt
);

public record PCBuildItemDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductImageUrl,
    ComponentType ComponentType,
    int Quantity,
    decimal UnitPrice,
    decimal TotalPrice,
    ComponentSpecification? Specifications
);

public record UserAddressDto(
    Guid Id,
    Guid UserId,
    string ReceiverName,
    string PhoneNumber,
    string AddressLine,
    string Ward,
    string District,
    string City,
    bool IsDefault
);

public record ReviewDto(
    Guid Id,
    Guid ProductId,
    Guid UserId,
    string UserFullName,
    Guid OrderId,
    int Rating,
    string? Title,
    string? Content,
    List<string> Images,
    string? StaffReply,
    DateTime? StaffReplyAt,
    DateTime CreatedAt
);

public record CommentDto(
    Guid Id,
    Guid ProductId,
    Guid UserId,
    string UserFullName,
    string Content,
    Guid? ParentCommentId,
    bool IsStaffAnswer,
    List<CommentDto> Replies,
    DateTime CreatedAt
);

public record WishlistDto(
    Guid Id,
    Guid UserId,
    Guid ProductId,
    string ProductName,
    decimal ProductPrice,
    string ProductImageUrl,
    DateTime CreatedAt
);

public record CompatibilityCheckResultDto(
    bool IsCompatible,
    bool IsCompleteSystem,
    List<string> MissingComponents,
    int EstimatedWattage,
    int RecommendedPsuWattage,
    List<CompatibilityIssueDto> Issues
);

public record CompatibilityIssueDto(
    string RuleId,
    string Severity, // Error, Warning, Info
    string ComponentA,
    string ComponentB,
    string Message
);
