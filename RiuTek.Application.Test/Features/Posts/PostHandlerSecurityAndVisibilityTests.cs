using FluentAssertions;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Posts;
using RiuTek.Application.Features.Posts.Commands;
using RiuTek.Application.Features.Posts.Queries;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Test.Features.Posts;

public class PostHandlerSecurityAndVisibilityTests
{
    [Fact]
    public async Task CreatePost_WhenUserIsCustomer_ReturnsForbidden()
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var customer = new User("cust@example.com", "hash", "Cust", UserRole.Customer);
        context.Users.Add(customer);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(customer.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Customer.ToString());

        var cacheMock = new Mock<ICacheService>();
        var handler = new CreatePostCommandHandler(context, currentUserMock.Object, cacheMock.Object);

        // Act
        var result = await handler.Handle(new CreatePostCommand("Title", "Summ", "Cont", null), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(Core.Common.ErrorType.Forbidden);
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Staff)]
    public async Task CreatePost_WhenUserIsAdminOrStaff_SucceedsAndInvalidatesCache(UserRole role)
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = new User("admin_staff@example.com", "hash", "Manager", role);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(user.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(role.ToString());

        var cacheMock = new Mock<ICacheService>();
        var handler = new CreatePostCommandHandler(context, currentUserMock.Object, cacheMock.Object);

        // Act
        var result = await handler.Handle(new CreatePostCommand("Title Manager", "Summ", "Cont", null, true), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Title.Should().Be("Title Manager");
        cacheMock.Verify(c => c.RemoveByPrefixAsync(PostCacheKeys.PostListPrefix, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdatePost_WhenUserIsCustomer_ReturnsForbidden()
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var customer = new User("cust@example.com", "hash", "Cust", UserRole.Customer);
        context.Users.Add(customer);

        var post = new Post
        {
            Title = "Old Title",
            Slug = "old-title",
            Summary = "Summ",
            Content = "Cont",
            AuthorId = customer.Id,
            Author = customer,
            IsPublished = true
        };
        context.Posts.Add(post);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(customer.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Customer.ToString());

        var cacheMock = new Mock<ICacheService>();
        var handler = new UpdatePostCommandHandler(context, currentUserMock.Object, cacheMock.Object);

        // Act
        var result = await handler.Handle(new UpdatePostCommand(post.Id, "New Title", "New Summ", "New Cont", null, true, false), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(Core.Common.ErrorType.Forbidden);
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Staff)]
    public async Task UpdatePost_WhenUserIsAdminOrStaff_SucceedsAndInvalidatesCache(UserRole role)
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var author = new User("author@example.com", "hash", "Author", UserRole.Admin);
        var manager = new User("manager@example.com", "hash", "Manager", role);
        context.Users.AddRange(author, manager);

        var post = new Post
        {
            Title = "Old Title",
            Slug = "old-title",
            Summary = "Old Summ",
            Content = "Old Cont",
            AuthorId = author.Id,
            Author = author,
            IsPublished = true
        };
        context.Posts.Add(post);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(manager.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(role.ToString());

        var cacheMock = new Mock<ICacheService>();
        var handler = new UpdatePostCommandHandler(context, currentUserMock.Object, cacheMock.Object);

        // Act
        var result = await handler.Handle(new UpdatePostCommand(post.Id, "New Title", "New Summ", "New Cont", null, true, false), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Title.Should().Be("New Title");
        cacheMock.Verify(c => c.RemoveByPrefixAsync(PostCacheKeys.PostListPrefix, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeletePost_WhenUserIsCustomer_ReturnsForbidden()
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var customer = new User("cust@example.com", "hash", "Cust", UserRole.Customer);
        context.Users.Add(customer);

        var post = new Post
        {
            Title = "To Delete",
            Slug = "to-delete",
            Summary = "Summ",
            Content = "Cont",
            AuthorId = customer.Id,
            Author = customer,
            IsPublished = true
        };
        context.Posts.Add(post);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(customer.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Customer.ToString());

        var cacheMock = new Mock<ICacheService>();
        var handler = new DeletePostCommandHandler(context, currentUserMock.Object, cacheMock.Object);

        // Act
        var result = await handler.Handle(new DeletePostCommand(post.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(Core.Common.ErrorType.Forbidden);
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Staff)]
    public async Task DeletePost_WhenUserIsAdminOrStaff_SucceedsAndInvalidatesCache(UserRole role)
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var author = new User("author@example.com", "hash", "Author", UserRole.Admin);
        var manager = new User("manager@example.com", "hash", "Manager", role);
        context.Users.AddRange(author, manager);

        var post = new Post
        {
            Title = "To Delete",
            Slug = "to-delete",
            Summary = "Summ",
            Content = "Cont",
            AuthorId = author.Id,
            Author = author,
            IsPublished = true
        };
        context.Posts.Add(post);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(manager.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(role.ToString());

        var cacheMock = new Mock<ICacheService>();
        var handler = new DeletePostCommandHandler(context, currentUserMock.Object, cacheMock.Object);

        // Act
        var result = await handler.Handle(new DeletePostCommand(post.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        cacheMock.Verify(c => c.RemoveByPrefixAsync(PostCacheKeys.PostListPrefix, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPostBySlug_WhenPostIsPublished_ReturnsSuccessAndIncrementsViewCount()
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var author = new User("author@example.com", "hash", "Author", UserRole.Admin);
        context.Users.Add(author);

        var post = new Post
        {
            Title = "Published Article",
            Slug = "published-article",
            Summary = "Summ",
            Content = "Cont",
            AuthorId = author.Id,
            Author = author,
            IsPublished = true,
            ViewCount = 5
        };
        context.Posts.Add(post);
        await context.SaveChangesAsync();

        var handler = new GetPostBySlugQueryHandler(context);

        // Act
        var result = await handler.Handle(new GetPostBySlugQuery("published-article"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ViewCount.Should().Be(6);

        var updatedPost = await context.Posts.FindAsync(post.Id);
        updatedPost!.ViewCount.Should().Be(6);
    }

    [Fact]
    public async Task GetPostBySlug_WhenPostIsDraft_ReturnsNotFoundAndDoesNotIncrementViewCount()
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var author = new User("author@example.com", "hash", "Author", UserRole.Admin);
        context.Users.Add(author);

        var post = new Post
        {
            Title = "Draft Article",
            Slug = "draft-article",
            Summary = "Summ",
            Content = "Cont",
            AuthorId = author.Id,
            Author = author,
            IsPublished = false,
            ViewCount = 0
        };
        context.Posts.Add(post);
        await context.SaveChangesAsync();

        var handler = new GetPostBySlugQueryHandler(context);

        // Act
        var result = await handler.Handle(new GetPostBySlugQuery("draft-article"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(Core.Common.ErrorType.NotFound);

        var updatedPost = await context.Posts.FindAsync(post.Id);
        updatedPost!.ViewCount.Should().Be(0);
    }
}
