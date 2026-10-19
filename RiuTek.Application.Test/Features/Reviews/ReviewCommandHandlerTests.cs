using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Reviews.Commands;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using Xunit;

namespace RiuTek.Application.Test.Features.Reviews;

public class ReviewCommandHandlerTests
{
    private static CpuSpecification CreateCpuSpec() => new()
    {
        Socket = CpuSocket.LGA1700,
        CoreCount = 6,
        ThreadCount = 12,
        BaseClockGhz = 2.5,
        BoostClockGhz = 4.4,
        TdpWattage = 65,
        HasIntegratedGpu = false,
        SupportedMemoryType = RamType.DDR4,
        MaxMemorySpeedMhz = 3200
    };

    private static (User User, Category Category, Product Product, Order Order) SeedStandardScenario(
        TestApplicationDbContext context,
        OrderStatus orderStatus = OrderStatus.Completed,
        UserRole userRole = UserRole.Customer,
        bool productIsActive = true,
        bool userIsActive = true)
    {
        var user = new User($"user_{Guid.NewGuid():N}@example.com", "hash", "Test User", userRole)
        {
            IsActive = userIsActive
        };
        context.Users.Add(user);

        var category = new Category("Electronics", "electronics", ComponentType.Cpu, "desc");
        context.Categories.Add(category);

        var product = new Product(
            category.Id,
            "Test Product",
            $"slug-{Guid.NewGuid():N}",
            $"SKU_{Guid.NewGuid():N}",
            "Brand",
            100_000m,
            10,
            "img.jpg",
            ComponentType.Cpu,
            CreateCpuSpec())
        {
            IsActive = productIsActive
        };
        context.Products.Add(product);

        var order = new Order(
            $"ORD-{Guid.NewGuid():N}"[..20],
            user.Id,
            $"key-{Guid.NewGuid():N}",
            "Test Customer",
            "test@example.com",
            "0901234567",
            "123 Street",
            PaymentMethod.COD);

        order.AddItem(product.Id, product.Name, product.Sku, product.Price, 1);

        TransitionOrderStatus(order, orderStatus);
        context.Orders.Add(order);

        return (user, category, product, order);
    }

    private static void TransitionOrderStatus(Order order, OrderStatus targetStatus)
    {
        if (order.Status == targetStatus) return;

        if (targetStatus == OrderStatus.Cancelled)
        {
            order.ChangeFulfillmentStatus(OrderStatus.Confirmed, OrderStatus.Cancelled);
            return;
        }

        order.ChangeFulfillmentStatus(OrderStatus.Confirmed, OrderStatus.Processing);
        if (targetStatus == OrderStatus.Processing) return;

        order.ChangeFulfillmentStatus(OrderStatus.Processing, OrderStatus.Shipping);
        if (targetStatus == OrderStatus.Shipping) return;

        order.ChangeFulfillmentStatus(OrderStatus.Shipping, OrderStatus.Completed);
    }

    private static Mock<ICurrentUserService> MockUser(User? user, bool authenticated = true)
    {
        var mock = new Mock<ICurrentUserService>();
        mock.Setup(u => u.IsAuthenticated).Returns(authenticated && user != null);
        mock.Setup(u => u.UserId).Returns(user?.Id);
        mock.Setup(u => u.UserRole).Returns(user?.Role.ToString());
        mock.Setup(u => u.UserEmail).Returns(user?.Email);
        return mock;
    }

    // --- CreateReviewCommand Tests ---

    [Fact]
    public async Task CreateReview_WhenUnauthenticated_ReturnsUnauthorized()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (_, _, product, order) = SeedStandardScenario(context);
        await context.SaveChangesAsync();

        var currentUserService = MockUser(null, authenticated: false);
        var handler = new CreateReviewCommandHandler(context, currentUserService.Object);

