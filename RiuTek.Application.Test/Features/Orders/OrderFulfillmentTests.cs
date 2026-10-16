using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Orders.Commands;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Test.Features.Orders;

public class OrderFulfillmentTests
{
    [Fact]
    public void CodOrder_ValidFulfillmentPath_CompletesOrderAndPayment()
    {
        var order = CreateOrder(PaymentMethod.COD);

        order.ChangeFulfillmentStatus(OrderStatus.Confirmed, OrderStatus.Processing).IsSuccess.Should().BeTrue();
        order.ChangeFulfillmentStatus(OrderStatus.Processing, OrderStatus.Shipping).IsSuccess.Should().BeTrue();
        order.ChangeFulfillmentStatus(OrderStatus.Shipping, OrderStatus.Completed).IsSuccess.Should().BeTrue();

        order.Status.Should().Be(OrderStatus.Completed);
        order.PaymentStatus.Should().Be(PaymentStatus.Completed);
    }

    [Fact]
    public void Order_InvalidJump_IsRejectedWithoutMutation()
    {
        var order = CreateOrder(PaymentMethod.COD);

        var result = order.ChangeFulfillmentStatus(
            OrderStatus.Confirmed,
            OrderStatus.Completed);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Order.InvalidStatusTransition");
        order.Status.Should().Be(OrderStatus.Confirmed);
        order.PaymentStatus.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public void Order_ReplayingAppliedTransition_IsIdempotent()
    {
        var order = CreateOrder(PaymentMethod.COD);
        order.ChangeFulfillmentStatus(OrderStatus.Confirmed, OrderStatus.Processing);

        var replay = order.ChangeFulfillmentStatus(
            OrderStatus.Confirmed,
            OrderStatus.Processing);

        replay.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Processing);
    }

    [Fact]
    public void PaidStripeOrder_CannotBeCancelledWithoutRefundWorkflow()
    {
        var order = CreateOrder(PaymentMethod.Stripe);
        order.AddItem(
            Guid.NewGuid(),
            "Stripe Product",
            "STRIPE-PRODUCT",
            100_000m).IsSuccess.Should().BeTrue();
        var attempt = order.CreatePaymentAttempt(
            "stripe-order-fulfillment-attempt",
            DateTime.UtcNow.AddMinutes(30));
        attempt.IsSuccess.Should().BeTrue();
        attempt.Value.SetProviderReference("cs_test_fulfillment")
            .IsSuccess.Should().BeTrue();
        order.MarkPaymentSucceeded(attempt.Value.Id, DateTime.UtcNow)
            .IsSuccess.Should().BeTrue();

        var result = order.ChangeFulfillmentStatus(
            OrderStatus.Confirmed,
            OrderStatus.Cancelled);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Order.PaidOrderRequiresRefund");
        order.Status.Should().Be(OrderStatus.Confirmed);
        order.PaymentStatus.Should().Be(PaymentStatus.Completed);
    }

    [Fact]
    public async Task Handler_CancelConfirmedOrder_RestoresStockExactlyOnce()
    {
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var admin = new User("admin-orders@riutek.test", "hash", "Admin", UserRole.Admin)
        {
            IsActive = true
        };
        var customer = new User("customer-orders@riutek.test", "hash", "Customer", UserRole.Customer)
        {
            IsActive = true
        };
        var category = new Category("CPU", "cpu-orders", ComponentType.Cpu);
        var product = new Product(
            category.Id,
            "AMD Ryzen 5 7600",
            "amd-ryzen-5-7600-orders",
            "CPU-ORDER-CANCEL",
            "AMD",
            5_000_000,
            7,
            "https://media.riutek.test/cpu.webp",
            ComponentType.Cpu,
            CreateCpuSpecification());
        var order = CreateOrder(PaymentMethod.COD, customer.Id);
        order.AddItem(product.Id, product.Name, product.Sku, product.Price, quantity: 3)
            .IsSuccess.Should().BeTrue();

        db.Users.AddRange(admin, customer);
        db.Categories.Add(category);
        db.Products.Add(product);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var currentUser = CreateCurrentUser(admin);
        var handler = new UpdateOrderStatusCommandHandler(db, currentUser.Object);
        var command = new UpdateOrderStatusCommand(
            order.Id,
            OrderStatus.Confirmed,
            OrderStatus.Cancelled);

        var first = await handler.Handle(command, CancellationToken.None);
        var replay = await handler.Handle(command, CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        replay.IsSuccess.Should().BeTrue();
        first.Value.Status.Should().Be(OrderStatus.Cancelled);
        (await db.Products.SingleAsync()).StockQuantity.Should().Be(10);
    }

    [Fact]
    public async Task Handler_CustomerCannotManageOrder()
    {
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var customer = new User("forbidden-orders@riutek.test", "hash", "Customer", UserRole.Customer)
        {
            IsActive = true
        };
        db.Users.Add(customer);
        await db.SaveChangesAsync();

        var handler = new UpdateOrderStatusCommandHandler(db, CreateCurrentUser(customer).Object);

        var result = await handler.Handle(
            new UpdateOrderStatusCommand(Guid.NewGuid(), OrderStatus.Confirmed, OrderStatus.Processing),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("OrderManagement.Forbidden");
    }

    private static Order CreateOrder(PaymentMethod paymentMethod, Guid? userId = null)
    {
        return new Order(
            $"ORD-{Guid.NewGuid():N}"[..20],
            userId ?? Guid.NewGuid(),
            $"order-key-{Guid.NewGuid():N}",
            "Test Customer",
            "customer@riutek.test",
            "0900000000",
            "123 Test Street",
            paymentMethod);
    }

    private static CpuSpecification CreateCpuSpecification() => new()
    {
        Socket = CpuSocket.AM5,
        CoreCount = 6,
        ThreadCount = 12,
        BaseClockGhz = 3.8,
        BoostClockGhz = 5.1,
        TdpWattage = 65,
        HasIntegratedGpu = true,
        SupportedMemoryType = RamType.DDR5,
        MaxMemorySpeedMhz = 5200
    };

    private static Mock<ICurrentUserService> CreateCurrentUser(User user)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(x => x.IsAuthenticated).Returns(true);
        currentUser.Setup(x => x.UserId).Returns(user.Id);
        currentUser.Setup(x => x.UserRole).Returns(user.Role.ToString());
        return currentUser;
    }
}
