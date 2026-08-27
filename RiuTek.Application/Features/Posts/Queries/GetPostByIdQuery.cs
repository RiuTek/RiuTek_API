using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Mappings;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Posts.Queries;

public record GetPostByIdQuery(Guid Id) : IRequest<Result<PostDto>>;

public class GetPostByIdQueryHandler : IRequestHandler<GetPostByIdQuery, Result<PostDto>>
{
    private readonly IApplicationDbContext _context;

    public GetPostByIdQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<PostDto>> Handle(
        GetPostByIdQuery request,
        CancellationToken cancellationToken)
    {
        var post = await _context.Posts
            .Include(p => p.Author)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        if (post == null)
        {
            return Result.Failure<PostDto>(Error.NotFound(
                "Post.NotFound",
                "Không tìm thấy bài viết."));
        }

        return Result.Success(post.ToDto());
    }
}
