namespace RiuTek.API.Contracts;

public record CreateCommentRequest(
    string Content,
    Guid? ParentCommentId = null
);
