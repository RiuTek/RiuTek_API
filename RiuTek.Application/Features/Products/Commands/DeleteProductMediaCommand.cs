using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Products.Common;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Products.Commands;

public record DeleteProductMediaCommand(string ObjectKey) : IRequest<Result>;

public class DeleteProductMediaCommandValidator : AbstractValidator<DeleteProductMediaCommand>
{
    public DeleteProductMediaCommandValidator()
    {
        RuleFor(x => x.ObjectKey)
            .NotEmpty().WithMessage("Mã đối tượng (ObjectKey) không được để trống.")
            .Must(ProductMediaHelper.IsValidManagedObjectKey)
            .WithMessage("Mã đối tượng (ObjectKey) không hợp lệ hoặc không thuộc tiền tố quản lý.");
    }
}

public class DeleteProductMediaCommandHandler : IRequestHandler<DeleteProductMediaCommand, Result>
{
    private readonly IProductMediaStorage _storage;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public DeleteProductMediaCommandHandler(
        IProductMediaStorage storage,
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _storage = storage;
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result> Handle(
        DeleteProductMediaCommand request,
        CancellationToken cancellationToken)
    {
        var authResult = await ProductMediaHelper.ValidateActiveContentManagerAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (!authResult.IsSuccess)
        {
            return Result.Failure(authResult.Error);
        }

        if (!_storage.IsEnabled)
        {
            return Result.Failure(Error.Unavailable(
                "ProductMedia.StorageDisabled",
                "Cloudflare R2 storage is disabled."));
        }

        if (!ProductMediaHelper.IsValidManagedObjectKey(request.ObjectKey))
        {
            return Result.Failure(Error.Validation(
                "ProductMedia.InvalidKey",
                "Mã đối tượng (ObjectKey) không hợp lệ hoặc không thuộc tiền tố quản lý."));
        }

        var publicUrl = _storage.GetPublicUrl(request.ObjectKey);

        var isInUse = await _context.Products
            .AsNoTracking()
            .AnyAsync(p => p.ImageUrl == publicUrl || p.AdditionalImages.Contains(publicUrl), cancellationToken);

        if (isInUse)
        {
            return Result.Failure(Error.Conflict(
                "ProductMedia.InUse",
                "Media is currently in use by one or more products."));
        }

        return await _storage.DeleteObjectAsync(request.ObjectKey, cancellationToken);
    }
}
