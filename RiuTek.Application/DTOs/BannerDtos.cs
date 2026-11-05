namespace RiuTek.Application.DTOs;

public record PublicBannerDto(
    Guid Id,
    string Title,
    string ImageUrl,
    string TargetUrl,
    int DisplayOrder);

public record ManagedBannerDto(
    Guid Id,
    string Title,
    string ImageObjectKey,
    string ImageUrl,
    string TargetUrl,
    int DisplayOrder,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
