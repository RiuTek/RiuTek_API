using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Mappings;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Comments.Commands;

public record CreateProductCommentCommand(
    Guid ProductId,
    string Content,
    Guid? ParentCommentId = null
) : IRequest<Result<ProductCommentDto>>;

public class CreateProductCommentCommandValidator : AbstractValidator<CreateProductCommentCommand>
{
    public CreateProductCommentCommandValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("Id sản phẩm không hợp lệ.");

        RuleFor(x => x.Content)
            .NotEmpty().WithMessage("Nội dung bình luận không được để trống.")
            .MaximumLength(1000).WithMessage("Nội dung bình luận không được vượt quá 1000 ký tự.");
    }
}

public class CreateProductCommentCommandHandler : IRequestHandler<CreateProductCommentCommand, Result<ProductCommentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public CreateProductCommentCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<ProductCommentDto>> Handle(
        CreateProductCommentCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
        {
            return Result.Failure<ProductCommentDto>(Error.Unauthorized(
                "Auth.Unauthorized",
                "Bạn cần đăng nhập để bình luận."));
        }

        var userId = _currentUserService.UserId.Value;
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
        {
            return Result.Failure<ProductCommentDto>(Error.NotFound(
                "User.NotFound",
                "Không tìm thấy tài khoản người dùng."));
        }

        var productExists = await _context.Products
            .AnyAsync(p => p.Id == request.ProductId, cancellationToken);

        if (!productExists)
        {
            return Result.Failure<ProductCommentDto>(Error.NotFound(
                "Product.NotFound",
                "Sản phẩm không tồn tại."));
        }

        if (request.ParentCommentId.HasValue)
        {
            var parentComment = await _context.Comments
                .FirstOrDefaultAsync(c => c.Id == request.ParentCommentId.Value && c.ProductId == request.ProductId, cancellationToken);

            if (parentComment == null)
            {
                return Result.Failure<ProductCommentDto>(Error.NotFound(
                    "Comment.ParentNotFound",
                    "Bình luận cha không tồn tại hoặc không thuộc sản phẩm này."));
            }
        }

        var userRole = _currentUserService.UserRole;
        var isStaffAnswer = userRole == UserRole.Admin.ToString() || userRole == UserRole.Staff.ToString();

        var comment = new Comment(
            productId: request.ProductId,
            userId: userId,
            content: request.Content.Trim(),
            parentCommentId: request.ParentCommentId,
            isStaffAnswer: isStaffAnswer
        )
        {
            User = user
        };

        _context.Comments.Add(comment);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(comment.ToProductDto());
    }
}
