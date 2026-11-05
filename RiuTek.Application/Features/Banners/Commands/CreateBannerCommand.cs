using FluentValidation;
using MediatR;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Banners.Common;
using RiuTek.Application.Features.Products.Common;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Features.Banners.Commands;

public record CreateBannerCommand(
    string Title,
    string ImageObjectKey,
    string TargetUrl,
    int DisplayOrder = 0,
    bool IsActive = false) : IRequest<Result<ManagedBannerDto>>;

public class CreateBannerCommandValidator : AbstractValidator<CreateBannerCommand>
{
    public CreateBannerCommandValidator()
    {
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

public class CreateBannerCommandHandler : IRequestHandler<CreateBannerCommand, Result<ManagedBannerDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IProductMediaStorage _storage;
    private readonly ICurrentUserService _currentUserService;

    public CreateBannerCommandHandler(
        IApplicationDbContext context,
        IProductMediaStorage storage,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _storage = storage;
        _currentUserService = currentUserService;
    }

    public async Task<Result<ManagedBannerDto>> Handle(
        CreateBannerCommand request,
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

        if (!_storage.IsEnabled)
        {
            return Result.Failure<ManagedBannerDto>(Error.Unavailable(
                "ProductMedia.StorageDisabled",
                "Cloudflare R2 storage is disabled."));
        }

        var normalizedKey = request.ImageObjectKey.Trim();
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

        var imageUrl = _storage.GetPublicUrl(normalizedKey);

        var banner = new Banner(
            title: request.Title.Trim(),
            imageObjectKey: normalizedKey,
            imageUrl: imageUrl,
            targetUrl: request.TargetUrl.Trim(),
            displayOrder: request.DisplayOrder,
            isActive: request.IsActive);

        _context.Banners.Add(banner);
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
