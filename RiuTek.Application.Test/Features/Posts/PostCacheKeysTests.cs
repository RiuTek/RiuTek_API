using FluentAssertions;
using RiuTek.Application.Features.Posts;

namespace RiuTek.Application.Test.Features.Posts;

public class PostCacheKeysTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetListKey_WhenSearchTermIsNullOrEmptyOrWhitespace_UsesQNoneSegment(string? searchTerm)
    {
        var key = PostCacheKeys.GetListKey(1, 10, null, true, searchTerm);

        key.Should().Be("posts:list:p1_s10_feat_all_pub_true_q_none");
    }

    [Fact]
    public void GetListKey_WhenSearchTermIsLiteralAll_DoesNotCollideWithNullSearchTerm()
    {
        var nullSearchKey = PostCacheKeys.GetListKey(1, 10, null, true, null);
        var literalAllKey = PostCacheKeys.GetListKey(1, 10, null, true, "all");

        nullSearchKey.Should().Be("posts:list:p1_s10_feat_all_pub_true_q_none");
        literalAllKey.Should().NotBe(nullSearchKey);
        literalAllKey.Should().Contain("q_sha256_");
    }

    [Fact]
    public void GetListKey_GeneratesDeterministicKey_CaseAndTrimInsensitive()
    {
        var key1 = PostCacheKeys.GetListKey(1, 10, true, true, "laptop");
        var key2 = PostCacheKeys.GetListKey(1, 10, true, true, "  LAPTOP  ");

        key1.Should().Be(key2);
        key1.Should().StartWith(PostCacheKeys.PostListPrefix);
    }

    [Fact]
    public void GetListKey_DifferentFilters_GenerateDistinctKeys()
    {
        var keyPage1 = PostCacheKeys.GetListKey(1, 10, null, true, null);
        var keyPage2 = PostCacheKeys.GetListKey(2, 10, null, true, null);
        var keyFeaturedTrue = PostCacheKeys.GetListKey(1, 10, true, true, null);
        var keyFeaturedFalse = PostCacheKeys.GetListKey(1, 10, false, true, null);
        var keyPublishedFalse = PostCacheKeys.GetListKey(1, 10, null, false, null);
        var keySearchIntel = PostCacheKeys.GetListKey(1, 10, null, true, "intel");
        var keySearchAmd = PostCacheKeys.GetListKey(1, 10, null, true, "amd");

        keyPage1.Should().NotBe(keyPage2);
        keyPage1.Should().NotBe(keyFeaturedTrue);
        keyFeaturedTrue.Should().NotBe(keyFeaturedFalse);
        keyPage1.Should().NotBe(keyPublishedFalse);
        keySearchIntel.Should().NotBe(keySearchAmd);
    }

    [Fact]
    public void GetListKey_VeryLongSearchTerm_ProducesBoundedLengthKey()
    {
        var longSearchTerm = new string('a', 5000);
        var key = PostCacheKeys.GetListKey(1, 10, null, true, longSearchTerm);

        // SHA-256 hex is 64 chars, total key length should be under 150 chars
        key.Length.Should().BeLessThan(150);
        key.Should().Contain("q_sha256_");
    }
}
