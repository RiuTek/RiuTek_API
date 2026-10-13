using System.Web;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RiuTek.Infrastructure.Services;
using RiuTek.Infrastructure.Settings;
using Xunit;

namespace RiuTek.Application.Test.Features.Products;

public class R2ProductMediaStorageTests
{
    private static (R2ProductMediaStorage Storage, R2Settings Settings) CreateTestStorage(
        int expiryMinutes = 10,
        long maxFileSizeBytes = 5242880)
    {
        var settings = new R2Settings
        {
            Enabled = true,
            ServiceUrl = "https://1234567890abcdef.r2.cloudflarestorage.com",
            AccessKeyId = "test_access_key_id",
            SecretAccessKey = "test_secret_access_key_987654321",
            BucketName = "riutek-media",
            PublicBaseUrl = "https://cdn.riutek.com",
            PresignedUrlExpiryMinutes = expiryMinutes,
            MaxFileSizeBytes = maxFileSizeBytes
        };

        var storage = new R2ProductMediaStorage(settings, NullLogger<R2ProductMediaStorage>.Instance);
        return (storage, settings);
    }

    [Theory]
    [InlineData("products/2026/10/01925b30-c9a7-7e61-a8d9-6012e1a3bc4d.jpg", "image/jpeg", 10)]
    [InlineData("products/2026/10/01925b30-c9a7-7e61-a8d9-6012e1a3bc4d.webp", "image/webp", 25)]
    public async Task CreatePresignedUploadUrlAsync_GeneratesValidSigV4Url_WithoutNetworkCalls(
        string objectKey,
        string contentType,
        int expiryMinutes)
    {
        var (storage, settings) = CreateTestStorage(expiryMinutes: expiryMinutes);

        var beforeTime = DateTimeOffset.UtcNow;
        var result = await storage.CreatePresignedUploadUrlAsync(objectKey, contentType);
        var afterTime = DateTimeOffset.UtcNow;

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;

        dto.ObjectKey.Should().Be(objectKey);
        dto.HttpMethod.Should().Be("PUT");
        dto.RequiredHeaders.Should().ContainKey("Content-Type");
        dto.RequiredHeaders["Content-Type"].Should().Be(contentType);

        // Public URL validation
        dto.PublicUrl.Should().Be($"https://cdn.riutek.com/{objectKey}");

        // Parse and validate presigned URL
        Uri.TryCreate(dto.UploadUrl, UriKind.Absolute, out var uri).Should().BeTrue();
        uri!.Scheme.Should().Be("https");
        uri.Host.Should().Be("1234567890abcdef.r2.cloudflarestorage.com");

        // Path-style: /bucket/objectKey
        var decodedPath = Uri.UnescapeDataString(uri.AbsolutePath);
        decodedPath.Should().Be($"/riutek-media/{objectKey}");

        // Query parameters: Signature V4 assertions
        var query = HttpUtility.ParseQueryString(uri.Query);

        query["X-Amz-Algorithm"].Should().Be("AWS4-HMAC-SHA256", "Cloudflare R2 requires SigV4");
        query["X-Amz-Credential"].Should().NotBeNullOrEmpty();
        query["X-Amz-Credential"].Should().StartWith(settings.AccessKeyId);
        query["X-Amz-Credential"].Should().Contain("/auto/s3/aws4_request");
        query["X-Amz-SignedHeaders"].Should().NotBeNullOrEmpty();
        query["X-Amz-SignedHeaders"].Should().Contain("content-type");
        query["X-Amz-Signature"].Should().NotBeNullOrEmpty();

        // Expiry verification
        query["X-Amz-Expires"].Should().NotBeNullOrEmpty();
        int.TryParse(query["X-Amz-Expires"], out var expiresSeconds).Should().BeTrue();
        expiresSeconds.Should().Be(expiryMinutes * 60, "X-Amz-Expires should match configured PresignedUrlExpiryMinutes");

        dto.ExpiresAt.Should().BeOnOrAfter(beforeTime.AddMinutes(expiryMinutes));
        dto.ExpiresAt.Should().BeOnOrBefore(afterTime.AddMinutes(expiryMinutes).AddSeconds(2));

        // Secret access key must NEVER be leaked in URL or DTO
        dto.UploadUrl.Should().NotContain(settings.SecretAccessKey);
        dto.PublicUrl.Should().NotContain(settings.SecretAccessKey);
    }

    [Fact]
    public void StorageProperties_ReflectSettingsCorrectly()
    {
        var (storage, settings) = CreateTestStorage(expiryMinutes: 15, maxFileSizeBytes: 10485760L);

        storage.IsEnabled.Should().BeTrue();
        storage.MaxFileSizeBytes.Should().Be(10485760L);
        storage.GetPublicUrl("products/test.png").Should().Be("https://cdn.riutek.com/products/test.png");
    }
}
