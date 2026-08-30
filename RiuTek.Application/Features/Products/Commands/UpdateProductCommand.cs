using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Authorization;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Mappings;
using RiuTek.Application.Common.Utils;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Products.Validation;
using RiuTek.Core.Common;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Products.Commands;

public record UpdateProductCommand(
    Guid Id,
    Guid CategoryId,
    string Name,
    string Sku,
    string Brand,
    decimal Price,
    decimal? OriginalPrice,
    int StockQuantity,
    bool IsActive,
    string ImageUrl,
    IReadOnlyList<string>? AdditionalImages,
    ComponentType ComponentType,
    ComponentSpecification Specifications
) : IRequest<Result<ProductDto>>;

public class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id sản phẩm không hợp lệ.");

        RuleFor(x => x.CategoryId)
            .NotEmpty().WithMessage("Id danh mục không hợp lệ.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên sản phẩm không được để trống.")
            .MaximumLength(255).WithMessage("Tên sản phẩm không được vượt quá 255 ký tự.");

        RuleFor(x => x.Sku)
            .NotEmpty().WithMessage("Mã SKU không được để trống.")
            .MaximumLength(100).WithMessage("Mã SKU không được vượt quá 100 ký tự.");

        RuleFor(x => x.Brand)
            .NotEmpty().WithMessage("Thương hiệu không được để trống.")
            .MaximumLength(100).WithMessage("Thương hiệu không được vượt quá 100 ký tự.");

        RuleFor(x => x.Price)
            .GreaterThan(0).WithMessage("Giá sản phẩm phải lớn hơn 0.");

        RuleFor(x => x.OriginalPrice)
            .GreaterThanOrEqualTo(x => x.Price)
            .When(x => x.OriginalPrice.HasValue)
            .WithMessage("Giá gốc (nếu có) phải lớn hơn hoặc bằng giá bán.");

        RuleFor(x => x.StockQuantity)
            .GreaterThanOrEqualTo(0).WithMessage("Số lượng tồn kho không được âm.");

        RuleFor(x => x.ImageUrl)
            .NotEmpty().WithMessage("Ảnh sản phẩm chính không được để trống.")
            .MaximumLength(1000).WithMessage("Đường dẫn ảnh chính không được vượt quá 1000 ký tự.");

        When(x => x.AdditionalImages != null, () =>
        {
            RuleFor(x => x.AdditionalImages)
                .Must(imgs => imgs!.Count <= 10).WithMessage("Tối đa 10 ảnh phụ.");

            RuleForEach(x => x.AdditionalImages)
                .NotEmpty().WithMessage("Đường dẫn ảnh phụ không được để trống.")
                .MaximumLength(1000).WithMessage("Đường dẫn ảnh phụ không được vượt quá 1000 ký tự.");
        });

        RuleFor(x => x.ComponentType)
            .IsInEnum().WithMessage("Loại linh kiện không hợp lệ.");

        RuleFor(x => x.Specifications)
            .NotNull().WithMessage("Thông số kỹ thuật không được để trống.")
            .SetValidator(new ComponentSpecificationValidator());

        RuleFor(x => x)
            .Must(x => x.Specifications == null || x.Specifications.ComponentType == x.ComponentType)
            .WithMessage("Loại linh kiện trong thông số kỹ thuật phải trùng khớp với loại linh kiện của sản phẩm.");
    }
}

public class UpdateProductCommandHandler : IRequestHandler<UpdateProductCommand, Result<ProductDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public UpdateProductCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<ProductDto>> Handle(
        UpdateProductCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
        {
            return Result.Failure<ProductDto>(Error.Unauthorized(
                "Auth.Unauthorized",
                "Bạn cần đăng nhập để cập nhật sản phẩm."));
        }

        if (!AuthorizationRules.IsContentManager(_currentUserService.UserRole))
        {
            return Result.Failure<ProductDto>(Error.Forbidden(
                "Product.Forbidden",
                "Bạn không có quyền cập nhật sản phẩm."));
        }

        var product = await _context.Products
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        if (product == null)
        {
            return Result.Failure<ProductDto>(Error.NotFound(
                "Product.NotFound",
                "Không tìm thấy sản phẩm."));
        }

        var targetCategory = await _context.Categories
            .FirstOrDefaultAsync(c => c.Id == request.CategoryId, cancellationToken);

        if (targetCategory == null)
        {
            return Result.Failure<ProductDto>(Error.NotFound(
                "Product.CategoryNotFound",
                "Danh mục không tồn tại."));
        }

        if (targetCategory.ComponentType != request.ComponentType)
        {
            return Result.Failure<ProductDto>(Error.Validation(
                "Product.CategoryComponentTypeMismatch",
                "Loại linh kiện của sản phẩm không trùng khớp với danh mục."));
        }

        var normalizedSku = request.Sku.Trim().ToUpperInvariant();
        var skuExists = await _context.Products
            .AnyAsync(p => p.Sku.ToUpper() == normalizedSku && p.Id != request.Id, cancellationToken);

        if (skuExists)
        {
            return Result.Failure<ProductDto>(Error.Conflict(
                "Product.SkuConflict",
                "Mã SKU này đã tồn tại trên sản phẩm khác."));
        }

        var normalizedName = request.Name.Trim();
        var slug = SlugHelper.GenerateSlug(normalizedName);
        if (string.IsNullOrWhiteSpace(slug))
        {
            return Result.Failure<ProductDto>(Error.Validation(
                "Product.InvalidSlug",
                "Không thể tạo slug từ tên sản phẩm."));
        }

        var slugLower = slug.ToLowerInvariant();
        var slugExists = await _context.Products
            .AnyAsync(p => p.Slug.ToLower() == slugLower && p.Id != request.Id, cancellationToken);

        if (slugExists)
        {
            return Result.Failure<ProductDto>(Error.Conflict(
                "Product.SlugConflict",
                "Slug của sản phẩm này đã tồn tại."));
        }

        var cleanImages = request.AdditionalImages?
            .Where(img => !string.IsNullOrWhiteSpace(img))
            .Select(img => img.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        product.CategoryId = targetCategory.Id;
        product.Category = targetCategory;
        product.Name = normalizedName;
        product.Slug = slug;
        product.Sku = normalizedSku;
        product.Brand = request.Brand.Trim();
        product.Price = request.Price;
        product.OriginalPrice = request.OriginalPrice;
        product.StockQuantity = request.StockQuantity;
        product.IsActive = request.IsActive;
        product.ImageUrl = request.ImageUrl.Trim();
        product.AdditionalImages = cleanImages;
        product.ComponentType = request.ComponentType;
        product.Specifications = request.Specifications;
        product.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(product.ToDto());
    }
}
