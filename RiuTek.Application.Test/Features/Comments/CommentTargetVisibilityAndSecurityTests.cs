using FluentAssertions;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Comments.Commands;
using RiuTek.Application.Features.Comments.Queries;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Test.Features.Comments;

public class CommentTargetVisibilityAndSecurityTests
{
    [Fact]
    public async Task GetPostComments_WhenPostIsPublished_ReturnsComments()
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var author = new User("author@example.com", "hash", "Author", UserRole.Admin);
        context.Users.Add(author);

        var post = new Post
        {
            Title = "Published Post",
            Slug = "published-post",
            Summary = "Summ",
            Content = "Cont",
            Author = author,
            AuthorId = author.Id,
            IsPublished = true
        };
        context.Posts.Add(post);

        var comment = new PostComment
        {
            PostId = post.Id,
            UserId = author.Id,
            User = author,
            Content = "Great post!"
        };
        context.PostComments.Add(comment);
        await context.SaveChangesAsync();

        var handler = new GetPostCommentsQueryHandler(context);

        // Act
        var result = await handler.Handle(new GetPostCommentsQuery(post.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetPostComments_WhenPostIsDraft_ReturnsNotFound()
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var author = new User("author@example.com", "hash", "Author", UserRole.Admin);
        context.Users.Add(author);

        var post = new Post
        {
            Title = "Draft Post",
            Slug = "draft-post",
            Summary = "Summ",
            Content = "Cont",
            Author = author,
            AuthorId = author.Id,
            IsPublished = false
        };
        context.Posts.Add(post);
        await context.SaveChangesAsync();

        var handler = new GetPostCommentsQueryHandler(context);

        // Act
        var result = await handler.Handle(new GetPostCommentsQuery(post.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task CreatePostComment_WhenPostIsPublished_CreatesCommentSuccessfully()
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = new User("user@example.com", "hash", "Customer User", UserRole.Customer);
        var post = new Post
        {
            Title = "Published Post",
            Slug = "published-post",
            Summary = "Summ",
            Content = "Cont",
            Author = user,
            AuthorId = user.Id,
            IsPublished = true
        };
        context.Users.Add(user);
        context.Posts.Add(post);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(user.Id);

        var handler = new CreatePostCommentCommandHandler(context, currentUserMock.Object);

        // Act
        var result = await handler.Handle(new CreatePostCommentCommand(post.Id, "Great Post Comment"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.PostId.Should().Be(post.Id);
        result.Value.UserId.Should().Be(user.Id);
        result.Value.Content.Should().Be("Great Post Comment");
        result.Value.ParentCommentId.Should().BeNull();
    }

    [Fact]
    public async Task CreatePostComment_WhenReplyingToValidParent_CreatesReplySuccessfully()
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = new User("user@example.com", "hash", "Customer User", UserRole.Customer);
        var post = new Post
        {
            Title = "Published Post",
            Slug = "published-post",
            Summary = "Summ",
            Content = "Cont",
            Author = user,
            AuthorId = user.Id,
            IsPublished = true
        };
        context.Users.Add(user);
        context.Posts.Add(post);

        var parentComment = new PostComment
        {
            PostId = post.Id,
            UserId = user.Id,
            User = user,
            Content = "Parent Post Comment"
        };
        context.PostComments.Add(parentComment);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(user.Id);

        var handler = new CreatePostCommentCommandHandler(context, currentUserMock.Object);

        // Act
        var result = await handler.Handle(new CreatePostCommentCommand(post.Id, "Reply Comment", parentComment.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.PostId.Should().Be(post.Id);
        result.Value.UserId.Should().Be(user.Id);
        result.Value.Content.Should().Be("Reply Comment");
        result.Value.ParentCommentId.Should().Be(parentComment.Id);
    }

    [Fact]
    public async Task CreatePostComment_WhenPostIsDraft_ReturnsNotFound()
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = new User("user@example.com", "hash", "User", UserRole.Customer);
        var post = new Post
        {
            Title = "Draft Post",
            Slug = "draft-post",
            Summary = "Summ",
            Content = "Cont",
            Author = user,
            AuthorId = user.Id,
            IsPublished = false
        };
        context.Users.Add(user);
        context.Posts.Add(post);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(user.Id);

        var handler = new CreatePostCommentCommandHandler(context, currentUserMock.Object);

        // Act
        var result = await handler.Handle(new CreatePostCommentCommand(post.Id, "Hello"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task GetProductComments_WhenProductIsActive_ReturnsComments()
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = new Category("CPU", "cpu", ComponentType.Cpu);
        context.Categories.Add(category);

        var product = new Product(
            category.Id,
            "Intel i9",
            "intel-i9",
            "SKU1",
            "Intel",
            500,
            10,
            "img.png",
            ComponentType.Cpu,
            new CpuSpecification()
        )
        {
            IsActive = true
        };
        context.Products.Add(product);

        var user = new User("user@example.com", "hash", "User");
        context.Users.Add(user);

        var comment = new Comment(product.Id, user.Id, "Awesome CPU") { User = user };
        context.Comments.Add(comment);
        await context.SaveChangesAsync();

        var handler = new GetProductCommentsQueryHandler(context);

        // Act
        var result = await handler.Handle(new GetProductCommentsQuery(product.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetProductComments_WhenProductIsInactive_ReturnsNotFound()
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = new Category("CPU", "cpu", ComponentType.Cpu);
        context.Categories.Add(category);

        var product = new Product(
            category.Id,
            "Intel i9",
            "intel-i9",
            "SKU1",
            "Intel",
            500,
            10,
            "img.png",
            ComponentType.Cpu,
            new CpuSpecification()
        )
        {
            IsActive = false
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var handler = new GetProductCommentsQueryHandler(context);

        // Act
        var result = await handler.Handle(new GetProductCommentsQuery(product.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task CreateProductComment_WhenProductIsActive_CreatesCommentSuccessfully()
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = new Category("CPU", "cpu", ComponentType.Cpu);
        context.Categories.Add(category);

        var product = new Product(
            category.Id,
            "Intel i9",
            "intel-i9",
            "SKU1",
            "Intel",
            500,
            10,
            "img.png",
            ComponentType.Cpu,
            new CpuSpecification()
        )
        {
            IsActive = true
        };
        context.Products.Add(product);

        var user = new User("user@example.com", "hash", "Customer User", UserRole.Customer);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(user.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Customer.ToString());

        var handler = new CreateProductCommentCommandHandler(context, currentUserMock.Object);

        // Act
        var result = await handler.Handle(new CreateProductCommentCommand(product.Id, "Awesome product comment"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ProductId.Should().Be(product.Id);
        result.Value.UserId.Should().Be(user.Id);
        result.Value.Content.Should().Be("Awesome product comment");
        result.Value.ParentCommentId.Should().BeNull();
        result.Value.IsStaffAnswer.Should().BeFalse();
    }

    [Fact]
    public async Task CreateProductComment_WhenReplyingToValidParent_CreatesReplySuccessfully()
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = new Category("CPU", "cpu", ComponentType.Cpu);
        context.Categories.Add(category);

        var product = new Product(
            category.Id,
            "Intel i9",
            "intel-i9",
            "SKU1",
            "Intel",
            500,
            10,
            "img.png",
            ComponentType.Cpu,
            new CpuSpecification()
        )
        {
            IsActive = true
        };
        context.Products.Add(product);

        var user = new User("user@example.com", "hash", "Customer User", UserRole.Customer);
        context.Users.Add(user);

        var parentComment = new Comment(product.Id, user.Id, "Parent comment") { User = user };
        context.Comments.Add(parentComment);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(user.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Customer.ToString());

        var handler = new CreateProductCommentCommandHandler(context, currentUserMock.Object);

        // Act
        var result = await handler.Handle(new CreateProductCommentCommand(product.Id, "Product reply comment", parentComment.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ProductId.Should().Be(product.Id);
        result.Value.UserId.Should().Be(user.Id);
        result.Value.Content.Should().Be("Product reply comment");
        result.Value.ParentCommentId.Should().Be(parentComment.Id);
    }

    [Fact]
    public async Task CreateProductComment_WhenProductIsInactive_ReturnsNotFound()
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = new Category("CPU", "cpu", ComponentType.Cpu);
        context.Categories.Add(category);

        var product = new Product(
            category.Id,
            "Intel i9",
            "intel-i9",
            "SKU1",
            "Intel",
            500,
            10,
            "img.png",
            ComponentType.Cpu,
            new CpuSpecification()
        )
        {
            IsActive = false
        };
        context.Products.Add(product);

        var user = new User("user@example.com", "hash", "User");
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(user.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Customer.ToString());

        var handler = new CreateProductCommentCommandHandler(context, currentUserMock.Object);

        // Act
        var result = await handler.Handle(new CreateProductCommentCommand(product.Id, "Great"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task DeleteComment_WhenUserIsAuthor_DeletesComment()
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = new User("user@example.com", "hash", "User", UserRole.Customer);
        context.Users.Add(user);

        var postComment = new PostComment
        {
            PostId = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            Content = "My Comment"
        };
        context.PostComments.Add(postComment);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(user.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Customer.ToString());

        var handler = new DeleteCommentCommandHandler(context, currentUserMock.Object);

        // Act
        var result = await handler.Handle(new DeleteCommentCommand(postComment.Id, CommentTargetType.Post), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteComment_WhenUserIsNotAuthorAndNotStaffOrAdmin_ReturnsForbidden()
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var author = new User("author@example.com", "hash", "Author", UserRole.Customer);
        var otherUser = new User("other@example.com", "hash", "Other", UserRole.Customer);
        context.Users.AddRange(author, otherUser);

        var postComment = new PostComment
        {
            PostId = Guid.NewGuid(),
            UserId = author.Id,
            User = author,
            Content = "Author Comment"
        };
        context.PostComments.Add(postComment);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(otherUser.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Customer.ToString());

        var handler = new DeleteCommentCommandHandler(context, currentUserMock.Object);

        // Act
        var result = await handler.Handle(new DeleteCommentCommand(postComment.Id, CommentTargetType.Post), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Staff)]
    public async Task DeleteComment_WhenUserIsAdminOrStaff_CanDeleteOtherUserComment(UserRole role)
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var author = new User("author@example.com", "hash", "Author", UserRole.Customer);
        var staff = new User("staff@example.com", "hash", "Staff", role);
        context.Users.AddRange(author, staff);

        var postComment = new PostComment
        {
            PostId = Guid.NewGuid(),
            UserId = author.Id,
            User = author,
            Content = "Author Comment"
        };
        context.PostComments.Add(postComment);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(staff.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(role.ToString());

        var handler = new DeleteCommentCommandHandler(context, currentUserMock.Object);

        // Act
        var result = await handler.Handle(new DeleteCommentCommand(postComment.Id, CommentTargetType.Post), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }
}
