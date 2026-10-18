using FluentAssertions;
using FluentValidation.TestHelper;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Products.Commands;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Test.Features.Products;

public class AdjustStockCommandHandlerTests
{
    private readonly AdjustStockCommandValidator _validator = new();

    private static Product CreateSampleProduct(int stock = 10)
    {
        return new Product(
            categoryId: Guid.NewGuid(),
            name: "Test CPU",
            slug: $"test-cpu-{Guid.NewGuid():N}",
            sku: $"TEST-CPU-{Guid.NewGuid():N}",
            brand: "Intel",
            price: 500m,
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
        var command = new AdjustStockCommand(
            ProductId: Guid.NewGuid(),
            ExpectedStockQuantity: 10,
            QuantityDelta: 5,
            Note: "Valid adjustment note");

        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validator_WhenProductIdEmpty_Fails()
    {
        var command = new AdjustStockCommand(
            ProductId: Guid.Empty,
            ExpectedStockQuantity: 10,
            QuantityDelta: 5,
            Note: "Valid note");

        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.ProductId);
    }

    [Fact]
    public void Validator_WhenExpectedStockQuantityNegative_Fails()
    {
        var command = new AdjustStockCommand(
            ProductId: Guid.NewGuid(),
            ExpectedStockQuantity: -1,
            QuantityDelta: 5,
            Note: "Valid note");

        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.ExpectedStockQuantity);
    }

    [Fact]
    public void Validator_WhenQuantityDeltaZero_Fails()
    {
        var command = new AdjustStockCommand(
            ProductId: Guid.NewGuid(),
            ExpectedStockQuantity: 10,
            QuantityDelta: 0,
            Note: "Valid note");

        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.QuantityDelta);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")] // length 2
    public void Validator_WhenNoteTooShortOrWhitespace_Fails(string invalidNote)
    {
        var command = new AdjustStockCommand(
            ProductId: Guid.NewGuid(),
            ExpectedStockQuantity: 10,
            QuantityDelta: 5,
            Note: invalidNote);

        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Note);
    }

    [Fact]
    public void Validator_WhenNoteExceeds500Chars_Fails()
    {
        var longNote = new string('x', 501);
        var command = new AdjustStockCommand(
            ProductId: Guid.NewGuid(),
            ExpectedStockQuantity: 10,
            QuantityDelta: 5,
            Note: longNote);

        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Note);
    }

    #endregion

    #region Handler Authorization & Business Logic Tests

    [Fact]
    public async Task Handle_WhenUnauthenticated_ReturnsUnauthorized()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(false);

        var handler = new AdjustStockCommandHandler(context, currentUserMock.Object);
        var command = new AdjustStockCommand(Guid.NewGuid(), 10, 5, "Adjustment");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task Handle_WhenCustomerRole_ReturnsForbidden()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var staff = CreateStaffUser(role: UserRole.Customer);
        context.Users.Add(staff);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(staff.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Customer.ToString());

        var handler = new AdjustStockCommandHandler(context, currentUserMock.Object);
        var command = new AdjustStockCommand(Guid.NewGuid(), 10, 5, "Adjustment");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task Handle_WhenUserInactive_ReturnsForbidden()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var staff = CreateStaffUser(isActive: false, role: UserRole.Staff);
        context.Users.Add(staff);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(staff.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Staff.ToString());

        var handler = new AdjustStockCommandHandler(context, currentUserMock.Object);
        var command = new AdjustStockCommand(Guid.NewGuid(), 10, 5, "Adjustment");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("User.AccountInactive");
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

        var handler = new AdjustStockCommandHandler(context, currentUserMock.Object);
        var command = new AdjustStockCommand(Guid.NewGuid(), 10, 5, "Adjustment");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Product.NotFound");
    }

    [Fact]
    public async Task Handle_WhenExpectedStockMismatchesCurrentStock_ReturnsConflict()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var staff = CreateStaffUser(role: UserRole.Admin);
        var product = CreateSampleProduct(stock: 10);
        context.Users.Add(staff);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(staff.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new AdjustStockCommandHandler(context, currentUserMock.Object);
        // Current is 10, expected passed is 8
        var command = new AdjustStockCommand(product.Id, ExpectedStockQuantity: 8, QuantityDelta: 5, Note: "Restock");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Inventory.StockConflict");
        product.StockQuantity.Should().Be(10);
    }

    [Fact]
    public async Task Handle_WhenDeltaCausesInsufficientStock_ReturnsConflict()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var staff = CreateStaffUser(role: UserRole.Staff);
        var product = CreateSampleProduct(stock: 5);
        context.Users.Add(staff);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(staff.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Staff.ToString());

        var handler = new AdjustStockCommandHandler(context, currentUserMock.Object);
        var command = new AdjustStockCommand(product.Id, ExpectedStockQuantity: 5, QuantityDelta: -10, Note: "Damage loss");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Inventory.InsufficientStock");
        product.StockQuantity.Should().Be(5);
    }

    [Fact]
    public async Task Handle_WhenValidPositiveAdjustment_SucceedsAndPersistsAudit()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var staff = CreateStaffUser(role: UserRole.Staff);
        var product = CreateSampleProduct(stock: 10);
        context.Users.Add(staff);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(staff.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Staff.ToString());

        var handler = new AdjustStockCommandHandler(context, currentUserMock.Object);
        var command = new AdjustStockCommand(product.Id, ExpectedStockQuantity: 10, QuantityDelta: 7, Note: "  Batch #123 restock  ");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.StockBefore.Should().Be(10);
        result.Value.QuantityDelta.Should().Be(7);
        result.Value.StockAfter.Should().Be(17);
        result.Value.Sku.Should().Be(product.Sku);
        result.Value.Reason.Should().Be(StockMovementReason.ManualAdjustment);
        result.Value.Note.Should().Be("Batch #123 restock");

        var updatedProduct = await context.Products.FindAsync(product.Id);
        updatedProduct!.StockQuantity.Should().Be(17);

        var movement = await context.StockMovements.FindAsync(result.Value.MovementId);
        movement.Should().NotBeNull();
        movement!.ActorUserId.Should().Be(staff.Id);
        movement.QuantityDelta.Should().Be(7);
        movement.StockBefore.Should().Be(10);
        movement.StockAfter.Should().Be(17);
        movement.Reason.Should().Be(StockMovementReason.ManualAdjustment);
    }

    [Fact]
    public async Task Handle_WhenValidNegativeAdjustment_SucceedsAndPersistsAudit()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var admin = CreateStaffUser(role: UserRole.Admin);
        var product = CreateSampleProduct(stock: 20);
        context.Users.Add(admin);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(admin.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new AdjustStockCommandHandler(context, currentUserMock.Object);
        var command = new AdjustStockCommand(product.Id, ExpectedStockQuantity: 20, QuantityDelta: -5, Note: "Defective units removed");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.StockBefore.Should().Be(20);
        result.Value.QuantityDelta.Should().Be(-5);
        result.Value.StockAfter.Should().Be(15);

        var updatedProduct = await context.Products.FindAsync(product.Id);
        updatedProduct!.StockQuantity.Should().Be(15);
    }

    #endregion
}
