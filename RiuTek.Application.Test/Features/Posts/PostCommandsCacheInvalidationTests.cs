using FluentAssertions;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Posts;
using RiuTek.Application.Features.Posts.Commands;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Test.Features.Posts;

public class PostCommandsCacheInvalidationTests
{
    [Fact]
    public async Task CreatePostCommandHandler_InvalidatesPostListPrefix_AfterSuccessfulSave()
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var author = new User("author@example.com", "hash", "Author Name", UserRole.Admin);
        context.Users.Add(author);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(author.Id);

        var cacheMock = new Mock<ICacheService>();

        var handler = new CreatePostCommandHandler(context, currentUserMock.Object, cacheMock.Object);

        var command = new CreatePostCommand("New Post Title", "Summary", "Full Content", null, true, false);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        cacheMock.Verify(c => c.RemoveByPrefixAsync(PostCacheKeys.PostListPrefix, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdatePostCommandHandler_InvalidatesPostListPrefix_AfterSuccessfulSave()
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var author = new User("author@example.com", "hash", "Author Name", UserRole.Admin);
        context.Users.Add(author);

        var post = new Post
        {
            Title = "Old Title",
            Slug = "old-title",
            Summary = "Old Summary",
            Content = "Old Content",
            AuthorId = author.Id,
            Author = author,
            IsPublished = true
        };
        context.Posts.Add(post);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(author.Id);

        var cacheMock = new Mock<ICacheService>();

        var handler = new UpdatePostCommandHandler(context, currentUserMock.Object, cacheMock.Object);

        var command = new UpdatePostCommand(post.Id, "Updated Title", "Updated Summary", "Updated Content", null, true, false);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        cacheMock.Verify(c => c.RemoveByPrefixAsync(PostCacheKeys.PostListPrefix, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeletePostCommandHandler_InvalidatesPostListPrefix_AfterSuccessfulSave()
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var author = new User("author@example.com", "hash", "Author Name", UserRole.Admin);
        context.Users.Add(author);

        var post = new Post
        {
            Title = "To Delete",
            Slug = "to-delete",
            Summary = "Summary",
            Content = "Content",
            AuthorId = author.Id,
            Author = author,
            IsPublished = true
        };
        context.Posts.Add(post);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(author.Id);

        var cacheMock = new Mock<ICacheService>();

        var handler = new DeletePostCommandHandler(context, currentUserMock.Object, cacheMock.Object);

        // Act
        var result = await handler.Handle(new DeletePostCommand(post.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        cacheMock.Verify(c => c.RemoveByPrefixAsync(PostCacheKeys.PostListPrefix, It.IsAny<CancellationToken>()), Times.Once);
    }
}
