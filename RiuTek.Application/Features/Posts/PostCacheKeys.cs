namespace RiuTek.Application.Features.Posts;

public static class PostCacheKeys
{
    public const string PostListPrefix = "posts:list:";

    public static string GetListKey(int pageIndex, int pageSize, bool? isFeaturedOnly, bool isPublishedOnly, string? searchTerm)
    {
        var normalizedSearch = string.IsNullOrWhiteSpace(searchTerm) ? "all" : searchTerm.Trim().ToLowerInvariant();
        var featured = isFeaturedOnly.HasValue ? isFeaturedOnly.Value.ToString().ToLowerInvariant() : "all";
        var published = isPublishedOnly.ToString().ToLowerInvariant();

        return $"{PostListPrefix}p{pageIndex}_s{pageSize}_feat_{featured}_pub_{published}_q_{normalizedSearch}";
    }
}
