using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Mappings;
using RiuTek.Application.Common.Utils;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Posts.Commands;

public record UpdatePostCommand(
    Guid Id,
    string Title,
    string Summary,
    string Content,
    string? ThumbnailUrl,
    bool IsPublished,
    bool IsFeatured
) : IRequest<Result<PostDto>>;

public class UpdatePostCommandValidator : AbstractValidator<UpdatePostCommand>
{
    public UpdatePostCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id bài viết không hợp lệ.");

        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Tiêu đề bài viết không được để trống.")
            .MaximumLength(255).WithMessage("Tiêu đề bài viết không được vượt quá 255 ký tự.");

        RuleFor(x => x.Summary)
            .MaximumLength(500).WithMessage("Tóm tắt bài viết không được vượt quá 500 ký tự.");

        RuleFor(x => x.Content)
            .NotEmpty().WithMessage("Nội dung bài viết không được để trống.");
    }
}

public class UpdatePostCommandHandler : IRequestHandler<UpdatePostCommand, Result<PostDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICacheService _cacheService;

    public UpdatePostCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        ICacheService cacheService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _cacheService = cacheService;
    }

    public async Task<Result<PostDto>> Handle(
        UpdatePostCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
        {
            return Result.Failure<PostDto>(Error.Unauthorized(
                "Auth.Unauthorized",
                "Bạn cần đăng nhập để cập nhật bài viết."));
        }

        var userRole = _currentUserService.UserRole;
        var isAdminOrStaff = userRole == UserRole.Admin.ToString() || userRole == UserRole.Staff.ToString();

        if (!isAdminOrStaff)
        {
            return Result.Failure<PostDto>(Error.Forbidden(
                "Post.Forbidden",
                "Bạn không có quyền chỉnh sửa bài viết này."));
        }

        var post = await _context.Posts
            .Include(p => p.Author)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        if (post == null)
        {
            return Result.Failure<PostDto>(Error.NotFound(
                "Post.NotFound",
                "Không tìm thấy bài viết."));
        }

        var newTitle = request.Title.Trim();
        if (!string.Equals(post.Title, newTitle, StringComparison.OrdinalIgnoreCase))
        {
            var baseSlug = SlugHelper.GenerateSlug(newTitle);
            var slug = baseSlug;
            var count = 1;

            while (await _context.Posts.AnyAsync(p => p.Slug == slug && p.Id != post.Id, cancellationToken))
            {
                slug = $"{baseSlug}-{count}";
                count++;
            }

            post.Slug = slug;
        }

        post.Title = newTitle;
        post.Summary = request.Summary.Trim();
        post.Content = request.Content.Trim();
        post.ThumbnailUrl = request.ThumbnailUrl?.Trim();
        post.IsFeatured = request.IsFeatured;

        if (!post.IsPublished && request.IsPublished)
        {
            post.PublishedAt = DateTime.UtcNow;
        }
        post.IsPublished = request.IsPublished;
        post.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        await _cacheService.RemoveByPrefixAsync(PostCacheKeys.PostListPrefix, cancellationToken);

        return Result.Success(post.ToDto());
    }
}
