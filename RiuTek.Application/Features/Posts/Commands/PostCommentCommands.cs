using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Mappings;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Posts.Commands;

public record CreatePostCommentCommand(
    Guid PostId,
    string Content,
    Guid? ParentCommentId = null
) : IRequest<Result<PostCommentDto>>;

public class CreatePostCommentCommandValidator : AbstractValidator<CreatePostCommentCommand>
{
    public CreatePostCommentCommandValidator()
    {
        RuleFor(x => x.PostId)
            .NotEmpty().WithMessage("Id bài viết không hợp lệ.");

        RuleFor(x => x.Content)
            .NotEmpty().WithMessage("Nội dung bình luận không được để trống.")
            .MaximumLength(1000).WithMessage("Nội dung bình luận không được vượt quá 1000 ký tự.");
    }
}

public class CreatePostCommentCommandHandler : IRequestHandler<CreatePostCommentCommand, Result<PostCommentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public CreatePostCommentCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<PostCommentDto>> Handle(
        CreatePostCommentCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
        {
            return Result.Failure<PostCommentDto>(Error.Unauthorized(
                "Auth.Unauthorized",
                "Bạn cần đăng nhập để bình luận."));
        }

        var userId = _currentUserService.UserId.Value;
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
        {
            return Result.Failure<PostCommentDto>(Error.NotFound(
                "User.NotFound",
                "Không tìm thấy tài khoản người dùng."));
        }

        var postExists = await _context.Posts
            .AnyAsync(p => p.Id == request.PostId, cancellationToken);

        if (!postExists)
        {
            return Result.Failure<PostCommentDto>(Error.NotFound(
                "Post.NotFound",
                "Bài viết không tồn tại."));
        }

        if (request.ParentCommentId.HasValue)
        {
            var parentComment = await _context.PostComments
                .FirstOrDefaultAsync(c => c.Id == request.ParentCommentId.Value && c.PostId == request.PostId, cancellationToken);

            if (parentComment == null)
            {
                return Result.Failure<PostCommentDto>(Error.NotFound(
                    "PostComment.ParentNotFound",
                    "Bình luận cha không tồn tại hoặc không thuộc bài viết này."));
            }
        }

        var comment = new PostComment
        {
            PostId = request.PostId,
            UserId = userId,
            User = user,
            Content = request.Content.Trim(),
            ParentCommentId = request.ParentCommentId
        };

        _context.PostComments.Add(comment);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(comment.ToDto());
    }
}

public record DeletePostCommentCommand(Guid Id) : IRequest<Result<Unit>>;

public class DeletePostCommentCommandHandler : IRequestHandler<DeletePostCommentCommand, Result<Unit>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public DeletePostCommentCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<Unit>> Handle(
        DeletePostCommentCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
        {
            return Result.Failure<Unit>(Error.Unauthorized(
                "Auth.Unauthorized",
                "Bạn cần đăng nhập để xóa bình luận."));
        }

        var comment = await _context.PostComments
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);

        if (comment == null)
        {
            return Result.Failure<Unit>(Error.NotFound(
                "PostComment.NotFound",
                "Không tìm thấy bình luận cần xóa."));
        }

        var currentUserId = _currentUserService.UserId.Value;
        var userRole = _currentUserService.UserRole;
        var isAdminOrStaff = userRole == UserRole.Admin.ToString() || userRole == UserRole.Staff.ToString();

        if (comment.UserId != currentUserId && !isAdminOrStaff)
        {
            return Result.Failure<Unit>(Error.Forbidden(
                "PostComment.Forbidden",
                "Bạn không có quyền xóa bình luận này."));
        }

        _context.PostComments.Remove(comment);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(Unit.Value);
    }
}
