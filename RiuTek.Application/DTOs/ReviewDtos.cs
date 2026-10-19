using RiuTek.Application.Common.Models;

namespace RiuTek.Application.DTOs;

public record CreateReviewRequest(
    Guid OrderId,
    Guid ProductId,
    int Rating,
    string? Title = null,
    string? Content = null
);

public record UpdateReviewRequest(
    int Rating,
    string? Title = null,
    string? Content = null
);

public record SetReviewReplyRequest(
    string Reply
);

public record ProductReviewDto(
    Guid Id,
    string UserDisplayName,
    int Rating,
    string? Title,
    string? Content,
    IReadOnlyList<string> Images,
    string? StaffReply,
    DateTime? StaffReplyAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record RatingDistributionDto(
    int FiveStar,
    int FourStar,
    int ThreeStar,
    int TwoStar,
    int OneStar
);

public record ProductReviewsResultDto(
    Guid ProductId,
    decimal AverageRating,
    int TotalReviews,
    RatingDistributionDto RatingDistribution,
    PagedResult<ProductReviewDto> Reviews
);

public record MyReviewDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductSlug,
    string? ProductImageUrl,
    Guid OrderId,
    int Rating,
    string? Title,
    string? Content,
    IReadOnlyList<string> Images,
    string? StaffReply,
    DateTime? StaffReplyAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record ManagedReviewDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductSku,
    Guid UserId,
    string UserDisplayName,
    Guid OrderId,
    int Rating,
    string? Title,
    string? Content,
    IReadOnlyList<string> Images,
    string? StaffReply,
    DateTime? StaffReplyAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);
