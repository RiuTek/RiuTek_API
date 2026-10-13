using FluentAssertions;
using RiuTek.Infrastructure.Settings;
using Xunit;

namespace RiuTek.Application.Test.Features.Products;

public class R2SettingsUnitTests
{
    private static R2Settings CreateValidSettings() => new()
    {
        Enabled = true,
        ServiceUrl = "https://test-account-id.r2.cloudflarestorage.com",
        AccessKeyId = "valid_access_key_id",
        SecretAccessKey = "valid_secret_access_key_123456",
        BucketName = "riutek-media-bucket",
        PublicBaseUrl = "https://cdn.riutek.com",
        PresignedUrlExpiryMinutes = 10,
        MaxFileSizeBytes = 5242880
    };

    [Fact]
    public void Validate_WhenDisabled_AllowsMissingOrEmptyCredentials()
    {
        var settings = new R2Settings
        {
            Enabled = false,
            ServiceUrl = "",
            AccessKeyId = "",
            SecretAccessKey = "",
            BucketName = "",
            PublicBaseUrl = "",
            PresignedUrlExpiryMinutes = 0,
            MaxFileSizeBytes = 0
        };

        var act = () => settings.Validate();
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("placeholder")]
    [InlineData("PLACEHOLDER_SERVICE_URL")]
    [InlineData("YOUR_ACCOUNT_ID.r2.cloudflarestorage.com")]
    [InlineData("https://CHANGE_ME.r2.cloudflarestorage.com")]
    [InlineData("https://DO_NOT_COMMIT.r2.cloudflarestorage.com")]
    public void Validate_WhenEnabled_AndServiceUrlInvalidOrPlaceholder_Throws(string? serviceUrl)
    {
        var settings = CreateValidSettings();
        settings.ServiceUrl = serviceUrl!;

        var act = () => settings.Validate();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*R2:ServiceUrl*");
    }

    [Theory]
    [InlineData("http://test.r2.cloudflarestorage.com")]
    [InlineData("ftp://test.r2.cloudflarestorage.com")]
    [InlineData("not-a-valid-url")]
    public void Validate_WhenEnabled_AndServiceUrlNotHttps_Throws(string url)
    {
        var settings = CreateValidSettings();
        settings.ServiceUrl = url;

        var act = () => settings.Validate();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*R2:ServiceUrl*HTTPS*");
    }

    [Theory]
    [InlineData("https://test.r2.cloudflarestorage.com/some-bucket")]
    [InlineData("https://test.r2.cloudflarestorage.com/path/segment")]
    public void Validate_WhenEnabled_AndServiceUrlHasPathSegments_Throws(string url)
    {
        var settings = CreateValidSettings();
        settings.ServiceUrl = url;

        var act = () => settings.Validate();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*R2:ServiceUrl*root endpoint*");
    }

    [Theory]
    [InlineData("https://user:pass@test.r2.cloudflarestorage.com")]
    [InlineData("https://test.r2.cloudflarestorage.com?param=value")]
    [InlineData("https://test.r2.cloudflarestorage.com#anchor")]
    public void Validate_WhenEnabled_AndServiceUrlHasUserInfoOrQueryOrFragment_Throws(string url)
    {
        var settings = CreateValidSettings();
        settings.ServiceUrl = url;

        var act = () => settings.Validate();
        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("placeholder_key")]
    [InlineData("YOUR_ACCESS_KEY_ID")]
    [InlineData("CHANGE_ME_KEY")]
    [InlineData("DO_NOT_COMMIT_KEY")]
    public void Validate_WhenEnabled_AndAccessKeyIdInvalidOrPlaceholder_Throws(string? accessKey)
    {
        var settings = CreateValidSettings();
        settings.AccessKeyId = accessKey!;

        var act = () => settings.Validate();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*R2:AccessKeyId*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("placeholder_secret_abc123")]
    [InlineData("YOUR_SECRET_ACCESS_KEY_123")]
    [InlineData("CHANGE_ME_SECRET")]
    [InlineData("DO_NOT_COMMIT_SECRET")]
    public void Validate_WhenEnabled_AndSecretAccessKeyInvalidOrPlaceholder_ThrowsAndNeverLeaksSecret(string? secret)
    {
        var settings = CreateValidSettings();
        settings.SecretAccessKey = secret!;

        var act = () => settings.Validate();
        var ex = act.Should().Throw<InvalidOperationException>().Which;
        ex.Message.Should().Contain("R2:SecretAccessKey");
        if (!string.IsNullOrWhiteSpace(secret))
        {
            ex.Message.Should().NotContain(secret);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("placeholder_bucket")]
    [InlineData("YOUR_BUCKET_NAME")]
    [InlineData("CHANGE_ME_BUCKET")]
    [InlineData("DO_NOT_COMMIT_BUCKET")]
    public void Validate_WhenEnabled_AndBucketNameInvalidOrPlaceholder_Throws(string? bucket)
    {
        var settings = CreateValidSettings();
        settings.BucketName = bucket!;

        var act = () => settings.Validate();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*R2:BucketName*");
    }

    [Theory]
    [InlineData("bucket with spaces")]
    [InlineData(" bucket")]
    [InlineData("bucket\tname")]
    public void Validate_WhenEnabled_AndBucketNameContainsWhitespace_Throws(string bucket)
    {
        var settings = CreateValidSettings();
        settings.BucketName = bucket;

        var act = () => settings.Validate();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*R2:BucketName*whitespace*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://cdn.riutek.com")]
    [InlineData("not-a-url")]
    [InlineData("https://YOUR_CDN_DOMAIN.com")]
    [InlineData("https://CHANGE_ME.com")]
    [InlineData("https://user:pass@cdn.riutek.com")]
    [InlineData("https://cdn.riutek.com?key=1")]
    [InlineData("https://cdn.riutek.com#hash")]
    public void Validate_WhenEnabled_AndPublicBaseUrlInvalid_Throws(string? url)
    {
        var settings = CreateValidSettings();
        settings.PublicBaseUrl = url!;

        var act = () => settings.Validate();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*R2:PublicBaseUrl*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(61)]
    [InlineData(100)]
    public void Validate_WhenEnabled_AndExpiryMinutesOutOfRange_Throws(int expiry)
    {
        var settings = CreateValidSettings();
        settings.PresignedUrlExpiryMinutes = expiry;

        var act = () => settings.Validate();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*R2:PresignedUrlExpiryMinutes*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(20971521)] // > 20 MiB
    public void Validate_WhenEnabled_AndMaxFileSizeBytesInvalid_Throws(long maxSize)
    {
        var settings = CreateValidSettings();
        settings.MaxFileSizeBytes = maxSize;

        var act = () => settings.Validate();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*R2:MaxFileSizeBytes*");
    }

    [Fact]
    public void Validate_WhenEnabled_WithValidConfiguration_Succeeds()
    {
        var settings = CreateValidSettings();
        var act = () => settings.Validate();
        act.Should().NotThrow();
    }
}
