using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;
using RiuTek.Infrastructure.Settings;

namespace RiuTek.Infrastructure.Services;

public class R2ProductMediaStorage : IProductMediaStorage
{
    private readonly IAmazonS3 _s3Client;
    private readonly R2Settings _settings;
    private readonly ILogger<R2ProductMediaStorage> _logger;

    public R2ProductMediaStorage(
        R2Settings settings,
        ILogger<R2ProductMediaStorage> logger,
        IAmazonS3? s3Client = null)
    {
        _settings = settings;
        _logger = logger;

        if (s3Client != null)
        {
            _s3Client = s3Client;
        }
        else
        {
            var credentials = new BasicAWSCredentials(settings.AccessKeyId, settings.SecretAccessKey);
            var config = new AmazonS3Config
            {
                ServiceURL = settings.ServiceUrl,
                AuthenticationRegion = "auto",
                ForcePathStyle = true
            };
            _s3Client = new AmazonS3Client(credentials, config);
        }
    }

    public bool IsEnabled => true;
    public long MaxFileSizeBytes => _settings.MaxFileSizeBytes;

    public string GetPublicUrl(string objectKey)
    {
        var baseUrl = _settings.PublicBaseUrl.TrimEnd('/');
        var key = objectKey.TrimStart('/');
        return $"{baseUrl}/{key}";
    }

    public async Task<Result<ProductMediaUploadDto>> CreatePresignedUploadUrlAsync(
        string objectKey,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_settings.PresignedUrlExpiryMinutes);
            var request = new GetPreSignedUrlRequest
            {
                BucketName = _settings.BucketName,
                Key = objectKey,
                Verb = HttpVerb.PUT,
                Expires = expiresAt.UtcDateTime,
                ContentType = contentType
            };

            var uploadUrl = await Task.Run(() => _s3Client.GetPreSignedURL(request), cancellationToken);

            var publicUrl = GetPublicUrl(objectKey);
            var requiredHeaders = new Dictionary<string, string>
            {
                { "Content-Type", contentType }
            };

            return Result.Success(new ProductMediaUploadDto(
                objectKey,
                uploadUrl,
                publicUrl,
                expiresAt,
                "PUT",
                requiredHeaders));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate presigned upload URL for key: {ObjectKey}", objectKey);
            return Result.Failure<ProductMediaUploadDto>(Error.Unavailable(
                "ProductMedia.StorageUnavailable",
                "Failed to generate upload URL from Cloudflare R2 storage."));
        }
    }

    public async Task<Result<ProductMediaMetadata>> GetObjectMetadataAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new GetObjectMetadataRequest
            {
                BucketName = _settings.BucketName,
                Key = objectKey
            };

            var response = await _s3Client.GetObjectMetadataAsync(request, cancellationToken);

            return Result.Success(new ProductMediaMetadata(
                objectKey,
                response.Headers.ContentType,
                response.Headers.ContentLength,
                response.ETag));
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound || string.Equals(ex.ErrorCode, "NotFound", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<ProductMediaMetadata>(Error.NotFound(
                "ProductMedia.NotFound",
                "Media object was not found in storage."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get metadata for object key: {ObjectKey}", objectKey);
            return Result.Failure<ProductMediaMetadata>(Error.Unavailable(
                "ProductMedia.StorageUnavailable",
                "Failed to retrieve media metadata from storage."));
        }
    }

    public async Task<Result> DeleteObjectAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new DeleteObjectRequest
            {
                BucketName = _settings.BucketName,
                Key = objectKey
            };

            await _s3Client.DeleteObjectAsync(request, cancellationToken);
            return Result.Success();
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound || string.Equals(ex.ErrorCode, "NotFound", StringComparison.OrdinalIgnoreCase))
        {
            // Idempotent success when object already not found in storage
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete object from storage: {ObjectKey}", objectKey);
            return Result.Failure(Error.Unavailable(
                "ProductMedia.StorageUnavailable",
                "Failed to delete media object from storage."));
        }
    }
}
