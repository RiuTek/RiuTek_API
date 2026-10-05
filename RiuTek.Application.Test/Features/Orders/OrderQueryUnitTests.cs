using FluentAssertions;
using FluentValidation.TestHelper;
using Microsoft.EntityFrameworkCore;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Orders.Queries;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using Xunit;

namespace RiuTek.Application.Test.Features.Orders;

public class OrderQueryUnitTests
{
    private static Mock<ICurrentUserService> CreateCurrentUserMock(Guid? userId, bool isAuthenticated = true)
    {
        var mock = new Mock<ICurrentUserService>();
        mock.Setup(x => x.IsAuthenticated).Returns(isAuthenticated);
        mock.Setup(x => x.UserId).Returns(userId);
        return mock;
    }

    private static async Task<User> SeedUserAsync(TestApplicationDbContext context, bool isActive = true)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(
            email: $"user_{suffix}@riutek.test",
            passwordHash: "dummy_hash",
            fullName: $"Test User {suffix}",
            role: UserRole.Customer,
            phoneNumber: "0901234567"
        )
        {
            IsActive = isActive
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private static Order CreateSampleOrder(
        Guid userId,
        string orderNumberSuffix,
        OrderStatus status = OrderStatus.Confirmed,
        PaymentMethod paymentMethod = PaymentMethod.COD,
        string? notes = null)
    {
        var order = new Order(
            orderNumber: $"ORD-{orderNumberSuffix}",
            userId: userId,
            checkoutIdempotencyKey: $"key-{Guid.NewGuid():N}",
            customerName: $"Customer {orderNumberSuffix}",
            customerEmail: $"customer_{orderNumberSuffix}@test.com",
            customerPhone: "0901112233",
            shippingAddress: $"123 Street {orderNumberSuffix}",
            paymentMethod: paymentMethod,
            notes: notes
        );

        if (status != order.Status)
        {
            typeof(Order).GetProperty(nameof(Order.Status))!.SetValue(order, status);
        }

        return order;
    }

    #region Validator Tests

    [Fact]
    public void GetMyOrdersQueryValidator_WhenValid_PassesValidation()
    {
        var validator = new GetMyOrdersQueryValidator();
        var query = new GetMyOrdersQuery(PageIndex: 1, PageSize: 10, Status: OrderStatus.Confirmed);

        var result = validator.TestValidate(query);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void GetMyOrdersQueryValidator_WhenStatusNull_PassesValidation()
    {
        var validator = new GetMyOrdersQueryValidator();
        var query = new GetMyOrdersQuery(PageIndex: 1, PageSize: 10, Status: null);

        var result = validator.TestValidate(query);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetMyOrdersQueryValidator_WhenPageIndexLessThanOne_Fails(int pageIndex)
    {
        var validator = new GetMyOrdersQueryValidator();
        var query = new GetMyOrdersQuery(PageIndex: pageIndex, PageSize: 10);

        var result = validator.TestValidate(query);

        result.ShouldHaveValidationErrorFor(x => x.PageIndex)
            .WithErrorMessage("PageIndex must be greater than or equal to 1.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(51)]
    [InlineData(100)]
    public void GetMyOrdersQueryValidator_WhenPageSizeOutOfRange_Fails(int pageSize)
    {
        var validator = new GetMyOrdersQueryValidator();
        var query = new GetMyOrdersQuery(PageIndex: 1, PageSize: pageSize);

        var result = validator.TestValidate(query);

        result.ShouldHaveValidationErrorFor(x => x.PageSize)
            .WithErrorMessage("PageSize must be between 1 and 50.");
    }

    [Fact]
    public void GetMyOrdersQueryValidator_WhenStatusIsInvalidEnum_Fails()
    {
        var validator = new GetMyOrdersQueryValidator();
        var query = new GetMyOrdersQuery(PageIndex: 1, PageSize: 10, Status: (OrderStatus)999);

        var result = validator.TestValidate(query);

        result.ShouldHaveValidationErrorFor(x => x.Status)
            .WithErrorMessage("Status is invalid.");
    }

    [Fact]
    public void GetMyOrderByIdQueryValidator_WhenGuidValid_PassesValidation()
    {
        var validator = new GetMyOrderByIdQueryValidator();
        var query = new GetMyOrderByIdQuery(Guid.NewGuid());

        var result = validator.TestValidate(query);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void GetMyOrderByIdQueryValidator_WhenGuidEmpty_FailsValidation()
    {
        var validator = new GetMyOrderByIdQueryValidator();
        var query = new GetMyOrderByIdQuery(Guid.Empty);

        var result = validator.TestValidate(query);

        result.ShouldHaveValidationErrorFor(x => x.Id)
            .WithErrorMessage("Order Id is required.");
    }

    #endregion

    #region GetMyOrdersQueryHandler Tests

    [Fact]
    public async Task GetMyOrders_WhenAnonymous_ReturnsUnauthorized()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var userMock = CreateCurrentUserMock(null, isAuthenticated: false);
        var handler = new GetMyOrdersQueryHandler(context, userMock.Object);

        var result = await handler.Handle(new GetMyOrdersQuery(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
        result.Error.Code.Should().Be("Checkout.Unauthorized");
    }

    [Fact]
    public async Task GetMyOrders_WhenUserNotFoundInDb_ReturnsNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var userMock = CreateCurrentUserMock(Guid.NewGuid(), isAuthenticated: true);
        var handler = new GetMyOrdersQueryHandler(context, userMock.Object);

        var result = await handler.Handle(new GetMyOrdersQuery(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("Checkout.UserNotFound");
    }

    [Fact]
    public async Task GetMyOrders_WhenUserInactive_ReturnsForbidden()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context, isActive: false);
        var userMock = CreateCurrentUserMock(user.Id, isAuthenticated: true);
        var handler = new GetMyOrdersQueryHandler(context, userMock.Object);

        var result = await handler.Handle(new GetMyOrdersQuery(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        result.Error.Code.Should().Be("Checkout.AccountInactive");
    }

    [Fact]
    public async Task GetMyOrders_WhenNoOrders_ReturnsEmptyPagedResultWithCorrectMetadata()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context);
        var userMock = CreateCurrentUserMock(user.Id);
        var handler = new GetMyOrdersQueryHandler(context, userMock.Object);

        var result = await handler.Handle(new GetMyOrdersQuery(PageIndex: 1, PageSize: 10), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.Items.Should().BeEmpty();
        result.Value.TotalCount.Should().Be(0);
        result.Value.TotalPages.Should().Be(0);
        result.Value.PageIndex.Should().Be(1);
        result.Value.PageSize.Should().Be(10);
    }

    [Fact]
    public async Task GetMyOrders_OnlyReturnsOrdersForCurrentUser_DoesNotLeakOtherUsersOrders()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var userA = await SeedUserAsync(context);
        var userB = await SeedUserAsync(context);

        var orderA1 = CreateSampleOrder(userA.Id, "A1");
        var orderA2 = CreateSampleOrder(userA.Id, "A2");
        var orderB1 = CreateSampleOrder(userB.Id, "B1");

        context.Orders.AddRange(orderA1, orderA2, orderB1);
        await context.SaveChangesAsync();

        var userMock = CreateCurrentUserMock(userA.Id);
        var handler = new GetMyOrdersQueryHandler(context, userMock.Object);

        var result = await handler.Handle(new GetMyOrdersQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(2);
        result.Value.Items.Should().HaveCount(2);
        result.Value.Items.Select(x => x.Id).Should().Contain([orderA1.Id, orderA2.Id]);
        result.Value.Items.Select(x => x.Id).Should().NotContain(orderB1.Id);
    }

    [Fact]
    public async Task GetMyOrders_WhenFilteringByStatus_ReturnsOnlyMatchingStatus()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context);

        var orderConfirmed1 = CreateSampleOrder(user.Id, "CONF1", OrderStatus.Confirmed);
        var orderConfirmed2 = CreateSampleOrder(user.Id, "CONF2", OrderStatus.Confirmed);
        var orderPending = CreateSampleOrder(user.Id, "PEND1", OrderStatus.PendingPayment);
        var orderCompleted = CreateSampleOrder(user.Id, "COMPL1", OrderStatus.Completed);

        context.Orders.AddRange(orderConfirmed1, orderConfirmed2, orderPending, orderCompleted);
        await context.SaveChangesAsync();

        var userMock = CreateCurrentUserMock(user.Id);
        var handler = new GetMyOrdersQueryHandler(context, userMock.Object);

        // Filter Confirmed
        var resultConfirmed = await handler.Handle(new GetMyOrdersQuery(Status: OrderStatus.Confirmed), CancellationToken.None);
        resultConfirmed.IsSuccess.Should().BeTrue();
        resultConfirmed.Value.TotalCount.Should().Be(2);
        resultConfirmed.Value.Items.Should().OnlyContain(x => x.Status == OrderStatus.Confirmed);

        // Filter PendingPayment
        var resultPending = await handler.Handle(new GetMyOrdersQuery(Status: OrderStatus.PendingPayment), CancellationToken.None);
        resultPending.IsSuccess.Should().BeTrue();
        resultPending.Value.TotalCount.Should().Be(1);
        resultPending.Value.Items.Single().Status.Should().Be(OrderStatus.PendingPayment);

        // No filter -> returns all 4
        var resultAll = await handler.Handle(new GetMyOrdersQuery(Status: null), CancellationToken.None);
        resultAll.IsSuccess.Should().BeTrue();
        resultAll.Value.TotalCount.Should().Be(4);
        resultAll.Value.Items.Should().HaveCount(4);
    }

    [Fact]
    public async Task GetMyOrders_StableOrdering_WhenSameCreatedAt_OrdersByCreatedAtDescThenIdAsc()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context);

        var fixedTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var order1 = CreateSampleOrder(user.Id, "ORD1");
        var order2 = CreateSampleOrder(user.Id, "ORD2");
        var order3 = CreateSampleOrder(user.Id, "ORD3");

        typeof(BaseEntity).GetProperty(nameof(BaseEntity.CreatedAt))!.SetValue(order1, fixedTime);
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.CreatedAt))!.SetValue(order2, fixedTime);
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.CreatedAt))!.SetValue(order3, fixedTime);

