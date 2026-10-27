using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Orders.Commands;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using Xunit;

namespace RiuTek.Application.Test.Features.Orders;

public class CancelMyOrderUnitTests
{
    private static Mock<ICurrentUserService> CreateCurrentUserServiceMock(Guid? userId, bool isAuthenticated = true)
    {
        var mock = new Mock<ICurrentUserService>();
        mock.Setup(s => s.UserId).Returns(userId);
        mock.Setup(s => s.IsAuthenticated).Returns(isAuthenticated && userId.HasValue);
        return mock;
    }

    private static (User User, Product Product, Order Order) SeedOrder(
        TestApplicationDbContext context,
        PaymentMethod paymentMethod,
        OrderStatus status,
        int stockQuantity = 10,
        int itemQuantity = 2,
        string? providerReference = "cs_test_session_123")
    {
        var user = new User(
            $"user_{Guid.NewGuid():N}@riutek.test",
            "hash",
            "Customer Test",
            UserRole.Customer,
            "0901234567")
        {
            IsActive = true
        };
        context.Users.Add(user);

        var category = new Category("Category Test", "cat-test", ComponentType.Accessory);
        context.Categories.Add(category);

        var product = new Product(
            category.Id,
            "Product Test",
            "prod-test",
            "SKU-TEST",
            "Brand",
            100_000m,
            stockQuantity,
            "https://riutek.test/img.png",
            ComponentType.Accessory,
            new AccessorySpecification { Details = "Specs" });
        context.Products.Add(product);

        var order = new Order(
            $"ORD-{Guid.NewGuid():N}",
            user.Id,
            $"checkout-{Guid.NewGuid():N}",
            user.FullName,
            user.Email,
            user.PhoneNumber!,
            "123 Street",
            paymentMethod);

        order.AddItem(product.Id, product.Name, product.Sku, product.Price, itemQuantity);

        if (paymentMethod == PaymentMethod.Stripe)
        {
            var attempt = order.CreatePaymentAttempt("attempt-1", DateTime.UtcNow.AddMinutes(30), DateTime.UtcNow).Value;
            if (providerReference != null)
            {
                attempt.SetProviderReference(providerReference);
            }
        }

        if (status == OrderStatus.Cancelled)
        {
            if (paymentMethod == PaymentMethod.Stripe && order.PaymentAttempts.Any())
            {
                order.CancelPendingStripePayment(order.PaymentAttempts.First().Id, DateTime.UtcNow);
            }
            else
            {
                order.ChangeFulfillmentStatus(order.Status, OrderStatus.Cancelled);
            }
        }
        else if (status != order.Status)
        {
            SetOrderStatus(order, status);
        }

        context.Orders.Add(order);
        context.SaveChanges();

        return (user, product, order);
    }

    private static void SetOrderStatus(Order order, OrderStatus targetStatus)
    {
        if (order.Status == targetStatus) return;

        if (order.PaymentMethod == PaymentMethod.Stripe && order.Status == OrderStatus.PendingPayment)
        {
            var attempt = order.PaymentAttempts.First();
            if (string.IsNullOrEmpty(attempt.ProviderReference)) attempt.SetProviderReference("cs_test");
            order.MarkPaymentSucceeded(attempt.Id, DateTime.UtcNow);
        }

        if (order.Status == targetStatus) return;

        if (targetStatus == OrderStatus.Processing)
        {
            order.ChangeFulfillmentStatus(OrderStatus.Confirmed, OrderStatus.Processing);
        }
        else if (targetStatus == OrderStatus.Shipping)
        {
            if (order.Status == OrderStatus.Confirmed) order.ChangeFulfillmentStatus(OrderStatus.Confirmed, OrderStatus.Processing);
            order.ChangeFulfillmentStatus(OrderStatus.Processing, OrderStatus.Shipping);
        }
        else if (targetStatus == OrderStatus.Completed)
        {
            if (order.Status == OrderStatus.Confirmed) order.ChangeFulfillmentStatus(OrderStatus.Confirmed, OrderStatus.Processing);
            if (order.Status == OrderStatus.Processing) order.ChangeFulfillmentStatus(OrderStatus.Processing, OrderStatus.Shipping);
            order.ChangeFulfillmentStatus(OrderStatus.Shipping, OrderStatus.Completed);
        }
        else
        {
            typeof(Order).GetProperty(nameof(Order.Status))!.SetValue(order, targetStatus);
        }
    }