        var result = await handler.Handle(
            new CreateReviewCommand(order.Id, product.Id, 5, "Title", "Content"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task CreateReview_WhenStaffOrAdminRole_ReturnsForbidden()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (staff, _, product, order) = SeedStandardScenario(context, userRole: UserRole.Staff);
        await context.SaveChangesAsync();

        var currentUserService = MockUser(staff);
        var handler = new CreateReviewCommandHandler(context, currentUserService.Object);

        var result = await handler.Handle(
            new CreateReviewCommand(order.Id, product.Id, 5, "Title", "Content"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Review.Forbidden");
    }

    [Fact]
    public async Task CreateReview_WhenUserIsInactive_ReturnsUserInactive()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, product, order) = SeedStandardScenario(context, userIsActive: false);
        await context.SaveChangesAsync();

        var currentUserService = MockUser(user);
        var handler = new CreateReviewCommandHandler(context, currentUserService.Object);

        var result = await handler.Handle(
            new CreateReviewCommand(order.Id, product.Id, 5, "Title", "Content"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("User.AccountInactive");
    }

    [Fact]
    public async Task CreateReview_WhenProductNotFound_ReturnsProductNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, _, order) = SeedStandardScenario(context);
        await context.SaveChangesAsync();

        var currentUserService = MockUser(user);
        var handler = new CreateReviewCommandHandler(context, currentUserService.Object);

        var result = await handler.Handle(
            new CreateReviewCommand(order.Id, Guid.NewGuid(), 5, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Review.ProductNotFound");
    }

    [Fact]
    public async Task CreateReview_WhenOrderNotFoundOrBelongsToOtherUser_ReturnsOrderNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, product, _) = SeedStandardScenario(context);
        var otherUser = new User("other@example.com", "hash", "Other", UserRole.Customer);
        context.Users.Add(otherUser);

        var otherOrder = new Order(
            $"ORD-{Guid.NewGuid():N}"[..20],
            otherUser.Id,
            $"key-{Guid.NewGuid():N}",
            "Other",
            "other@example.com",
            "0999999999",
            "Address",
            PaymentMethod.COD);
        otherOrder.AddItem(product.Id, product.Name, product.Sku, product.Price, 1);
        TransitionOrderStatus(otherOrder, OrderStatus.Completed);
        context.Orders.Add(otherOrder);
        await context.SaveChangesAsync();

        var currentUserService = MockUser(user);
        var handler = new CreateReviewCommandHandler(context, currentUserService.Object);

        var result = await handler.Handle(
            new CreateReviewCommand(otherOrder.Id, product.Id, 5, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Review.OrderNotFound");
    }

    [Fact]
    public async Task CreateReview_WhenProductNotInOrder_ReturnsProductNotPurchased()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, category, _, order) = SeedStandardScenario(context);
        var otherProduct = new Product(
            category.Id,
            "Unbought Product",
            $"slug-{Guid.NewGuid():N}",
            "SKU-UNBOUGHT",
            "Brand",
            50_000m,
            5,
            "img.jpg",
            ComponentType.Cpu,
            CreateCpuSpec());
        context.Products.Add(otherProduct);
        await context.SaveChangesAsync();

        var currentUserService = MockUser(user);
        var handler = new CreateReviewCommandHandler(context, currentUserService.Object);

        var result = await handler.Handle(
            new CreateReviewCommand(order.Id, otherProduct.Id, 5, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Review.ProductNotPurchased");
    }

    [Theory]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Processing)]
    [InlineData(OrderStatus.Shipping)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task CreateReview_WhenOrderStatusNotCompleted_ReturnsOrderNotCompleted(OrderStatus status)
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, product, order) = SeedStandardScenario(context, orderStatus: status);
        await context.SaveChangesAsync();

        var currentUserService = MockUser(user);
        var handler = new CreateReviewCommandHandler(context, currentUserService.Object);

        var result = await handler.Handle(
            new CreateReviewCommand(order.Id, product.Id, 5, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Review.OrderNotCompleted");
    }

    [Fact]
    public async Task CreateReview_WhenProductIsInactiveButPurchasedInCompletedOrder_Succeeds()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, product, order) = SeedStandardScenario(context, productIsActive: false);
        await context.SaveChangesAsync();

        var currentUserService = MockUser(user);
        var handler = new CreateReviewCommandHandler(context, currentUserService.Object);

        var result = await handler.Handle(
            new CreateReviewCommand(order.Id, product.Id, 5, "Great", "Bought before discontinued"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.Rating.Should().Be(5);
        result.Value.Title.Should().Be("Great");
        result.Value.Content.Should().Be("Bought before discontinued");

        var reviewInDb = await context.Reviews.SingleOrDefaultAsync();
        reviewInDb.Should().NotBeNull();
        reviewInDb!.ProductId.Should().Be(product.Id);
        reviewInDb.UserId.Should().Be(user.Id);
        reviewInDb.OrderId.Should().Be(order.Id);
    }

    [Fact]
    public async Task CreateReview_WhenDuplicateReviewExists_ReturnsAlreadyExists()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, product, order) = SeedStandardScenario(context);
        var existingReview = new Review(product.Id, user.Id, order.Id, 4, "Initial", "Initial content");
        context.Reviews.Add(existingReview);
        await context.SaveChangesAsync();

        var currentUserService = MockUser(user);
        var handler = new CreateReviewCommandHandler(context, currentUserService.Object);

        var result = await handler.Handle(
            new CreateReviewCommand(order.Id, product.Id, 5, "Duplicate", "Content"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Review.AlreadyExists");
    }

    // --- UpdateMyReviewCommand Tests ---

    [Fact]
    public async Task UpdateMyReview_WhenSuccessful_UpdatesFieldsAndPreservesImmutableKeys()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, product, order) = SeedStandardScenario(context);
        var review = new Review(product.Id, user.Id, order.Id, 3, "Old Title", "Old Content");
        review.SetStaffReply("Old staff reply");
        context.Reviews.Add(review);
        await context.SaveChangesAsync();

        var currentUserService = MockUser(user);
        var handler = new UpdateMyReviewCommandHandler(context, currentUserService.Object);

        var result = await handler.Handle(
            new UpdateMyReviewCommand(review.Id, 5, "New Title", "New Content"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Rating.Should().Be(5);
        result.Value.Title.Should().Be("New Title");
        result.Value.Content.Should().Be("New Content");
        result.Value.StaffReply.Should().Be("Old staff reply");

        var updated = await context.Reviews.FindAsync(review.Id);
        updated!.ProductId.Should().Be(product.Id);
        updated.UserId.Should().Be(user.Id);
        updated.OrderId.Should().Be(order.Id);
        updated.Rating.Should().Be(5);
        updated.StaffReply.Should().Be("Old staff reply");
    }

    [Fact]
    public async Task UpdateMyReview_WhenReviewBelongsToAnotherUser_ReturnsNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, product, order) = SeedStandardScenario(context);
        var otherUser = new User("other@example.com", "hash", "Other User", UserRole.Customer);
        context.Users.Add(otherUser);
        var otherReview = new Review(product.Id, otherUser.Id, order.Id, 3, "Other Title", "Other Content");
        context.Reviews.Add(otherReview);
        await context.SaveChangesAsync();

        var currentUserService = MockUser(user);
        var handler = new UpdateMyReviewCommandHandler(context, currentUserService.Object);

        var result = await handler.Handle(
            new UpdateMyReviewCommand(otherReview.Id, 5, "Hack", "Hack"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Review.NotFound");
    }

    // --- DeleteMyReviewCommand Tests ---

    [Fact]
    public async Task DeleteMyReview_WhenSuccessful_HardDeletesReview()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, product, order) = SeedStandardScenario(context);
        var review = new Review(product.Id, user.Id, order.Id, 4, "Title", "Content");
        context.Reviews.Add(review);
        await context.SaveChangesAsync();

        var currentUserService = MockUser(user);
        var handler = new DeleteMyReviewCommandHandler(context, currentUserService.Object);

        var result = await handler.Handle(new DeleteMyReviewCommand(review.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        context.Reviews.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteMyReview_WhenReviewBelongsToAnotherUser_ReturnsNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, product, order) = SeedStandardScenario(context);
        var otherUser = new User("other@example.com", "hash", "Other User", UserRole.Customer);
        context.Users.Add(otherUser);
        var otherReview = new Review(product.Id, otherUser.Id, order.Id, 4, "Title", "Content");
        context.Reviews.Add(otherReview);
        await context.SaveChangesAsync();

        var currentUserService = MockUser(user);
        var handler = new DeleteMyReviewCommandHandler(context, currentUserService.Object);

        var result = await handler.Handle(new DeleteMyReviewCommand(otherReview.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Review.NotFound");
        context.Reviews.Should().HaveCount(1);
    }

    // --- SetReviewReplyCommand & DeleteReviewReplyCommand Tests ---

    [Fact]
    public async Task SetReviewReply_WhenCustomerAttempts_ReturnsForbidden()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (customer, _, product, order) = SeedStandardScenario(context, userRole: UserRole.Customer);
        var review = new Review(product.Id, customer.Id, order.Id, 5, "Title", "Content");
        context.Reviews.Add(review);
        await context.SaveChangesAsync();

        var currentUserService = MockUser(customer);
        var handler = new SetReviewReplyCommandHandler(context, currentUserService.Object);

        var result = await handler.Handle(
            new SetReviewReplyCommand(review.Id, "Customer trying to reply"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Review.Forbidden");
    }

    [Fact]
    public async Task SetReviewReply_WhenStaffMember_SetsReplySuccessfully()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (customer, _, product, order) = SeedStandardScenario(context, userRole: UserRole.Customer);
        var staff = new User("staff@example.com", "hash", "Staff Member", UserRole.Staff);
        context.Users.Add(staff);
        var review = new Review(product.Id, customer.Id, order.Id, 5, "Title", "Content");
        context.Reviews.Add(review);
        await context.SaveChangesAsync();

        var currentUserService = MockUser(staff);
        var handler = new SetReviewReplyCommandHandler(context, currentUserService.Object);

        var result = await handler.Handle(
            new SetReviewReplyCommand(review.Id, "Thank you from staff!"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.StaffReply.Should().Be("Thank you from staff!");
        result.Value.StaffReplyAt.Should().NotBeNull();

        var dbReview = await context.Reviews.FindAsync(review.Id);
        dbReview!.StaffReply.Should().Be("Thank you from staff!");
    }

    [Fact]
    public async Task DeleteReviewReply_WhenStaffMember_ClearsReplySuccessfully()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (customer, _, product, order) = SeedStandardScenario(context, userRole: UserRole.Customer);
        var staff = new User("staff@example.com", "hash", "Staff Member", UserRole.Staff);
        context.Users.Add(staff);
        var review = new Review(product.Id, customer.Id, order.Id, 5, "Title", "Content");
        review.SetStaffReply("Initial reply");
        context.Reviews.Add(review);
        await context.SaveChangesAsync();

        var currentUserService = MockUser(staff);
        var handler = new DeleteReviewReplyCommandHandler(context, currentUserService.Object);

        var result = await handler.Handle(new DeleteReviewReplyCommand(review.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dbReview = await context.Reviews.FindAsync(review.Id);
        dbReview!.StaffReply.Should().BeNull();
        dbReview.StaffReplyAt.Should().BeNull();
    }

    // --- DeleteManagedReviewCommand Tests ---

    [Fact]
    public async Task DeleteManagedReview_WhenCustomerAttempts_ReturnsForbidden()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (customer, _, product, order) = SeedStandardScenario(context, userRole: UserRole.Customer);
        var review = new Review(product.Id, customer.Id, order.Id, 5, "Title", "Content");
        context.Reviews.Add(review);
        await context.SaveChangesAsync();

        var currentUserService = MockUser(customer);
        var handler = new DeleteManagedReviewCommandHandler(context, currentUserService.Object);

        var result = await handler.Handle(new DeleteManagedReviewCommand(review.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Review.Forbidden");
    }

    [Fact]
    public async Task DeleteManagedReview_WhenAdmin_DeletesAnyReviewSuccessfully()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (customer, _, product, order) = SeedStandardScenario(context, userRole: UserRole.Customer);
        var admin = new User("admin@example.com", "hash", "Admin", UserRole.Admin);
        context.Users.Add(admin);
        var review = new Review(product.Id, customer.Id, order.Id, 5, "Title", "Content");
        context.Reviews.Add(review);
        await context.SaveChangesAsync();

        var currentUserService = MockUser(admin);
        var handler = new DeleteManagedReviewCommandHandler(context, currentUserService.Object);

        var result = await handler.Handle(new DeleteManagedReviewCommand(review.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        context.Reviews.Should().BeEmpty();
    }
}
