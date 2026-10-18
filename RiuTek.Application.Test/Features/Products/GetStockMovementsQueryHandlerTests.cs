using FluentAssertions;
using FluentValidation.TestHelper;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Products.Queries;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Test.Features.Products;

public class GetStockMovementsQueryHandlerTests
{
    private readonly GetStockMovementsQueryValidator _validator = new();

    private static Product CreateSampleProduct(int stock = 10)
    {
        return new Product(
            categoryId: Guid.NewGuid(),
            name: "Test CPU",
            slug: $"test-cpu-{Guid.NewGuid():N}",
            sku: $"TEST-CPU-{Guid.NewGuid():N}",
            brand: "Intel",
            price: 150m,
            stockQuantity: stock,
            imageUrl: "https://example.com/cpu.jpg",
            componentType: ComponentType.Cpu,
            specifications: ProductCommandValidatorTests.CreateValidCpuSpec()
        );
    }

    private static User CreateStaffUser(bool isActive = true, UserRole role = UserRole.Staff)
    {
        return new User(
            email: $"staff_{Guid.NewGuid():N}@riutek.com",
            passwordHash: "hash",
            fullName: "Staff Member",
            role: role)
        {
            IsActive = isActive
        };
    }

    #region Validator Tests

    [Fact]
    public void Validator_WhenValid_Passes()
    {
        var query = new GetStockMovementsQuery(
            ProductId: Guid.NewGuid(),
            PageIndex: 1,
            PageSize: 20,
            Reason: StockMovementReason.ManualAdjustment);

        var result = _validator.TestValidate(query);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validator_WhenProductIdEmpty_Fails()
    {
        var query = new GetStockMovementsQuery(
            ProductId: Guid.Empty,
            PageIndex: 1,
            PageSize: 20);

        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.ProductId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validator_WhenPageIndexInvalid_Fails(int pageIndex)
    {
        var query = new GetStockMovementsQuery(
            ProductId: Guid.NewGuid(),
            PageIndex: pageIndex,
            PageSize: 20);

        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.PageIndex);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Validator_WhenPageSizeOutOfRange_Fails(int pageSize)
    {
        var query = new GetStockMovementsQuery(
            ProductId: Guid.NewGuid(),
            PageIndex: 1,
            PageSize: pageSize);

        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.PageSize);
    }

    [Fact]
    public void Validator_WhenReasonInvalidEnum_Fails()
    {
        var query = new GetStockMovementsQuery(
            ProductId: Guid.NewGuid(),
            PageIndex: 1,
            PageSize: 20,
            Reason: (StockMovementReason)999);

        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.Reason);
    }

    #endregion

    #region Handler Tests

    [Fact]
    public async Task Handle_WhenUnauthenticated_ReturnsUnauthorized()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(false);

        var handler = new GetStockMovementsQueryHandler(context, currentUserMock.Object);
        var query = new GetStockMovementsQuery(Guid.NewGuid());

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task Handle_WhenCustomerRole_ReturnsForbidden()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = CreateStaffUser(role: UserRole.Customer);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(user.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Customer.ToString());

        var handler = new GetStockMovementsQueryHandler(context, currentUserMock.Object);
        var query = new GetStockMovementsQuery(Guid.NewGuid());

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task Handle_WhenProductNotFound_ReturnsNotFound()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var staff = CreateStaffUser(role: UserRole.Staff);
        context.Users.Add(staff);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(staff.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Staff.ToString());

        var handler = new GetStockMovementsQueryHandler(context, currentUserMock.Object);
        var query = new GetStockMovementsQuery(Guid.NewGuid());

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Product.NotFound");
    }

    [Fact]
    public async Task Handle_WhenProductHasMovements_ReturnsPagedHistoryOrderedDesc()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var staff = CreateStaffUser(role: UserRole.Staff);
        var product = CreateSampleProduct(stock: 15);
        context.Users.Add(staff);
        context.Products.Add(product);

        var m1 = new StockMovement(product.Id, 10, 0, 10, StockMovementReason.ProductCreated)
        {
            CreatedAt = DateTime.UtcNow.AddMinutes(-30)
        };
        var m2 = new StockMovement(product.Id, 5, 10, 15, StockMovementReason.ManualAdjustment, "Restocked", staff.Id)
        {
            CreatedAt = DateTime.UtcNow.AddMinutes(-10)
        };
        context.StockMovements.AddRange(m1, m2);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(staff.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Staff.ToString());

        var handler = new GetStockMovementsQueryHandler(context, currentUserMock.Object);
        var query = new GetStockMovementsQuery(product.Id, PageIndex: 1, PageSize: 10);

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(2);
        result.Value.Items.Should().HaveCount(2);

        // Most recent first (m2 then m1)
        result.Value.Items[0].Id.Should().Be(m2.Id);
        result.Value.Items[0].QuantityDelta.Should().Be(5);
        result.Value.Items[0].StockBefore.Should().Be(10);
        result.Value.Items[0].StockAfter.Should().Be(15);
        result.Value.Items[0].Reason.Should().Be(StockMovementReason.ManualAdjustment);
        result.Value.Items[0].Note.Should().Be("Restocked");
        result.Value.Items[0].ActorUserId.Should().Be(staff.Id);
        result.Value.Items[0].ActorUserName.Should().Be(staff.FullName);
        result.Value.Items[0].Sku.Should().Be(product.Sku);

        result.Value.Items[1].Id.Should().Be(m1.Id);
        result.Value.Items[1].Reason.Should().Be(StockMovementReason.ProductCreated);
    }

    [Fact]
    public async Task Handle_WhenFilterByReason_ReturnsOnlyMatchingMovements()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var staff = CreateStaffUser(role: UserRole.Staff);
        var product = CreateSampleProduct(stock: 12);
        context.Users.Add(staff);
        context.Products.Add(product);

        var m1 = new StockMovement(product.Id, 10, 0, 10, StockMovementReason.ProductCreated);
        var m2 = new StockMovement(product.Id, 2, 10, 12, StockMovementReason.ManualAdjustment, "Restocked", staff.Id);
        context.StockMovements.AddRange(m1, m2);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(staff.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Staff.ToString());

        var handler = new GetStockMovementsQueryHandler(context, currentUserMock.Object);
        var query = new GetStockMovementsQuery(product.Id, PageIndex: 1, PageSize: 10, Reason: StockMovementReason.ManualAdjustment);

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1);
        result.Value.Items.Should().ContainSingle();
        result.Value.Items[0].Reason.Should().Be(StockMovementReason.ManualAdjustment);
    }

    #endregion
}
