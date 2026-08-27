using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Mappings;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Posts.Queries;

public record GetPostBySlugQuery(string Slug) : IRequest<Result<PostDto>>;

public class GetPostBySlugQueryHandler : IRequestHandler<GetPostBySlugQuery, Result<PostDto>>
{
    private readonly IApplicationDbContext _context;

    public GetPostBySlugQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<PostDto>> Handle(
        GetPostBySlugQuery request,
        CancellationToken cancellationToken)
    {
        var slug = request.Slug.Trim().ToLower();

        var post = await _context.Posts
            .Include(p => p.Author)
            .FirstOrDefaultAsync(p => p.Slug.ToLower() == slug, cancellationToken);

        if (post == null)
        {
            return Result.Failure<PostDto>(Error.NotFound(
                "Post.NotFound",
                "Không tìm thấy bài viết."));
        }

        // Increment view count
        post.ViewCount++;
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(post.ToDto());
    }
}
