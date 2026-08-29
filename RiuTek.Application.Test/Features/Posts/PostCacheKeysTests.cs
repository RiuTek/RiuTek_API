using FluentAssertions;
using RiuTek.Application.Features.Posts;

namespace RiuTek.Application.Test.Features.Posts;

public class PostCacheKeysTests
{
    [Fact]
    public void GetListKey_GeneratesDeterministicKey()
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
        var keyFeatured = PostCacheKeys.GetListKey(1, 10, true, true, null);
        var keySearch = PostCacheKeys.GetListKey(1, 10, null, true, "intel");

        keyPage1.Should().NotBe(keyPage2);
        keyPage1.Should().NotBe(keyFeatured);
        keyPage1.Should().NotBe(keySearch);
    }

    [Fact]
    public void GetListKey_HandlesNullSearchAndFeaturedGracefully()
    {
        var key = PostCacheKeys.GetListKey(1, 10, null, true, null);

        key.Should().Be("posts:list:p1_s10_feat_all_pub_true_q_all");
    }
}
