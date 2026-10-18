using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Authorization;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Mappings;
using RiuTek.Application.Common.Utils;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Products.Common;
using RiuTek.Application.Features.Products.Validation;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Products.Commands;

public record CreateProductCommand(
    Guid CategoryId,
    string Name,
    string Sku,
    string Brand,
    decimal Price,
    decimal? OriginalPrice,
    int StockQuantity,
    string ImageUrl,
    IReadOnlyList<string>? AdditionalImages,
    ComponentType ComponentType,
    ComponentSpecification Specifications
) : IRequest<Result<ProductDto>>;

public class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
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
            .MaximumLength(1000).WithMessage("Đường dẫn ảnh chính không được vượt quá 1000 ký tự.")
            .Must(ProductMediaHelper.IsValidHttpsUrl).WithMessage("Ảnh sản phẩm chính phải là đường dẫn HTTPS hợp lệ.");

        When(x => x.AdditionalImages != null, () =>
        {
            RuleForEach(x => x.AdditionalImages)
                .NotEmpty().WithMessage("Đường dẫn ảnh phụ không được để trống.")
                .MaximumLength(1000).WithMessage("Đường dẫn ảnh phụ không được vượt quá 1000 ký tự.")
                .Must(ProductMediaHelper.IsValidHttpsUrl).WithMessage("Đường dẫn ảnh phụ phải là đường dẫn HTTPS hợp lệ.");

            RuleFor(x => x.AdditionalImages)
                .Must((cmd, images) =>
                {
                    if (images == null) return true;
                    return !images.Any(img =>
                        !string.IsNullOrWhiteSpace(img) &&
                        string.Equals(img.Trim(), cmd.ImageUrl?.Trim(), StringComparison.OrdinalIgnoreCase));
                })
                .WithMessage("Ảnh phụ không được trùng với ảnh chính.");

            RuleFor(x => x.AdditionalImages)
                .Must((cmd, images) =>
                {
                    if (images == null) return true;
                    var normalized = ProductMediaHelper.NormalizeAdditionalImages(cmd.ImageUrl, images);
                    return normalized.Count <= 10;
                })
                .WithMessage("Tối đa 10 ảnh phụ.");
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

public class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, Result<ProductDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public CreateProductCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<ProductDto>> Handle(
        CreateProductCommand request,
        CancellationToken cancellationToken)
    {
        var authResult = await ProductMediaHelper.ValidateActiveContentManagerAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (authResult.IsFailure)
        {
            return Result.Failure<ProductDto>(authResult.Error);
        }

        var actorUserId = authResult.Value;

        var category = await _context.Categories
            .FirstOrDefaultAsync(c => c.Id == request.CategoryId, cancellationToken);

        if (category == null)
        {
            return Result.Failure<ProductDto>(Error.NotFound(
                "Product.CategoryNotFound",
                "Danh mục không tồn tại."));
        }

        if (category.ComponentType != request.ComponentType)
        {
            return Result.Failure<ProductDto>(Error.Validation(
                "Product.CategoryComponentTypeMismatch",
                "Loại linh kiện của sản phẩm không trùng khớp với danh mục."));
        }

        var normalizedSku = request.Sku.Trim().ToUpperInvariant();
        var skuExists = await _context.Products
            .AnyAsync(p => p.Sku.ToUpper() == normalizedSku, cancellationToken);

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
            .AnyAsync(p => p.Slug.ToLower() == slugLower, cancellationToken);

        if (slugExists)
        {
            return Result.Failure<ProductDto>(Error.Conflict(
                "Product.SlugConflict",
                "Slug của sản phẩm này đã tồn tại."));
        }

        var cleanImages = ProductMediaHelper.NormalizeAdditionalImages(request.ImageUrl, request.AdditionalImages);

        var product = new Product(
            categoryId: category.Id,
            name: normalizedName,
            slug: slug,
            sku: normalizedSku,
            brand: request.Brand.Trim(),
            price: request.Price,
            stockQuantity: request.StockQuantity,
            imageUrl: request.ImageUrl.Trim(),
            componentType: request.ComponentType,
            specifications: request.Specifications,
            originalPrice: request.OriginalPrice
        )
        {
            AdditionalImages = cleanImages,
            Category = category,
            Embedding = null,
            IsActive = true
        };

        _context.Products.Add(product);

        if (request.StockQuantity > 0)
        {
            var initialMovement = new StockMovement(
                productId: product.Id,
                quantityDelta: request.StockQuantity,
                stockBefore: 0,
                stockAfter: request.StockQuantity,
                reason: StockMovementReason.ProductCreated,
                note: null,
                actorUserId: actorUserId,
                orderId: null);
            _context.StockMovements.Add(initialMovement);
        }

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(product.ToDto());
    }
}