        context.Orders.AddRange(order1, order2, order3);
        await context.SaveChangesAsync();

        var userMock = CreateCurrentUserMock(user.Id);
        var handler = new GetMyOrdersQueryHandler(context, userMock.Object);

        var result = await handler.Handle(new GetMyOrdersQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var expectedOrderIds = new[] { order1.Id, order2.Id, order3.Id }.OrderBy(id => id).ToList();
        result.Value.Items.Select(x => x.Id).Should().ContainInOrder(expectedOrderIds);
    }

    [Fact]
    public async Task GetMyOrders_Pagination_DoesNotOverlapOrMissItems()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context);

        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var orders = new List<Order>();
        for (int i = 1; i <= 5; i++)
        {
            var o = CreateSampleOrder(user.Id, $"NUM-{i}");
            typeof(BaseEntity).GetProperty(nameof(BaseEntity.CreatedAt))!.SetValue(o, baseTime.AddHours(i));
            orders.Add(o);
        }

        context.Orders.AddRange(orders);
        await context.SaveChangesAsync();

        var userMock = CreateCurrentUserMock(user.Id);
        var handler = new GetMyOrdersQueryHandler(context, userMock.Object);

        // Page 1 with pageSize 2 (should get items 5 and 4 by CreatedAt DESC)
        var p1 = await handler.Handle(new GetMyOrdersQuery(PageIndex: 1, PageSize: 2), CancellationToken.None);
        p1.IsSuccess.Should().BeTrue();
        p1.Value.Items.Should().HaveCount(2);
        p1.Value.TotalCount.Should().Be(5);
        p1.Value.TotalPages.Should().Be(3);

        // Page 2 with pageSize 2 (should get items 3 and 2)
        var p2 = await handler.Handle(new GetMyOrdersQuery(PageIndex: 2, PageSize: 2), CancellationToken.None);
        p2.IsSuccess.Should().BeTrue();
        p2.Value.Items.Should().HaveCount(2);

        // Page 3 with pageSize 2 (should get item 1)
        var p3 = await handler.Handle(new GetMyOrdersQuery(PageIndex: 3, PageSize: 2), CancellationToken.None);
        p3.IsSuccess.Should().BeTrue();
        p3.Value.Items.Should().HaveCount(1);

        // Verify no overlap
        var allFetchedIds = p1.Value.Items.Concat(p2.Value.Items).Concat(p3.Value.Items).Select(x => x.Id).ToList();
        allFetchedIds.Should().HaveCount(5);
        allFetchedIds.Distinct().Should().HaveCount(5);
    }

    [Fact]
    public async Task GetMyOrders_WhenPageBeyondTotalData_ReturnsEmptyItemsWithCorrectMetadata()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context);

        var order = CreateSampleOrder(user.Id, "ONE");
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var userMock = CreateCurrentUserMock(user.Id);
        var handler = new GetMyOrdersQueryHandler(context, userMock.Object);

        var result = await handler.Handle(new GetMyOrdersQuery(PageIndex: 10, PageSize: 10), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        result.Value.TotalCount.Should().Be(1);
        result.Value.TotalPages.Should().Be(1);
        result.Value.PageIndex.Should().Be(10);
        result.Value.PageSize.Should().Be(10);
    }

    [Fact]
    public async Task GetMyOrders_WhenPageIndexVeryLarge_DoesNotOverflowOrThrow()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context);

        var order = CreateSampleOrder(user.Id, "ONE");
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var userMock = CreateCurrentUserMock(user.Id);
        var handler = new GetMyOrdersQueryHandler(context, userMock.Object);

        // PageIndex is int.MaxValue -> offset would overflow int if not cast to long
        var result = await handler.Handle(new GetMyOrdersQuery(PageIndex: int.MaxValue, PageSize: 50), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        result.Value.TotalCount.Should().Be(1);
        result.Value.TotalPages.Should().Be(1);
        result.Value.PageIndex.Should().Be(int.MaxValue);
    }

    [Fact]
    public async Task GetMyOrders_ReturnsCorrectItemCount_WithoutExposingItemDetails()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context);

        var order = CreateSampleOrder(user.Id, "WITH_ITEMS");
        order.AddItem(Guid.NewGuid(), "Item 1", "SKU1", 100_000m, 2);
        order.AddItem(Guid.NewGuid(), "Item 2", "SKU2", 50_000m, 1);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var userMock = CreateCurrentUserMock(user.Id);
        var handler = new GetMyOrdersQueryHandler(context, userMock.Object);

        var result = await handler.Handle(new GetMyOrdersQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var summary = result.Value.Items.Single();
        summary.ItemCount.Should().Be(2);

        // Verify OrderSummaryDto type definition doesn't have Items or Address
        typeof(OrderSummaryDto).GetProperty("Items").Should().BeNull();
        typeof(OrderSummaryDto).GetProperty("ShippingAddress").Should().BeNull();
        typeof(OrderSummaryDto).GetProperty("CustomerPhone").Should().BeNull();
        typeof(OrderSummaryDto).GetProperty("CheckoutIdempotencyKey").Should().BeNull();
    }

    #endregion

    #region GetMyOrderByIdQueryHandler Tests

    [Fact]
    public async Task GetMyOrderById_WhenAnonymous_ReturnsUnauthorized()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var userMock = CreateCurrentUserMock(null, isAuthenticated: false);
        var handler = new GetMyOrderByIdQueryHandler(context, userMock.Object);

        var result = await handler.Handle(new GetMyOrderByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
        result.Error.Code.Should().Be("Checkout.Unauthorized");
    }

    [Fact]
    public async Task GetMyOrderById_WhenUserNotFoundInDb_ReturnsNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var userMock = CreateCurrentUserMock(Guid.NewGuid(), isAuthenticated: true);
        var handler = new GetMyOrderByIdQueryHandler(context, userMock.Object);

        var result = await handler.Handle(new GetMyOrderByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("Checkout.UserNotFound");
    }

    [Fact]
    public async Task GetMyOrderById_WhenUserInactive_ReturnsForbidden()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context, isActive: false);
        var userMock = CreateCurrentUserMock(user.Id);
        var handler = new GetMyOrderByIdQueryHandler(context, userMock.Object);

        var result = await handler.Handle(new GetMyOrderByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        result.Error.Code.Should().Be("Checkout.AccountInactive");
    }

    [Fact]
    public async Task GetMyOrderById_WhenOrderDoesNotExist_ReturnsOrderNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context);
        var userMock = CreateCurrentUserMock(user.Id);
        var handler = new GetMyOrderByIdQueryHandler(context, userMock.Object);

        var result = await handler.Handle(new GetMyOrderByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("Order.NotFound");
        result.Error.Description.Should().Be("Order was not found.");
    }

    [Fact]
    public async Task GetMyOrderById_WhenOrderBelongsToOtherUser_ReturnsOrderNotFound_HidingExistence()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var userA = await SeedUserAsync(context);
        var userB = await SeedUserAsync(context);

        var orderB = CreateSampleOrder(userB.Id, "ORDER_B");
        context.Orders.Add(orderB);
        await context.SaveChangesAsync();

        // User A attempts to view User B's order
        var userMockA = CreateCurrentUserMock(userA.Id);
        var handler = new GetMyOrderByIdQueryHandler(context, userMockA.Object);

        var result = await handler.Handle(new GetMyOrderByIdQuery(orderB.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("Order.NotFound");
        result.Error.Description.Should().Be("Order was not found.");
    }

    [Fact]
    public async Task GetMyOrderById_WhenOrderBelongsToCurrentUser_ReturnsFullSnapshotAndItemsSortedById()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context);

        var order = CreateSampleOrder(user.Id, "DETAIL1", OrderStatus.Confirmed, PaymentMethod.COD, notes: "Handle with care");
        var prodId1 = Guid.NewGuid();
        var prodId2 = Guid.NewGuid();

        order.AddItem(prodId1, "Product 1", "SKU-001", 100_000m, 2);
        order.AddItem(prodId2, "Product 2", "SKU-002", 50_000m, 3);

        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var userMock = CreateCurrentUserMock(user.Id);
        var handler = new GetMyOrderByIdQueryHandler(context, userMock.Object);

        var result = await handler.Handle(new GetMyOrderByIdQuery(order.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var detail = result.Value;
        detail.Id.Should().Be(order.Id);
        detail.OrderNumber.Should().Be(order.OrderNumber);
        detail.Status.Should().Be(OrderStatus.Confirmed);
        detail.PaymentMethod.Should().Be(PaymentMethod.COD);
        detail.PaymentStatus.Should().Be(PaymentStatus.Pending);
        detail.Currency.Should().Be("VND");
        detail.CustomerName.Should().Be(order.CustomerName);
        detail.CustomerEmail.Should().Be(order.CustomerEmail);
        detail.CustomerPhone.Should().Be(order.CustomerPhone);
        detail.ShippingAddress.Should().Be(order.ShippingAddress);
        detail.Notes.Should().Be("Handle with care");
        detail.TotalAmount.Should().Be(350_000m);
        detail.DiscountAmount.Should().Be(0m);
        detail.FinalAmount.Should().Be(350_000m);

        detail.Items.Should().HaveCount(2);
        var expectedSortedItems = order.Items.OrderBy(i => i.Id).ToList();
        detail.Items[0].ProductId.Should().Be(expectedSortedItems[0].ProductId);
        detail.Items[0].ProductName.Should().Be(expectedSortedItems[0].ProductName);
        detail.Items[0].ProductSku.Should().Be(expectedSortedItems[0].ProductSku);
        detail.Items[0].UnitPrice.Should().Be(expectedSortedItems[0].UnitPrice);
        detail.Items[0].Quantity.Should().Be(expectedSortedItems[0].Quantity);
        detail.Items[0].TotalPrice.Should().Be(expectedSortedItems[0].TotalPrice);

        detail.Items[1].ProductId.Should().Be(expectedSortedItems[1].ProductId);
    }

    [Fact]
    public async Task GetMyOrderById_WhenProductChangedOrDeletedInCatalog_ReturnsUnchangedSnapshot()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context);

        var product = new Product(
            categoryId: Guid.NewGuid(),
            name: "Original Product Name",
            slug: "orig-prod",
            sku: "ORIG-SKU",
            brand: "Brand",
            price: 150_000m,
            stockQuantity: 10,
            imageUrl: "https://riutek.test/p.png",
            componentType: ComponentType.Accessory,
            specifications: new AccessorySpecification { Details = "Specs" }
        );
        context.Products.Add(product);

        var order = CreateSampleOrder(user.Id, "PROD_CHANGE");
        order.AddItem(product.Id, product.Name, product.Sku, product.Price, 1);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        // Mutate product in catalog (e.g. price changed, name changed, made inactive)
        product.Name = "Mutated Name";
        product.Sku = "MUTATED-SKU";
        product.Price = 999_999m;
        product.IsActive = false;
        await context.SaveChangesAsync();

        var userMock = CreateCurrentUserMock(user.Id);
        var handler = new GetMyOrderByIdQueryHandler(context, userMock.Object);

        var result = await handler.Handle(new GetMyOrderByIdQuery(order.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var item = result.Value.Items.Single();
        item.ProductName.Should().Be("Original Product Name", "Snapshot must preserve historical product name");
        item.ProductSku.Should().Be("ORIG-SKU", "Snapshot must preserve historical SKU");
        item.UnitPrice.Should().Be(150_000m, "Snapshot must preserve historical unit price");
        item.TotalPrice.Should().Be(150_000m);
    }

    [Fact]
    public void OrderDetailDto_DoesNotContainInternalOrSensitiveFields()
    {
        typeof(OrderDetailDto).GetProperty("CheckoutIdempotencyKey").Should().BeNull();
        typeof(OrderDetailDto).GetProperty("PaymentAttempts").Should().BeNull();
        typeof(OrderDetailDto).GetProperty("User").Should().BeNull();
    }

    #endregion
}
