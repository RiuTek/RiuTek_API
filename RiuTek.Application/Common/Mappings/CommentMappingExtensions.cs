using RiuTek.Application.DTOs;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Common.Mappings;

public static class CommentMappingExtensions
{
    public static PostCommentDto ToDto(this PostComment comment)
    {
        return new PostCommentDto(
            comment.Id,
            comment.PostId,
            comment.UserId,
            comment.User?.FullName ?? string.Empty,
            comment.Content,
            comment.ParentCommentId,
            comment.CreatedAt,
            comment.UpdatedAt,
            comment.Replies?.Select(x => x.ToDto()).ToList()
        );
    }

    public static ProductCommentDto ToProductDto(this Comment comment)
    {
        return new ProductCommentDto(
            comment.Id,
            comment.ProductId,
            comment.UserId,
            comment.User?.FullName ?? string.Empty,
            comment.Content,
            comment.ParentCommentId,
            comment.IsStaffAnswer,
            comment.CreatedAt,
            comment.Replies?.Select(r => r.ToProductDto()).ToList()
        );
    }
}
