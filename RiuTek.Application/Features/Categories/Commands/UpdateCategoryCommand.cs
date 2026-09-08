using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Authorization;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Mappings;
using RiuTek.Application.Common.Utils;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Categories.Commands;

public record UpdateCategoryCommand(
    Guid Id,
    string Name,
    ComponentType ComponentType,
    string? Description,
    Guid? ParentId
) : IRequest<Result<CategoryDto>>;

public class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id danh mục không hợp lệ.");

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

public class UpdateCategoryCommandHandler : IRequestHandler<UpdateCategoryCommand, Result<CategoryDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public UpdateCategoryCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<CategoryDto>> Handle(
        UpdateCategoryCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
        {
            return Result.Failure<CategoryDto>(Error.Unauthorized(
                "Auth.Unauthorized",
                "Bạn cần đăng nhập để cập nhật danh mục."));
        }

        if (!AuthorizationRules.IsContentManager(_currentUserService.UserRole))
        {
            return Result.Failure<CategoryDto>(Error.Forbidden(
                "Category.Forbidden",
                "Bạn không có quyền chỉnh sửa danh mục."));
        }

        var category = await _context.Categories
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);

        if (category == null)
        {
            return Result.Failure<CategoryDto>(Error.NotFound(
                "Category.NotFound",
                "Không tìm thấy danh mục."));
        }

        if (request.ParentId.HasValue)
        {
            if (request.ParentId.Value == request.Id)
            {
                return Result.Failure<CategoryDto>(Error.Validation(
                    "Category.SelfParent",
                    "Danh mục không thể là cha của chính nó."));
            }

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

            // Cycle detection: walk up from parent to root
            var currentParentId = (Guid?)request.ParentId.Value;
            var visited = new HashSet<Guid> { request.Id };

            while (currentParentId.HasValue)
            {
                if (currentParentId.Value == request.Id || !visited.Add(currentParentId.Value))
                {
                    return Result.Failure<CategoryDto>(Error.Validation(
                        "Category.CycleDetected",
                        "Không thể thiết lập danh mục cha vì sẽ tạo thành vòng lặp phân cấp."));
                }

                var parentNode = await _context.Categories
                    .AsNoTracking()
                    .Where(c => c.Id == currentParentId.Value)
                    .Select(c => new { c.Id, c.ParentId })
                    .FirstOrDefaultAsync(cancellationToken);

                if (parentNode == null)
                    break;

                currentParentId = parentNode.ParentId;
            }
        }

        // If ComponentType changed, ensure no products or subcategories exist
        if (request.ComponentType != category.ComponentType)
        {
            var hasProducts = await _context.Products
                .AnyAsync(p => p.CategoryId == request.Id, cancellationToken);

            if (hasProducts)
            {
                return Result.Failure<CategoryDto>(Error.Conflict(
                    "Category.HasProducts",
                    "Không thể thay đổi loại linh kiện vì danh mục đang chứa sản phẩm."));
            }

            var hasSubCategories = await _context.Categories
                .AnyAsync(c => c.ParentId == request.Id, cancellationToken);

            if (hasSubCategories)
            {
                return Result.Failure<CategoryDto>(Error.Conflict(
                    "Category.HasSubCategories",
                    "Không thể thay đổi loại linh kiện vì danh mục đang có danh mục con."));
            }
        }

        var normalizedName = request.Name.Trim();
        var slug = SlugHelper.GenerateSlug(normalizedName);
        if (string.IsNullOrWhiteSpace(slug))
        {
            return Result.Failure<CategoryDto>(Error.Validation(
                "Category.InvalidSlug",
                "Không thể tạo slug từ tên danh mục."));
        }

        var slugLower = slug.ToLowerInvariant();
        var slugExists = await _context.Categories
            .AnyAsync(c => c.Slug.ToLower() == slugLower && c.Id != request.Id, cancellationToken);

        if (slugExists)
        {
            return Result.Failure<CategoryDto>(Error.Conflict(
                "Category.SlugConflict",
                "Danh mục với slug này đã tồn tại."));
        }

        category.Name = normalizedName;
        category.Slug = slug;
        category.ComponentType = request.ComponentType;
        category.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        category.ParentId = request.ParentId;
        category.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(category.ToDto());
    }
}
