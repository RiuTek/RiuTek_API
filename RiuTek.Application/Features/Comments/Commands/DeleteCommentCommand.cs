using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Core.Common;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Comments.Commands;

public enum CommentTargetType
{
    Post,
    Product
}

public record DeleteCommentCommand(
    Guid Id,
    CommentTargetType TargetType = CommentTargetType.Post
) : IRequest<Result<Unit>>;

public class DeleteCommentCommandHandler : IRequestHandler<DeleteCommentCommand, Result<Unit>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public DeleteCommentCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<Unit>> Handle(
        DeleteCommentCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
        {
            return Result.Failure<Unit>(Error.Unauthorized(
                "Auth.Unauthorized",
                "Bạn cần đăng nhập để xóa bình luận."));
        }

        var currentUserId = _currentUserService.UserId.Value;
        var userRole = _currentUserService.UserRole;
        var isAdminOrStaff = userRole == UserRole.Admin.ToString() || userRole == UserRole.Staff.ToString();

        if (request.TargetType == CommentTargetType.Post)
        {
            var postComment = await _context.PostComments
                .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);

            if (postComment == null)
            {
                return Result.Failure<Unit>(Error.NotFound(
                    "Comment.NotFound",
                    "Không tìm thấy bình luận cần xóa."));
            }

            if (postComment.UserId != currentUserId && !isAdminOrStaff)
            {
                return Result.Failure<Unit>(Error.Forbidden(
                    "Comment.Forbidden",
                    "Bạn không có quyền xóa bình luận này."));
            }

            _context.PostComments.Remove(postComment);
        }
        else
        {
            var productComment = await _context.Comments
                .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);

            if (productComment == null)
            {
                return Result.Failure<Unit>(Error.NotFound(
                    "Comment.NotFound",
                    "Không tìm thấy bình luận cần xóa."));
            }

            if (productComment.UserId != currentUserId && !isAdminOrStaff)
            {
                return Result.Failure<Unit>(Error.Forbidden(
                    "Comment.Forbidden",
                    "Bạn không có quyền xóa bình luận này."));
            }

            _context.Comments.Remove(productComment);
        }

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(Unit.Value);
    }
}
