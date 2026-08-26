namespace RiuTek.Application.DTOs;

public record PostDto(
    Guid Id,
    string Title,
    string Slug,
    string Summary,
    string Content,
    string? ThumbnailUrl,
    Guid AuthorId,
    string AuthorName,
    int ViewCount,
    bool IsPublished,
    bool IsFeatured,
    DateTime? PublishedAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record PostSummaryDto(
    Guid Id,
    string Title,
    string Slug,
    string Summary,
    string? ThumbnailUrl,
    Guid AuthorId,
    string AuthorName,
    int ViewCount,
    bool IsFeatured,
    DateTime? PublishedAt,
    DateTime CreatedAt
);

public record PostCommentDto(
    Guid Id,
    Guid PostId,
    Guid UserId,
    string UserName,
    string Content,
    Guid? ParentCommentId,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    List<PostCommentDto>? Replies = null
);
