namespace RiuTek.API.Contracts;

public record CreatePostRequest(
    string Title,
    string Summary,
    string Content,
    string? ThumbnailUrl,
    bool IsPublished = false,
    bool IsFeatured = false
);

public record UpdatePostRequest(
    string Title,
    string Summary,
    string Content,
    string? ThumbnailUrl,
    bool IsPublished,
    bool IsFeatured
);
