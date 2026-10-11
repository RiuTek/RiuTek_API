using RiuTek.Application.DTOs;
using RiuTek.Core.Common;

namespace RiuTek.Application.Common.Interfaces;

public interface IProductMediaStorage
{
    bool IsEnabled { get; }

    Task<Result<ProductMediaUploadDto>> CreatePresignedUploadUrlAsync(
        string objectKey,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<Result<ProductMediaMetadata>> GetObjectMetadataAsync(
        string objectKey,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteObjectAsync(
        string objectKey,
        CancellationToken cancellationToken = default);

    string GetPublicUrl(string objectKey);
}
