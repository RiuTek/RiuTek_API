using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Authorization;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Mappings;
using RiuTek.Application.Common.Utils;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Categories.Commands;

public record CreateCategoryCommand(
    string Name,
    ComponentType ComponentType,
    string? Description = null,
    Guid? ParentId = null
) : IRequest<Result<CategoryDto>>;

public class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên danh mục không được để trống.")
            .MaximumLength(150).WithMessage("Tên danh mục không được vượt quá 150 ký tự.");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Mô tả danh mục không được vượt quá 500 ký tự.");

        RuleFor(x => x.ComponentType)
            .IsInEnum().WithMessage("Loại linh kiện không hợp lệ.");

        RuleFor(x => x.ParentId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Id danh mục cha không hợp lệ.");
    }
}

public class CreateCategoryCommandHandler : IRequestHandler<CreateCategoryCommand, Result<CategoryDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public CreateCategoryCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<CategoryDto>> Handle(
        CreateCategoryCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
        {
            return Result.Failure<CategoryDto>(Error.Unauthorized(
                "Auth.Unauthorized",
                "Bạn cần đăng nhập để tạo danh mục."));
        }

        if (!AuthorizationRules.IsContentManager(_currentUserService.UserRole))
        {
            return Result.Failure<CategoryDto>(Error.Forbidden(
                "Category.Forbidden",
                "Bạn không có quyền tạo danh mục."));
        }

        var normalizedName = request.Name.Trim();

        if (request.ParentId.HasValue)
        {
            var parent = await _context.Categories
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == request.ParentId.Value, cancellationToken);

            if (parent == null)
            {
                return Result.Failure<CategoryDto>(Error.NotFound(
                    "Category.ParentNotFound",
                    "Danh mục cha không tồn tại."));
            }

            if (parent.ComponentType != request.ComponentType)
            {
                return Result.Failure<CategoryDto>(Error.Validation(
                    "Category.ComponentTypeMismatch",
                    "Loại linh kiện của danh mục con phải trùng khớp với danh mục cha."));
            }
        }

        var slug = SlugHelper.GenerateSlug(normalizedName);
        if (string.IsNullOrWhiteSpace(slug))
        {
            return Result.Failure<CategoryDto>(Error.Validation(
                "Category.InvalidSlug",
                "Không thể tạo slug từ tên danh mục."));
        }

        var slugLower = slug.ToLowerInvariant();
        var slugExists = await _context.Categories
            .AnyAsync(c => c.Slug.ToLower() == slugLower, cancellationToken);

        if (slugExists)
        {
            return Result.Failure<CategoryDto>(Error.Conflict(
                "Category.SlugConflict",
                "Danh mục với slug này đã tồn tại."));
        }

        var normalizedDescription = string.IsNullOrWhiteSpace(request.Description)
            ? null
            : request.Description.Trim();

        var category = new Category(
            name: normalizedName,
            slug: slug,
            componentType: request.ComponentType,
            description: normalizedDescription,
            parentId: request.ParentId
        );

        _context.Categories.Add(category);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(category.ToDto());
    }
}
