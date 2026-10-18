using FluentAssertions;
using RiuTek.Application.Features.Products.Common;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Test.Features.Products;

public class StockMovementUnitTests
{
    private static Product CreateSampleProduct(int stock = 10)
    {
        return new Product(
            categoryId: Guid.NewGuid(),
            name: "Test CPU",
            slug: "test-cpu",
            sku: "TEST-CPU-01",
            brand: "Intel",
            price: 100m,
            stockQuantity: stock,
            imageUrl: "https://example.com/cpu.jpg",
            componentType: ComponentType.Cpu,
            specifications: new CpuSpecification
            {
                Socket = CpuSocket.LGA1700,
                CoreCount = 8,
                ThreadCount = 16,
                BaseClockGhz = 3.6,
                BoostClockGhz = 5.0,
                TdpWattage = 65,
                HasIntegratedGpu = true,
                SupportedMemoryType = RamType.DDR5,
                MaxMemorySpeedMhz = 5600
            }
        );
    }

    [Fact]
    public void Constructor_WhenProductIdIsEmpty_ThrowsArgumentException()
    {
        var act = () => new StockMovement(
            productId: Guid.Empty,
            quantityDelta: 5,
            stockBefore: 0,
            stockAfter: 5,
            reason: StockMovementReason.ProductCreated);

        act.Should().Throw<ArgumentException>().WithMessage("*ProductId*");
    }

    [Fact]
    public void Constructor_WhenQuantityDeltaIsZero_ThrowsArgumentException()
    {
        var act = () => new StockMovement(
            productId: Guid.NewGuid(),
            quantityDelta: 0,
            stockBefore: 5,
            stockAfter: 5,
            reason: StockMovementReason.ProductCreated);

        act.Should().Throw<ArgumentException>().WithMessage("*QuantityDelta*");
    }

    [Fact]
    public void Constructor_WhenStockBeforeIsNegative_ThrowsArgumentException()
    {
        var act = () => new StockMovement(
            productId: Guid.NewGuid(),
            quantityDelta: 5,
            stockBefore: -1,
            stockAfter: 4,
            reason: StockMovementReason.ManualAdjustment,
            note: "Adjust",
            actorUserId: Guid.NewGuid());

        act.Should().Throw<ArgumentException>().WithMessage("*StockBefore*");
    }

    [Fact]
    public void Constructor_WhenStockAfterIsNegative_ThrowsArgumentException()
    {
        var act = () => new StockMovement(
            productId: Guid.NewGuid(),
            quantityDelta: -5,
            stockBefore: 2,
            stockAfter: -3,
            reason: StockMovementReason.ManualAdjustment,
            note: "Adjust",
            actorUserId: Guid.NewGuid());

        act.Should().Throw<ArgumentException>().WithMessage("*StockAfter*");
    }

    [Fact]
    public void Constructor_WhenStockEquationDoesNotMatch_ThrowsInvalidOperationException()
    {
        var act = () => new StockMovement(
            productId: Guid.NewGuid(),
            quantityDelta: 5,
            stockBefore: 10,
            stockAfter: 20, // Should be 15
            reason: StockMovementReason.ProductCreated);

        act.Should().Throw<InvalidOperationException>().WithMessage("*StockAfter*");
    }

