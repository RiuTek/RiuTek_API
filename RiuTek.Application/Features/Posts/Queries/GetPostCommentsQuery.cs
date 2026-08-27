using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Mappings;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Posts.Queries;

public record GetPostCommentsQuery(Guid PostId) : IRequest<Result<List<PostCommentDto>>>;

public class GetPostCommentsQueryHandler : IRequestHandler<GetPostCommentsQuery, Result<List<PostCommentDto>>>
{
    private readonly IApplicationDbContext _context;

    public GetPostCommentsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<List<PostCommentDto>>> Handle(
        GetPostCommentsQuery request,
        CancellationToken cancellationToken)
    {
        var postExists = await _context.Posts
            .AnyAsync(p => p.Id == request.PostId, cancellationToken);

        if (!postExists)
        {
            return Result.Failure<List<PostCommentDto>>(Error.NotFound(
                "Post.NotFound",
                "Bài viết không tồn tại."));
        }

        var comments = await _context.PostComments
            .AsNoTracking()
            .Include(c => c.User)
            .Include(c => c.Replies)
                .ThenInclude(r => r.User)
            .Where(c => c.PostId == request.PostId && c.ParentCommentId == null)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        var result = comments.Select(c => c.ToDto()).ToList();

        return Result.Success(result);
    }
}
