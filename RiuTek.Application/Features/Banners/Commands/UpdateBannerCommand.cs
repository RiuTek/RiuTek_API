using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Banners.Common;
using RiuTek.Application.Features.Products.Common;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Banners.Commands;

public record UpdateBannerCommand(
    Guid Id,
    string Title,
    string ImageObjectKey,
    string TargetUrl,
    int DisplayOrder,
    bool IsActive) : IRequest<Result<ManagedBannerDto>>;

public class UpdateBannerCommandValidator : AbstractValidator<UpdateBannerCommand>
{
    public UpdateBannerCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id banner không được để trống.");

        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Tiêu đề banner không được để trống.")
            .MaximumLength(160).WithMessage("Tiêu đề banner không được vượt quá 160 ký tự.");

        RuleFor(x => x.ImageObjectKey)
            .NotEmpty().WithMessage("Mã đối tượng ảnh (ImageObjectKey) không được để trống.")
            .MaximumLength(512).WithMessage("Mã đối tượng ảnh không được vượt quá 512 ký tự.")
            .Must(ProductMediaHelper.IsValidManagedObjectKey)
            .WithMessage("Mã đối tượng ảnh (ImageObjectKey) không hợp lệ hoặc không thuộc tiền tố quản lý.");

        RuleFor(x => x.TargetUrl)
            .NotEmpty().WithMessage("Đường dẫn đích (TargetUrl) không được để trống.")
            .MaximumLength(2048).WithMessage("Đường dẫn đích không được vượt quá 2048 ký tự.")
            .Must(BannerHelper.IsValidInternalTargetUrl)
            .WithMessage("Đường dẫn đích phải là đường dẫn nội bộ hợp lệ bắt đầu bằng '/' và không chứa ký tự nguy hiểm.");

        RuleFor(x => x.DisplayOrder)
            .GreaterThanOrEqualTo(0).WithMessage("Thứ tự hiển thị phải lớn hơn hoặc bằng 0.");
    }
}

public class UpdateBannerCommandHandler : IRequestHandler<UpdateBannerCommand, Result<ManagedBannerDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IProductMediaStorage _storage;
    private readonly ICurrentUserService _currentUserService;

    public UpdateBannerCommandHandler(
        IApplicationDbContext context,
        IProductMediaStorage storage,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _storage = storage;
        _currentUserService = currentUserService;
    }

    public async Task<Result<ManagedBannerDto>> Handle(
        UpdateBannerCommand request,
        CancellationToken cancellationToken)
    {
        var authResult = await ProductMediaHelper.ValidateActiveContentManagerAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (!authResult.IsSuccess)
        {
            return Result.Failure<ManagedBannerDto>(authResult.Error);
        }

        var banner = await _context.Banners
            .FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken);

        if (banner == null)
        {
            return Result.Failure<ManagedBannerDto>(Error.NotFound(
                "Banner.NotFound",
                "Banner không tồn tại."));
        }

        var normalizedKey = request.ImageObjectKey.Trim();

        if (!string.Equals(banner.ImageObjectKey, normalizedKey, StringComparison.Ordinal))
        {
            if (!_storage.IsEnabled)
            {
                return Result.Failure<ManagedBannerDto>(Error.Unavailable(
                    "ProductMedia.StorageDisabled",
                    "Cloudflare R2 storage is disabled."));
            }

            var metaResult = await _storage.GetObjectMetadataAsync(normalizedKey, cancellationToken);
            if (!metaResult.IsSuccess)
            {
                return Result.Failure<ManagedBannerDto>(metaResult.Error);
            }

            var metadata = metaResult.Value;
            var ext = Path.GetExtension(normalizedKey);
            var expectedMime = ProductMediaHelper.GetExpectedMimeTypeForExtension(ext);

            var isMimeValid = ProductMediaHelper.IsSupportedMimeType(metadata.ContentType) &&
                              string.Equals(metadata.ContentType, expectedMime, StringComparison.OrdinalIgnoreCase);
            var isSizeValid = metadata.ContentLength > 0 && metadata.ContentLength <= _storage.MaxFileSizeBytes;

            if (!isMimeValid || !isSizeValid)
            {
                return Result.Failure<ManagedBannerDto>(Error.Validation(
                    "ProductMedia.InvalidMetadata",
                    "Metadata của tệp tải lên không hợp lệ hoặc vượt quá kích thước cho phép."));
            }

            banner.ImageObjectKey = normalizedKey;
            banner.ImageUrl = _storage.GetPublicUrl(normalizedKey);
        }

        banner.Title = request.Title.Trim();
        banner.TargetUrl = request.TargetUrl.Trim();
        banner.DisplayOrder = request.DisplayOrder;
        banner.IsActive = request.IsActive;
        banner.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new ManagedBannerDto(
            banner.Id,
            banner.Title,
            banner.ImageObjectKey,
            banner.ImageUrl,
            banner.TargetUrl,
            banner.DisplayOrder,
            banner.IsActive,
            banner.CreatedAt,
            banner.UpdatedAt));
    }
}