    [Fact]
    public async Task Handle_WhenCodConfirmedOrder_CancelsOrderAndRestoresStock_StripeGatewayNotCalled()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, product, order) = SeedOrder(context, PaymentMethod.COD, OrderStatus.Confirmed, stockQuantity: 10, itemQuantity: 3);

        var currentUserServiceMock = CreateCurrentUserServiceMock(user.Id);
        var stripeGatewayMock = new Mock<IStripePaymentGateway>();

        var handler = new CancelMyOrderCommandHandler(context, currentUserServiceMock.Object, stripeGatewayMock.Object);

        var result = await handler.Handle(new CancelMyOrderCommand(order.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(OrderStatus.Cancelled);

        var updatedOrder = await context.Orders.FindAsync(order.Id);
        updatedOrder!.Status.Should().Be(OrderStatus.Cancelled);

        var updatedProduct = await context.Products.FindAsync(product.Id);
        updatedProduct!.StockQuantity.Should().Be(13); // 10 + 3 restored

        var movements = await context.StockMovements.Where(m => m.OrderId == order.Id).ToListAsync();
        movements.Should().ContainSingle();
        movements[0].Reason.Should().Be(StockMovementReason.OrderCancellation);
        movements[0].QuantityDelta.Should().Be(3);
        movements[0].ActorUserId.Should().Be(user.Id);

        stripeGatewayMock.Verify(g => g.ExpireCheckoutSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenStripePendingPaymentOrder_CallsGatewayFirst_CancelsOrderAndAttempt_RestoresStock()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, product, order) = SeedOrder(context, PaymentMethod.Stripe, OrderStatus.PendingPayment, stockQuantity: 5, itemQuantity: 2);

        var currentUserServiceMock = CreateCurrentUserServiceMock(user.Id);
        var callOrder = new List<string>();

        var stripeGatewayMock = new Mock<IStripePaymentGateway>();
        stripeGatewayMock.Setup(g => g.ExpireCheckoutSessionAsync("cs_test_session_123", It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("GatewayExpire"))
            .ReturnsAsync(Result.Success());

        var handler = new CancelMyOrderCommandHandler(context, currentUserServiceMock.Object, stripeGatewayMock.Object);

        var result = await handler.Handle(new CancelMyOrderCommand(order.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(OrderStatus.Cancelled);
        result.Value.PaymentStatus.Should().Be(PaymentStatus.Failed);

        callOrder.Should().ContainSingle().Which.Should().Be("GatewayExpire");

        var updatedOrder = await context.Orders.Include(o => o.PaymentAttempts).FirstAsync(o => o.Id == order.Id);
        updatedOrder.Status.Should().Be(OrderStatus.Cancelled);
        updatedOrder.PaymentStatus.Should().Be(PaymentStatus.Failed);
        updatedOrder.PaymentAttempts.Should().ContainSingle();
        updatedOrder.PaymentAttempts.First().Status.Should().Be(PaymentAttemptStatus.Cancelled);

        var updatedProduct = await context.Products.FindAsync(product.Id);
        updatedProduct!.StockQuantity.Should().Be(7); // 5 + 2 restored

        var movements = await context.StockMovements.Where(m => m.OrderId == order.Id).ToListAsync();
        movements.Should().ContainSingle();
        movements[0].Reason.Should().Be(StockMovementReason.OrderCancellation);
        movements[0].QuantityDelta.Should().Be(2);
        movements[0].ActorUserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task Handle_WhenStripeGatewayReturnsUnavailable_DoesNotMutateOrderOrRestoreStock_Returns503()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, product, order) = SeedOrder(context, PaymentMethod.Stripe, OrderStatus.PendingPayment, stockQuantity: 5, itemQuantity: 2);

        var currentUserServiceMock = CreateCurrentUserServiceMock(user.Id);

        var stripeGatewayMock = new Mock<IStripePaymentGateway>();
        stripeGatewayMock.Setup(g => g.ExpireCheckoutSessionAsync("cs_test_session_123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(Error.Unavailable("Payment.GatewayUnavailable", "Stripe unavailable")));

        var handler = new CancelMyOrderCommandHandler(context, currentUserServiceMock.Object, stripeGatewayMock.Object);

        var result = await handler.Handle(new CancelMyOrderCommand(order.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Payment.GatewayUnavailable");
        result.Error.Type.Should().Be(ErrorType.Unavailable);

        var unchangedOrder = await context.Orders.Include(o => o.PaymentAttempts).FirstAsync(o => o.Id == order.Id);
        unchangedOrder.Status.Should().Be(OrderStatus.PendingPayment);
        unchangedOrder.PaymentStatus.Should().Be(PaymentStatus.Pending);
        unchangedOrder.PaymentAttempts.First().Status.Should().Be(PaymentAttemptStatus.Pending);

        var unchangedProduct = await context.Products.FindAsync(product.Id);
        unchangedProduct!.StockQuantity.Should().Be(5);

        var movements = await context.StockMovements.Where(m => m.OrderId == order.Id).ToListAsync();
        movements.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenStripeGatewayReturnsConflict_DoesNotMutateOrderOrRestoreStock_ReturnsConflict()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, product, order) = SeedOrder(context, PaymentMethod.Stripe, OrderStatus.PendingPayment, stockQuantity: 5, itemQuantity: 2);

        var currentUserServiceMock = CreateCurrentUserServiceMock(user.Id);

        var stripeGatewayMock = new Mock<IStripePaymentGateway>();
        stripeGatewayMock.Setup(g => g.ExpireCheckoutSessionAsync("cs_test_session_123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(Error.Conflict("Payment.CannotExpireCompletedSession", "Session completed")));

        var handler = new CancelMyOrderCommandHandler(context, currentUserServiceMock.Object, stripeGatewayMock.Object);

        var result = await handler.Handle(new CancelMyOrderCommand(order.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Payment.CannotExpireCompletedSession");
        result.Error.Type.Should().Be(ErrorType.Conflict);

        var unchangedOrder = await context.Orders.FindAsync(order.Id);
        unchangedOrder!.Status.Should().Be(OrderStatus.PendingPayment);

        var unchangedProduct = await context.Products.FindAsync(product.Id);
        unchangedProduct!.StockQuantity.Should().Be(5);
    }

    [Fact]
    public async Task Handle_WhenNoPendingPaymentAttemptFound_ReturnsConflictWithoutMutation()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, product, order) = SeedOrder(context, PaymentMethod.Stripe, OrderStatus.PendingPayment, stockQuantity: 5, itemQuantity: 2);

        // Mark the attempt failed so there are 0 pending attempts
        order.PaymentAttempts.First().MarkFailed("FAILED", DateTime.UtcNow);
        context.SaveChanges();

        var currentUserServiceMock = CreateCurrentUserServiceMock(user.Id);
        var stripeGatewayMock = new Mock<IStripePaymentGateway>();

        var handler = new CancelMyOrderCommandHandler(context, currentUserServiceMock.Object, stripeGatewayMock.Object);

        var result = await handler.Handle(new CancelMyOrderCommand(order.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Order.PendingPaymentAttemptMissing");

        stripeGatewayMock.Verify(g => g.ExpireCheckoutSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPendingAttemptHasEmptyProviderReference_ReturnsConflictWithoutMutation()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, product, order) = SeedOrder(context, PaymentMethod.Stripe, OrderStatus.PendingPayment, stockQuantity: 5, itemQuantity: 2, providerReference: null);

        var currentUserServiceMock = CreateCurrentUserServiceMock(user.Id);
        var stripeGatewayMock = new Mock<IStripePaymentGateway>();

        var handler = new CancelMyOrderCommandHandler(context, currentUserServiceMock.Object, stripeGatewayMock.Object);

        var result = await handler.Handle(new CancelMyOrderCommand(order.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Order.PendingPaymentAttemptMissing");

        stripeGatewayMock.Verify(g => g.ExpireCheckoutSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenOrderAlreadyCancelled_ReturnsSuccessWithoutCallingGatewayOrRestoringStockAgain()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, product, order) = SeedOrder(context, PaymentMethod.Stripe, OrderStatus.Cancelled, stockQuantity: 10, itemQuantity: 2);

        var currentUserServiceMock = CreateCurrentUserServiceMock(user.Id);
        var stripeGatewayMock = new Mock<IStripePaymentGateway>();

        var handler = new CancelMyOrderCommandHandler(context, currentUserServiceMock.Object, stripeGatewayMock.Object);

        var result = await handler.Handle(new CancelMyOrderCommand(order.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(OrderStatus.Cancelled);

        var unchangedProduct = await context.Products.FindAsync(product.Id);
        unchangedProduct!.StockQuantity.Should().Be(10); // Not changed

        var movements = await context.StockMovements.Where(m => m.OrderId == order.Id).ToListAsync();
        movements.Should().BeEmpty();

        stripeGatewayMock.Verify(g => g.ExpireCheckoutSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenUserNotAuthenticated_ReturnsUnauthorized()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, product, order) = SeedOrder(context, PaymentMethod.Stripe, OrderStatus.PendingPayment);

        var currentUserServiceMock = CreateCurrentUserServiceMock(null, isAuthenticated: false);
        var stripeGatewayMock = new Mock<IStripePaymentGateway>();

        var handler = new CancelMyOrderCommandHandler(context, currentUserServiceMock.Object, stripeGatewayMock.Object);

        var result = await handler.Handle(new CancelMyOrderCommand(order.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.Unauthorized");
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task Handle_WhenUserInactive_ReturnsForbidden()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, product, order) = SeedOrder(context, PaymentMethod.Stripe, OrderStatus.PendingPayment);

        user.IsActive = false;
        context.SaveChanges();

        var currentUserServiceMock = CreateCurrentUserServiceMock(user.Id);
        var stripeGatewayMock = new Mock<IStripePaymentGateway>();

        var handler = new CancelMyOrderCommandHandler(context, currentUserServiceMock.Object, stripeGatewayMock.Object);

        var result = await handler.Handle(new CancelMyOrderCommand(order.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.AccountInactive");
        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task Handle_WhenOrderBelongsToDifferentUser_ReturnsNotFound()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, product, order) = SeedOrder(context, PaymentMethod.Stripe, OrderStatus.PendingPayment);

        var diffUser = new User("other@riutek.test", "hash", "Other User", UserRole.Customer, "0900000001") { IsActive = true };
        context.Users.Add(diffUser);
        context.SaveChanges();

        var currentUserServiceMock = CreateCurrentUserServiceMock(diffUser.Id);
        var stripeGatewayMock = new Mock<IStripePaymentGateway>();

        var handler = new CancelMyOrderCommandHandler(context, currentUserServiceMock.Object, stripeGatewayMock.Object);

        var result = await handler.Handle(new CancelMyOrderCommand(order.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Order.NotFound");
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_WhenOrderDoesNotExist_ReturnsNotFound()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, _) = SeedOrder(context, PaymentMethod.Stripe, OrderStatus.PendingPayment);

        var currentUserServiceMock = CreateCurrentUserServiceMock(user.Id);
        var stripeGatewayMock = new Mock<IStripePaymentGateway>();

        var handler = new CancelMyOrderCommandHandler(context, currentUserServiceMock.Object, stripeGatewayMock.Object);

        var result = await handler.Handle(new CancelMyOrderCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Order.NotFound");
    }

    [Theory]
    [InlineData(PaymentMethod.COD, OrderStatus.PendingPayment)]
    [InlineData(PaymentMethod.COD, OrderStatus.Processing)]
    [InlineData(PaymentMethod.COD, OrderStatus.Shipping)]
    [InlineData(PaymentMethod.COD, OrderStatus.Completed)]
    [InlineData(PaymentMethod.Stripe, OrderStatus.Confirmed)]
    [InlineData(PaymentMethod.Stripe, OrderStatus.Processing)]
    [InlineData(PaymentMethod.Stripe, OrderStatus.Shipping)]
    [InlineData(PaymentMethod.Stripe, OrderStatus.Completed)]
    public async Task Handle_WhenDisallowedOrderStatus_ReturnsConflict(PaymentMethod method, OrderStatus status)
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, product, order) = SeedOrder(context, method, status);

        var currentUserServiceMock = CreateCurrentUserServiceMock(user.Id);
        var stripeGatewayMock = new Mock<IStripePaymentGateway>();

        var handler = new CancelMyOrderCommandHandler(context, currentUserServiceMock.Object, stripeGatewayMock.Object);

        var result = await handler.Handle(new CancelMyOrderCommand(order.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Order.CustomerCancellationNotAllowed");
    }

    [Fact]
    public async Task Handle_WhenConcurrencyOccurs_AndOrderBecomesCancelled_ReturnsSuccessWithoutDuplicateRestoration()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, product, order) = SeedOrder(context, PaymentMethod.Stripe, OrderStatus.PendingPayment, stockQuantity: 5, itemQuantity: 2);

        var currentUserServiceMock = CreateCurrentUserServiceMock(user.Id);

        var stripeGatewayMock = new Mock<IStripePaymentGateway>();
        stripeGatewayMock.Setup(g => g.ExpireCheckoutSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        bool concurrencyThrown = false;
        context.SaveChangesInterceptor = () =>
        {
            if (!concurrencyThrown)
            {
                concurrencyThrown = true;
                // Simulate concurrent winner cancelling the order directly in DB
                order.CancelPendingStripePayment(order.PaymentAttempts.First().Id, DateTime.UtcNow);
                context.SaveChangesInterceptor = null;
                context.SaveChanges();

                throw new DbUpdateConcurrencyException("Concurrency conflict");
            }
            return Task.FromResult(1);
        };

        var handler = new CancelMyOrderCommandHandler(context, currentUserServiceMock.Object, stripeGatewayMock.Object);

        var result = await handler.Handle(new CancelMyOrderCommand(order.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(OrderStatus.Cancelled);
    }
}