    [Fact]
    public void Constructor_WhenManualAdjustmentWithoutActor_ThrowsArgumentException()
    {
        var act = () => new StockMovement(
            productId: Guid.NewGuid(),
            quantityDelta: 5,
            stockBefore: 10,
            stockAfter: 15,
            reason: StockMovementReason.ManualAdjustment,
            note: "Valid note",
            actorUserId: null);

        act.Should().Throw<ArgumentException>().WithMessage("*ActorUserId*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WhenManualAdjustmentWithoutNote_ThrowsArgumentException(string? invalidNote)
    {
        var act = () => new StockMovement(
            productId: Guid.NewGuid(),
            quantityDelta: 5,
            stockBefore: 10,
            stockAfter: 15,
            reason: StockMovementReason.ManualAdjustment,
            note: invalidNote,
            actorUserId: Guid.NewGuid());

        act.Should().Throw<ArgumentException>().WithMessage("*Note*");
    }

    [Theory]
    [InlineData(StockMovementReason.Checkout)]
    [InlineData(StockMovementReason.OrderCancellation)]
    [InlineData(StockMovementReason.PaymentExpiration)]
    public void Constructor_WhenOrderReasonWithoutOrderId_ThrowsArgumentException(StockMovementReason reason)
    {
        var act = () => new StockMovement(
            productId: Guid.NewGuid(),
            quantityDelta: -2,
            stockBefore: 10,
            stockAfter: 8,
            reason: reason,
            orderId: null);

        act.Should().Throw<ArgumentException>().WithMessage("*OrderId*");
    }

    [Fact]
    public void Constructor_WhenValidManualAdjustment_TrimsNoteAndSetsProperties()
    {
        var prodId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var movement = new StockMovement(
            productId: prodId,
            quantityDelta: 5,
            stockBefore: 10,
            stockAfter: 15,
            reason: StockMovementReason.ManualAdjustment,
            note: "  Restock batch A  ",
            actorUserId: actorId);

        movement.ProductId.Should().Be(prodId);
        movement.QuantityDelta.Should().Be(5);
        movement.StockBefore.Should().Be(10);
        movement.StockAfter.Should().Be(15);
        movement.Reason.Should().Be(StockMovementReason.ManualAdjustment);
        movement.Note.Should().Be("Restock batch A");
        movement.ActorUserId.Should().Be(actorId);
        movement.OrderId.Should().BeNull();
        movement.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void ApplyStockMovement_WhenPositiveDelta_UpdatesStockAndAddsMovement()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var product = CreateSampleProduct(stock: 10);
        var actorId = Guid.NewGuid();

        var result = StockMovementHelper.ApplyStockMovement(
            product,
            quantityDelta: 5,
            reason: StockMovementReason.ManualAdjustment,
            note: "Nhập thêm hàng",
            actorUserId: actorId,
            orderId: null,
            context);

        result.IsSuccess.Should().BeTrue();
        product.StockQuantity.Should().Be(15);
        context.StockMovements.Local.Should().ContainSingle();
        var movement = context.StockMovements.Local.First();
        movement.StockBefore.Should().Be(10);
        movement.StockAfter.Should().Be(15);
        movement.QuantityDelta.Should().Be(5);
        movement.Reason.Should().Be(StockMovementReason.ManualAdjustment);
    }

    [Fact]
    public void ApplyStockMovement_WhenNegativeDelta_ReducesStockAndAddsMovement()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var product = CreateSampleProduct(stock: 10);
        var orderId = Guid.NewGuid();

        var result = StockMovementHelper.ApplyStockMovement(
            product,
            quantityDelta: -3,
            reason: StockMovementReason.Checkout,
            note: null,
            actorUserId: Guid.NewGuid(),
            orderId: orderId,
            context);

        result.IsSuccess.Should().BeTrue();
        product.StockQuantity.Should().Be(7);
        context.StockMovements.Local.Should().ContainSingle();
        var movement = context.StockMovements.Local.First();
        movement.StockBefore.Should().Be(10);
        movement.StockAfter.Should().Be(7);
        movement.QuantityDelta.Should().Be(-3);
        movement.Reason.Should().Be(StockMovementReason.Checkout);
        movement.OrderId.Should().Be(orderId);
    }

    [Fact]
    public void ApplyStockMovement_WhenInsufficientStock_ReturnsConflictError()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var product = CreateSampleProduct(stock: 3);

        var result = StockMovementHelper.ApplyStockMovement(
            product,
            quantityDelta: -5,
            reason: StockMovementReason.Checkout,
            note: null,
            actorUserId: Guid.NewGuid(),
            orderId: Guid.NewGuid(),
            context);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Inventory.InsufficientStock");
        product.StockQuantity.Should().Be(3);
        context.StockMovements.Local.Should().BeEmpty();
    }

    [Fact]
    public void ApplyStockMovement_WhenZeroDelta_ReturnsValidationError()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var product = CreateSampleProduct(stock: 10);

        var result = StockMovementHelper.ApplyStockMovement(
            product,
            quantityDelta: 0,
            reason: StockMovementReason.ProductCreated,
            note: null,
            actorUserId: null,
            orderId: null,
            context);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Inventory.InvalidDelta");
        context.StockMovements.Local.Should().BeEmpty();
    }

    [Fact]
    public void ApplyStockMovement_WhenManualAdjustmentNoteTooShort_ReturnsValidationError()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var product = CreateSampleProduct(stock: 10);

        var result = StockMovementHelper.ApplyStockMovement(
            product,
            quantityDelta: 5,
            reason: StockMovementReason.ManualAdjustment,
            note: "ok", // Less than 3 characters
            actorUserId: Guid.NewGuid(),
            orderId: null,
            context);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Inventory.InvalidNote");
    }
}
