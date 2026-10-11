using FluentValidation;
using MediatR;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Products.Common;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Products.Commands;

public record ConfirmProductMediaUploadCommand(string ObjectKey) : IRequest<Result<ProductMediaDto>>;

public class ConfirmProductMediaUploadCommandValidator : AbstractValidator<ConfirmProductMediaUploadCommand>
{
    public ConfirmProductMediaUploadCommandValidator()
    {
        RuleFor(x => x.ObjectKey)
            .NotEmpty().WithMessage("Mã đối tượng (ObjectKey) không được để trống.")
            .Must(ProductMediaHelper.IsValidManagedObjectKey)
            .WithMessage("Mã đối tượng (ObjectKey) không hợp lệ hoặc không thuộc tiền tố quản lý.");
    }
}

public class ConfirmProductMediaUploadCommandHandler : IRequestHandler<ConfirmProductMediaUploadCommand, Result<ProductMediaDto>>
{
    public const long DefaultMaxFileSizeBytes = 5242880; // 5 MiB

    private readonly IProductMediaStorage _storage;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public ConfirmProductMediaUploadCommandHandler(
        IProductMediaStorage storage,
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _storage = storage;
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<ProductMediaDto>> Handle(
        ConfirmProductMediaUploadCommand request,
        CancellationToken cancellationToken)
    {
        var authResult = await ProductMediaHelper.ValidateActiveContentManagerAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (!authResult.IsSuccess)
        {
            return Result.Failure<ProductMediaDto>(authResult.Error);
        }

        if (!_storage.IsEnabled)
        {
            return Result.Failure<ProductMediaDto>(Error.Unavailable(
                "ProductMedia.StorageDisabled",
                "Cloudflare R2 storage is disabled."));
        }

        if (!ProductMediaHelper.IsValidManagedObjectKey(request.ObjectKey))
        {
            return Result.Failure<ProductMediaDto>(Error.Validation(
                "ProductMedia.InvalidKey",
                "Mã đối tượng (ObjectKey) không hợp lệ hoặc không thuộc tiền tố quản lý."));
        }

        var metaResult = await _storage.GetObjectMetadataAsync(request.ObjectKey, cancellationToken);
        if (!metaResult.IsSuccess)
        {
            return Result.Failure<ProductMediaDto>(metaResult.Error);
        }

        var metadata = metaResult.Value;
        var ext = Path.GetExtension(request.ObjectKey);
        var expectedMime = ProductMediaHelper.GetExpectedMimeTypeForExtension(ext);

        var isMimeValid = ProductMediaHelper.IsSupportedMimeType(metadata.ContentType) &&
                          string.Equals(metadata.ContentType, expectedMime, StringComparison.OrdinalIgnoreCase);

        var isSizeValid = metadata.ContentLength > 0 && metadata.ContentLength <= DefaultMaxFileSizeBytes;

        if (!isMimeValid || !isSizeValid)
        {
            // Best-effort cleanup of invalid object without masking main validation error
            try
            {
                await _storage.DeleteObjectAsync(request.ObjectKey, cancellationToken);
            }
            catch
            {
                // Best-effort ignore cleanup failure
            }

            return Result.Failure<ProductMediaDto>(Error.Validation(
                "ProductMedia.InvalidMetadata",
                "Metadata của tệp tải lên không hợp lệ hoặc vượt quá kích thước cho phép."));
        }

        var publicUrl = _storage.GetPublicUrl(request.ObjectKey);

        return Result.Success(new ProductMediaDto(
            request.ObjectKey,
            publicUrl,
            metadata.ContentType,
            metadata.ContentLength,
            metadata.ETag));
    }
}
