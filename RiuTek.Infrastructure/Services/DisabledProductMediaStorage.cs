using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;

namespace RiuTek.Infrastructure.Services;

public class DisabledProductMediaStorage : IProductMediaStorage
{
    private static readonly Error StorageDisabledError = Error.Unavailable(
        "ProductMedia.StorageDisabled",
        "Cloudflare R2 storage is disabled.");

    public bool IsEnabled => false;

    public string GetPublicUrl(string objectKey) => string.Empty;

    public Task<Result<ProductMediaUploadDto>> CreatePresignedUploadUrlAsync(
        string objectKey,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure<ProductMediaUploadDto>(StorageDisabledError));
    }

    public Task<Result<ProductMediaMetadata>> GetObjectMetadataAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure<ProductMediaMetadata>(StorageDisabledError));
    }

    public Task<Result> DeleteObjectAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure(StorageDisabledError));
    }
}
