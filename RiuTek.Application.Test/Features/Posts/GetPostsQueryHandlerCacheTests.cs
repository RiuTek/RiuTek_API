using FluentAssertions;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Models;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Posts;
using RiuTek.Application.Features.Posts.Queries;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Test.Features.Posts;

public class GetPostsQueryHandlerCacheTests
{
    [Fact]
    public async Task Handle_WhenCacheHit_ReturnsCachedDataWithoutQueryingDb()
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var cacheMock = new Mock<ICacheService>();

        var expectedKey = PostCacheKeys.GetListKey(1, 10, null, true, null);
        var cachedItems = new List<PostSummaryDto>
        {
            new(Guid.NewGuid(), "Cached Post", "cached-post", "Summary", null, Guid.NewGuid(), "Author", 10, false, DateTime.UtcNow, DateTime.UtcNow)
        };
        var cachedResult = PagedResult<PostSummaryDto>.Create(cachedItems, 1, 1, 10);

        cacheMock.Setup(c => c.GetAsync<PagedResult<PostSummaryDto>>(expectedKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedResult);

        var handler = new GetPostsQueryHandler(context, cacheMock.Object);

        // Act
        var result = await handler.Handle(new GetPostsQuery(1, 10), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(1);
        result.Value.Items[0].Title.Should().Be("Cached Post");

        // Verify SetAsync was never called because it was a cache hit
        cacheMock.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<PagedResult<PostSummaryDto>>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCacheMiss_QueriesDbAndSetsCache()
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var author = new User("author@example.com", "hash", "Post Author");
        context.Users.Add(author);

        var post = new Post
        {
            Title = "DB Post",
            Slug = "db-post",
            Summary = "From DB",
            Content = "Content",
            Author = author,
            AuthorId = author.Id,
            IsPublished = true,
            PublishedAt = DateTime.UtcNow
        };
        context.Posts.Add(post);
        await context.SaveChangesAsync();

        var cacheMock = new Mock<ICacheService>();
        cacheMock.Setup(c => c.GetAsync<PagedResult<PostSummaryDto>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PagedResult<PostSummaryDto>?)null);

        var handler = new GetPostsQueryHandler(context, cacheMock.Object);

        // Act
        var result = await handler.Handle(new GetPostsQuery(1, 10), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(1);
        result.Value.Items[0].Title.Should().Be("DB Post");

        var expectedKey = PostCacheKeys.GetListKey(1, 10, null, true, null);
        cacheMock.Verify(c => c.SetAsync(
            expectedKey,
            It.Is<PagedResult<PostSummaryDto>>(p => p.TotalCount == 1),
            It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
