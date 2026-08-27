namespace RiuTek.Application.DTOs;

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

public record ProductCommentDto(
    Guid Id,
    Guid ProductId,
    Guid UserId,
    string UserName,
    string Content,
    Guid? ParentCommentId,
    bool IsStaffAnswer,
    DateTime CreatedAt,
    List<ProductCommentDto>? Replies = null
);
