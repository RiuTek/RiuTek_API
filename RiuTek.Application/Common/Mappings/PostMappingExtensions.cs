using RiuTek.Application.DTOs;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Common.Mappings;

public static class PostMappingExtensions
{
    public static PostDto ToDto(this Post post)
    {
        return new PostDto(
            post.Id,
            post.Title,
            post.Slug,
            post.Summary,
            post.Content,
            post.ThumbnailUrl,
            post.AuthorId,
            post.Author?.FullName ?? string.Empty,
            post.ViewCount,
            post.IsPublished,
            post.IsFeatured,
            post.PublishedAt,
            post.CreatedAt,
            post.UpdatedAt
        );
    }

    public static PostSummaryDto ToSummaryDto(this Post post)
    {
        return new PostSummaryDto(
            post.Id,
            post.Title,
            post.Slug,
            post.Summary,
            post.ThumbnailUrl,
            post.AuthorId,
            post.Author?.FullName ?? string.Empty,
            post.ViewCount,
            post.IsFeatured,
            post.PublishedAt,
            post.CreatedAt
        );
    }
}
