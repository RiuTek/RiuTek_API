using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Core.Common;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Posts.Commands;

public record DeletePostCommand(Guid Id) : IRequest<Result<Unit>>;

public class DeletePostCommandHandler : IRequestHandler<DeletePostCommand, Result<Unit>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICacheService _cacheService;

    public DeletePostCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        ICacheService cacheService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _cacheService = cacheService;
    }

    public async Task<Result<Unit>> Handle(
        DeletePostCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
        {
            return Result.Failure<Unit>(Error.Unauthorized(
                "Auth.Unauthorized",
                "Bạn cần đăng nhập để xóa bài viết."));
        }

        var post = await _context.Posts
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        if (post == null)
        {
            return Result.Failure<Unit>(Error.NotFound(
                "Post.NotFound",
                "Không tìm thấy bài viết cần xóa."));
        }

        var currentUserId = _currentUserService.UserId.Value;
        var userRole = _currentUserService.UserRole;
        var isAdminOrStaff = userRole == UserRole.Admin.ToString() || userRole == UserRole.Staff.ToString();

        if (post.AuthorId != currentUserId && !isAdminOrStaff)
        {
            return Result.Failure<Unit>(Error.Forbidden(
                "Post.Forbidden",
                "Bạn không có quyền xóa bài viết này."));
        }

        _context.Posts.Remove(post);
        await _context.SaveChangesAsync(cancellationToken);

        await _cacheService.RemoveByPrefixAsync(PostCacheKeys.PostListPrefix, cancellationToken);

        return Result.Success(Unit.Value);
    }
}
