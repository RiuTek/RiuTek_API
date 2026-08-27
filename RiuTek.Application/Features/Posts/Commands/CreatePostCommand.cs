using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Mappings;
using RiuTek.Application.Common.Utils;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Features.Posts.Commands;

public record CreatePostCommand(
    string Title,
    string Summary,
    string Content,
    string? ThumbnailUrl,
    bool IsPublished = false,
    bool IsFeatured = false
) : IRequest<Result<PostDto>>;

public class CreatePostCommandValidator : AbstractValidator<CreatePostCommand>
{
    public CreatePostCommandValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Tiêu đề bài viết không được để trống.")
            .MaximumLength(255).WithMessage("Tiêu đề bài viết không được vượt quá 255 ký tự.");

        RuleFor(x => x.Summary)
            .MaximumLength(500).WithMessage("Tóm tắt bài viết không được vượt quá 500 ký tự.");

        RuleFor(x => x.Content)
            .NotEmpty().WithMessage("Nội dung bài viết không được để trống.");
    }
}

public class CreatePostCommandHandler : IRequestHandler<CreatePostCommand, Result<PostDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICacheService? _cacheService;

    public CreatePostCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        ICacheService? cacheService = null)
    {
        _context = context;
        _currentUserService = currentUserService;
        _cacheService = cacheService;
    }

    public async Task<Result<PostDto>> Handle(
        CreatePostCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
        {
            return Result.Failure<PostDto>(Error.Unauthorized(
                "Auth.Unauthorized",
                "Bạn cần đăng nhập để tạo bài viết."));
        }

        var authorId = _currentUserService.UserId.Value;
        var author = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == authorId, cancellationToken);

        if (author == null)
        {
            return Result.Failure<PostDto>(Error.NotFound(
                "User.NotFound",
                "Không tìm thấy tài khoản tác giả."));
        }

        var baseSlug = SlugHelper.GenerateSlug(request.Title);
        var slug = baseSlug;
        var count = 1;

        while (await _context.Posts.AnyAsync(p => p.Slug == slug, cancellationToken))
        {
            slug = $"{baseSlug}-{count}";
            count++;
        }

        var post = new Post
        {
            Title = request.Title.Trim(),
            Slug = slug,
            Summary = request.Summary.Trim(),
            Content = request.Content.Trim(),
            ThumbnailUrl = request.ThumbnailUrl?.Trim(),
            AuthorId = authorId,
            Author = author,
            IsPublished = request.IsPublished,
            IsFeatured = request.IsFeatured,
            PublishedAt = request.IsPublished ? DateTime.UtcNow : null,
            ViewCount = 0
        };

        _context.Posts.Add(post);
        await _context.SaveChangesAsync(cancellationToken);

        if (_cacheService != null)
        {
            await _cacheService.RemoveByPrefixAsync("posts_", cancellationToken);
        }

        return Result.Success(post.ToDto());
    }
}
