using FluentAssertions;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Reviews.Queries;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using Xunit;

namespace RiuTek.Application.Test.Features.Reviews;

public class ReviewQueryHandlerTests
{
    private static CpuSpecification CreateCpuSpec() => new()
    {
        Socket = CpuSocket.AM5,
        CoreCount = 8,
        ThreadCount = 16,
        BaseClockGhz = 3.8,
        BoostClockGhz = 5.0,
        TdpWattage = 105,
        HasIntegratedGpu = true,
        SupportedMemoryType = RamType.DDR5,
        MaxMemorySpeedMhz = 5200
    };

    private static (Category Category, Product Product, User Customer1, User Customer2, Order Order1, Order Order2) SeedScenario(
        TestApplicationDbContext context)
    {
        var category = new Category("Electronics", "electronics", ComponentType.Cpu, "desc");
        context.Categories.Add(category);

        var product = new Product(
            category.Id,
            "Test GPU",
            "test-gpu",
            "SKU-GPU-1",
            "Nvidia",
            15_000_000m,
            5,
            "gpu.jpg",
            ComponentType.Cpu,
            CreateCpuSpec());
        context.Products.Add(product);

        var customer1 = new User("cust1@example.com", "hash", "Nguyen Van A", UserRole.Customer);
        var customer2 = new User("cust2@example.com", "hash", "Tran Thi B", UserRole.Customer);
        context.Users.AddRange(customer1, customer2);

        var order1 = new Order(
            $"ORD-{Guid.NewGuid():N}"[..20],
            customer1.Id,
            $"key-{Guid.NewGuid():N}",
            "Nguyen Van A",
            "cust1@example.com",
            "0911111111",
            "Address 1",
            PaymentMethod.COD);
        order1.AddItem(product.Id, product.Name, product.Sku, product.Price, 1);
        order1.ChangeFulfillmentStatus(OrderStatus.Confirmed, OrderStatus.Processing);
        order1.ChangeFulfillmentStatus(OrderStatus.Processing, OrderStatus.Shipping);
        order1.ChangeFulfillmentStatus(OrderStatus.Shipping, OrderStatus.Completed);

        var order2 = new Order(
            $"ORD-{Guid.NewGuid():N}"[..20],
            customer2.Id,
            $"key-{Guid.NewGuid():N}",
            "Tran Thi B",
            "cust2@example.com",
            "0922222222",
            "Address 2",
            PaymentMethod.COD);
        order2.AddItem(product.Id, product.Name, product.Sku, product.Price, 1);
        order2.ChangeFulfillmentStatus(OrderStatus.Confirmed, OrderStatus.Processing);
        order2.ChangeFulfillmentStatus(OrderStatus.Processing, OrderStatus.Shipping);
        order2.ChangeFulfillmentStatus(OrderStatus.Shipping, OrderStatus.Completed);

        context.Orders.AddRange(order1, order2);

        return (category, product, customer1, customer2, order1, order2);
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

    // --- GetProductReviewsQuery Tests ---

    [Fact]
    public async Task GetProductReviews_WhenProductNotFound_ReturnsProductNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var handler = new GetProductReviewsQueryHandler(context);

        var result = await handler.Handle(new GetProductReviewsQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Review.ProductNotFound");
    }

    [Fact]
    public async Task GetProductReviews_WhenProductHasZeroReviews_ReturnsZeroSummaryAndEmptyList()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (_, product, _, _, _, _) = SeedScenario(context);
        await context.SaveChangesAsync();

        var handler = new GetProductReviewsQueryHandler(context);

        var result = await handler.Handle(new GetProductReviewsQuery(product.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var data = result.Value;
        data.AverageRating.Should().Be(0.0m);
        data.TotalReviews.Should().Be(0);
        data.RatingDistribution.OneStar.Should().Be(0);
        data.RatingDistribution.TwoStar.Should().Be(0);
        data.RatingDistribution.ThreeStar.Should().Be(0);
        data.RatingDistribution.FourStar.Should().Be(0);
        data.RatingDistribution.FiveStar.Should().Be(0);
        data.Reviews.Items.Should().BeEmpty();
        data.Reviews.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetProductReviews_WithMultipleReviews_CalculatesSummaryAndHidesSensitiveData()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (_, product, cust1, cust2, order1, order2) = SeedScenario(context);

        // Ratings: 5 and 4 -> Average = 4.5
        var review1 = new Review(product.Id, cust1.Id, order1.Id, 5, "Amazing", "Runs super fast")
        {
            CreatedAt = DateTime.UtcNow.AddMinutes(-10)
        };
        var review2 = new Review(product.Id, cust2.Id, order2.Id, 4, "Good", "A bit warm")
        {
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        context.Reviews.AddRange(review1, review2);
        await context.SaveChangesAsync();

        var handler = new GetProductReviewsQueryHandler(context);

        var result = await handler.Handle(new GetProductReviewsQuery(product.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var data = result.Value;
        data.AverageRating.Should().Be(4.5m);
        data.TotalReviews.Should().Be(2);
        data.RatingDistribution.FiveStar.Should().Be(1);
        data.RatingDistribution.FourStar.Should().Be(1);
        data.RatingDistribution.ThreeStar.Should().Be(0);
        data.RatingDistribution.TwoStar.Should().Be(0);
        data.RatingDistribution.OneStar.Should().Be(0);
        data.Reviews.TotalCount.Should().Be(2);
        data.Reviews.Items.Should().HaveCount(2);

        // Deterministic ordering: CreatedAt descending -> review2 first
        var firstItem = data.Reviews.Items[0];
        firstItem.Rating.Should().Be(4);
        firstItem.UserDisplayName.Should().Be("Tran Thi B");

        // Verify no UserId or OrderId property exists on ProductReviewDto
        typeof(DTOs.ProductReviewDto).GetProperty("UserId").Should().BeNull();
        typeof(DTOs.ProductReviewDto).GetProperty("OrderId").Should().BeNull();
    }

    [Fact]
    public async Task GetProductReviews_WhenFilteredByRating_FiltersListOnlyWhileSummaryIncludesAll()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (_, product, cust1, cust2, order1, order2) = SeedScenario(context);

        var review1 = new Review(product.Id, cust1.Id, order1.Id, 5, "Five star", null);
        var review2 = new Review(product.Id, cust2.Id, order2.Id, 4, "Four star", null);
        context.Reviews.AddRange(review1, review2);
        await context.SaveChangesAsync();

        var handler = new GetProductReviewsQueryHandler(context);

        // Filter for rating 5 only
        var result = await handler.Handle(new GetProductReviewsQuery(product.Id, Rating: 5), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var data = result.Value;

        // TotalReviews and Distribution should still reflect ALL reviews
        data.TotalReviews.Should().Be(2);
        data.AverageRating.Should().Be(4.5m);
        data.RatingDistribution.FiveStar.Should().Be(1);
        data.RatingDistribution.FourStar.Should().Be(1);

        // But the paged items list should contain only rating 5
        data.Reviews.Items.Should().HaveCount(1);
        data.Reviews.TotalCount.Should().Be(1);
        data.Reviews.Items[0].Rating.Should().Be(5);
    }

    [Fact]
    public async Task GetProductReviews_PageBeyondEnd_ReturnsEmptyItemsGracefully()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (_, product, cust1, _, order1, _) = SeedScenario(context);
        var review = new Review(product.Id, cust1.Id, order1.Id, 5, "Title", "Content");
        context.Reviews.Add(review);
        await context.SaveChangesAsync();

        var handler = new GetProductReviewsQueryHandler(context);

        var result = await handler.Handle(new GetProductReviewsQuery(product.Id, PageIndex: 999, PageSize: 10), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Reviews.Items.Should().BeEmpty();
        result.Value.Reviews.TotalCount.Should().Be(1);
    }

    // --- GetMyReviewsQuery Tests ---

    [Fact]
    public async Task GetMyReviews_ReturnsOnlyAuthenticatedUserReviews()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (_, product, cust1, cust2, order1, order2) = SeedScenario(context);

        var review1 = new Review(product.Id, cust1.Id, order1.Id, 5, "My Review", "Great");
        var review2 = new Review(product.Id, cust2.Id, order2.Id, 3, "Other Review", "Okay");
        context.Reviews.AddRange(review1, review2);
        await context.SaveChangesAsync();

        var currentUserService = MockUser(cust1);
        var handler = new GetMyReviewsQueryHandler(context, currentUserService.Object);

        var result = await handler.Handle(new GetMyReviewsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1);
        result.Value.Items.Should().HaveCount(1);
        result.Value.Items[0].Id.Should().Be(review1.Id);
        result.Value.Items[0].ProductName.Should().Be(product.Name);
        result.Value.Items[0].ProductSlug.Should().Be("test-gpu");
    }

    // --- GetManagedReviewsQuery Tests ---

    [Fact]
    public async Task GetManagedReviews_WhenCustomerAttempts_ReturnsForbidden()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (_, _, cust1, _, _, _) = SeedScenario(context);
        await context.SaveChangesAsync();

        var currentUserService = MockUser(cust1);
        var handler = new GetManagedReviewsQueryHandler(context, currentUserService.Object);

        var result = await handler.Handle(new GetManagedReviewsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Review.Forbidden");
    }

    [Fact]
    public async Task GetManagedReviews_WhenStaffOrAdmin_ReturnsReviewsWithUserDetails()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (_, product, cust1, _, order1, _) = SeedScenario(context);

        var staff = new User("staff@example.com", "hash", "Staff Member", UserRole.Staff);
        context.Users.Add(staff);

        var review = new Review(product.Id, cust1.Id, order1.Id, 5, "Cust Review", "Detail");
        context.Reviews.Add(review);
        await context.SaveChangesAsync();

        var currentUserService = MockUser(staff);
        var handler = new GetManagedReviewsQueryHandler(context, currentUserService.Object);

        var result = await handler.Handle(new GetManagedReviewsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1);
        var item = result.Value.Items[0];
        item.Id.Should().Be(review.Id);
        item.UserId.Should().Be(cust1.Id);
        item.UserDisplayName.Should().Be("Nguyen Van A");
        item.OrderId.Should().Be(order1.Id);
    }
}
