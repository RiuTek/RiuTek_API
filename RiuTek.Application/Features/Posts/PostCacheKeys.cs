using System.Security.Cryptography;
using System.Text;

namespace RiuTek.Application.Features.Posts;

public static class PostCacheKeys
{
    public const string PostListPrefix = "posts:list:";

    public static string GetListKey(int pageIndex, int pageSize, bool? isFeaturedOnly, bool isPublishedOnly, string? searchTerm)
    {
        string searchSegment;
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            searchSegment = "q_none";
        }
        else
        {
            var normalizedSearch = searchTerm.Trim().ToLowerInvariant();
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedSearch));
            var hexDigest = Convert.ToHexString(hashBytes).ToLowerInvariant();
            searchSegment = $"q_sha256_{hexDigest}";
        }

        var featured = isFeaturedOnly.HasValue ? isFeaturedOnly.Value.ToString().ToLowerInvariant() : "all";
        var published = isPublishedOnly.ToString().ToLowerInvariant();

        return $"{PostListPrefix}p{pageIndex}_s{pageSize}_feat_{featured}_pub_{published}_{searchSegment}";
    }
}
