using FluentValidation;
using MediatR;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Products.Common;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Products.Commands;

public record CreateProductMediaUploadCommand(
    string FileName,
    string ContentType,
    long ContentLength
) : IRequest<Result<ProductMediaUploadDto>>;

public class CreateProductMediaUploadCommandValidator : AbstractValidator<CreateProductMediaUploadCommand>
{
    public const long DefaultMaxFileSizeBytes = 5242880; // 5 MiB

    public CreateProductMediaUploadCommandValidator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty().WithMessage("Tên tệp không được để trống.")
            .MaximumLength(255).WithMessage("Tên tệp không được vượt quá 255 ký tự.");

        RuleFor(x => x.ContentType)
            .NotEmpty().WithMessage("Loại nội dung (MIME type) không được để trống.")
            .Must(ProductMediaHelper.IsSupportedMimeType)
            .WithMessage("Chỉ hỗ trợ các định dạng ảnh: image/jpeg, image/png, image/webp.");

        RuleFor(x => x.ContentLength)
            .GreaterThan(0).WithMessage("Dung lượng tệp phải lớn hơn 0.")
            .LessThanOrEqualTo(DefaultMaxFileSizeBytes)
            .WithMessage($"Dung lượng tệp không được vượt quá {DefaultMaxFileSizeBytes} bytes (5 MiB).");
    }
}

public class CreateProductMediaUploadCommandHandler : IRequestHandler<CreateProductMediaUploadCommand, Result<ProductMediaUploadDto>>
{
    private readonly IProductMediaStorage _storage;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public CreateProductMediaUploadCommandHandler(
        IProductMediaStorage storage,
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _storage = storage;
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<ProductMediaUploadDto>> Handle(
        CreateProductMediaUploadCommand request,
        CancellationToken cancellationToken)
    {
        var authResult = await ProductMediaHelper.ValidateActiveContentManagerAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (!authResult.IsSuccess)
        {
            return Result.Failure<ProductMediaUploadDto>(authResult.Error);
        }

        if (!_storage.IsEnabled)
        {
            return Result.Failure<ProductMediaUploadDto>(Error.Unavailable(
                "ProductMedia.StorageDisabled",
                "Cloudflare R2 storage is disabled."));
        }

        var normalizedContentType = request.ContentType.Trim().ToLowerInvariant();
        var objectKey = ProductMediaHelper.GenerateObjectKey(normalizedContentType);

        return await _storage.CreatePresignedUploadUrlAsync(
            objectKey,
            normalizedContentType,
            cancellationToken);
    }
}
