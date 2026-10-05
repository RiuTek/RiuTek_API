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

public class CheckoutStripeUnitTests
{
    private static async Task<(User User, UserAddress Address, Product Product, Cart Cart)> SeedPrerequisitesAsync(
        TestApplicationDbContext context,
        int stockQuantity = 50,
        decimal price = 200_000m,
        int cartQuantity = 2)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(
            email: $"stripe_user_{suffix}@riutek.test",
            passwordHash: "dummy_hash",
            fullName: $"Stripe User {suffix}",
            role: UserRole.Customer,
            phoneNumber: "0901234567"
        )
        {
            IsActive = true
        };
        context.Users.Add(user);

        var address = new UserAddress(
            userId: user.Id,
            receiverName: $"Receiver {suffix}",
            phoneNumber: "0909999999",
            addressLine: "123 Test Street",
            ward: "Ward 1",
            district: "District 1",
            city: "Ho Chi Minh City",
            isDefault: true
        );
        context.UserAddresses.Add(address);

        var category = new Category(
            name: $"Category {suffix}",
            slug: $"cat-{suffix}",
            componentType: ComponentType.Accessory
        );
        context.Categories.Add(category);

        var product = new Product(
            categoryId: category.Id,
            name: $"Product {suffix}",
            slug: $"prod-{suffix}",
            sku: $"SKU-{suffix}",
            brand: "TestBrand",
            price: price,
            stockQuantity: stockQuantity,
            imageUrl: "https://riutek.test/img.png",
            componentType: ComponentType.Accessory,
            specifications: new AccessorySpecification { Details = "Specs" }
        )
        {
            IsActive = true
        };
        context.Products.Add(product);

        var cart = new Cart(user.Id);
        if (cartQuantity > 0)
        {
            cart.AddItem(product.Id, cartQuantity);
        }
        context.Carts.Add(cart);

        await context.SaveChangesAsync();
        return (user, address, product, cart);
    }

    private static Mock<ICurrentUserService> CreateCurrentUserMock(Guid userId)
    {
        var mock = new Mock<ICurrentUserService>();
        mock.Setup(u => u.IsAuthenticated).Returns(true);
        mock.Setup(u => u.UserId).Returns(userId);
        mock.Setup(u => u.UserRole).Returns(UserRole.Customer.ToString());
        return mock;
    }

    [Fact]
    public async Task CheckoutCart_WhenStripeDisabled_ReturnsPaymentMethodNotAvailable()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, cart) = await SeedPrerequisitesAsync(context);
        var authMock = CreateCurrentUserMock(user.Id);

        var gatewayMock = new Mock<IStripePaymentGateway>();
        gatewayMock.Setup(g => g.IsEnabled).Returns(false);

        var handler = new CheckoutCartCommandHandler(context, authMock.Object, gatewayMock.Object);

        var command = new CheckoutCartCommand(
            AddressId: address.Id,
            ExpectedCartVersion: cart.Version,
            PaymentMethod: PaymentMethod.Stripe,
            Notes: null,
            IdempotencyKey: "valid-stripe-key-disabled-12345"
        );

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.PaymentMethodNotAvailable");
        (await context.Orders.CountAsync()).Should().Be(0);
        (await context.PaymentAttempts.CountAsync()).Should().Be(0);

        var productDb = await context.Products.FindAsync(product.Id);
        productDb!.StockQuantity.Should().Be(50);

        var cartDb = await context.Carts.Include(c => c.Items).FirstAsync(c => c.UserId == user.Id);
        cartDb.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task CheckoutCart_WhenStripeSuccessful_CreatesOrderAttemptStockCartAndReturnsPaymentAction()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, cart) = await SeedPrerequisitesAsync(context, cartQuantity: 2);
        var authMock = CreateCurrentUserMock(user.Id);

        var gatewayMock = new Mock<IStripePaymentGateway>();
        gatewayMock.Setup(g => g.IsEnabled).Returns(true);
        gatewayMock.Setup(g => g.EnsureCheckoutSessionAsync(
                It.IsAny<CreateStripeCheckoutSessionRequest>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((CreateStripeCheckoutSessionRequest req, string? r, CancellationToken _) =>
                Result.Success(new StripeCheckoutSessionResult(
                    SessionId: "cs_test_session_1",
                    Url: "https://checkout.stripe.com/pay/cs_test_session_1",
                    ExpiresAt: req.ExpiresAt
                )));

        var handler = new CheckoutCartCommandHandler(context, authMock.Object, gatewayMock.Object);

        var idempotencyKey = "valid-stripe-key-success-12345";
        var command = new CheckoutCartCommand(
            AddressId: address.Id,
            ExpectedCartVersion: cart.Version,
            PaymentMethod: PaymentMethod.Stripe,
            Notes: "Stripe order notes",
            IdempotencyKey: idempotencyKey
        );

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.PaymentMethod.Should().Be(PaymentMethod.Stripe);
        dto.Status.Should().Be(OrderStatus.PendingPayment);
        dto.PaymentStatus.Should().Be(PaymentStatus.Pending);
        dto.TotalAmount.Should().Be(400_000m);
        dto.PaymentAction.Should().NotBeNull();
        dto.PaymentAction!.Type.Should().Be("Redirect");
        dto.PaymentAction.Url.Should().Be("https://checkout.stripe.com/pay/cs_test_session_1");

        // Verify DB
        var order = await context.Orders
            .Include(o => o.Items)
            .Include(o => o.PaymentAttempts)
            .FirstOrDefaultAsync(o => o.Id == dto.Id);

        order.Should().NotBeNull();
        order!.PaymentMethod.Should().Be(PaymentMethod.Stripe);
        order.Status.Should().Be(OrderStatus.PendingPayment);
        order.PaymentStatus.Should().Be(PaymentStatus.Pending);
        order.CheckoutIdempotencyKey.Should().Be(idempotencyKey);
        order.PaymentAttempts.Should().HaveCount(1);

        var attempt = order.PaymentAttempts.First();
        attempt.Status.Should().Be(PaymentAttemptStatus.Pending);
        attempt.Method.Should().Be(PaymentMethod.Stripe);
        attempt.Amount.Should().Be(400_000m);
        attempt.Currency.Should().Be("VND");
        attempt.ProviderReference.Should().Be("cs_test_session_1");
        attempt.IdempotencyKey.Should().Be($"stripe-{order.Id:N}");

        // Stock held
        var productDb = await context.Products.FindAsync(product.Id);
        productDb!.StockQuantity.Should().Be(48);

        // Cart cleared
        var cartDb = await context.Carts.Include(c => c.Items).FirstAsync(c => c.UserId == user.Id);
        cartDb.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckoutCart_WhenReplayedWithSameIdempotencyKey_ReturnsSameOrderAndSessionWithoutRecreatingOrDecreasingStock()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, cart) = await SeedPrerequisitesAsync(context, cartQuantity: 2);
        var authMock = CreateCurrentUserMock(user.Id);

        var gatewayMock = new Mock<IStripePaymentGateway>();
        gatewayMock.Setup(g => g.IsEnabled).Returns(true);
        gatewayMock.Setup(g => g.EnsureCheckoutSessionAsync(
                It.IsAny<CreateStripeCheckoutSessionRequest>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((CreateStripeCheckoutSessionRequest req, string? r, CancellationToken _) =>
                Result.Success(new StripeCheckoutSessionResult(
                    SessionId: "cs_test_session_replay",
                    Url: "https://checkout.stripe.com/pay/cs_test_session_replay",
                    ExpiresAt: req.ExpiresAt
                )));

        var handler = new CheckoutCartCommandHandler(context, authMock.Object, gatewayMock.Object);

        var idempotencyKey = "valid-stripe-replay-key-123456";
        var command = new CheckoutCartCommand(
            AddressId: address.Id,
            ExpectedCartVersion: cart.Version,
            PaymentMethod: PaymentMethod.Stripe,
            Notes: null,
            IdempotencyKey: idempotencyKey
        );

        // First call
        var result1 = await handler.Handle(command, CancellationToken.None);
        result1.IsSuccess.Should().BeTrue();

        // Second call with same key
        var result2 = await handler.Handle(command, CancellationToken.None);
        result2.IsSuccess.Should().BeTrue();

        result2.Value.Id.Should().Be(result1.Value.Id);
        result2.Value.PaymentAction!.Url.Should().Be("https://checkout.stripe.com/pay/cs_test_session_replay");

        (await context.Orders.CountAsync()).Should().Be(1);
        (await context.PaymentAttempts.CountAsync()).Should().Be(1);

        var productDb = await context.Products.FindAsync(product.Id);
        productDb!.StockQuantity.Should().Be(48); // not decremented twice
    }

    [Fact]
    public async Task CheckoutCart_WhenGatewayFailsOrTimesOut_OrderAndPendingAttemptPersistedAndReturns503()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, cart) = await SeedPrerequisitesAsync(context, cartQuantity: 2);
        var authMock = CreateCurrentUserMock(user.Id);

        var gatewayMock = new Mock<IStripePaymentGateway>();
        gatewayMock.Setup(g => g.IsEnabled).Returns(true);
        // Gateway unavailable on initial checkout
        gatewayMock.Setup(g => g.EnsureCheckoutSessionAsync(
                It.IsAny<CreateStripeCheckoutSessionRequest>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<StripeCheckoutSessionResult>(Error.Unavailable(
                "Payment.GatewayUnavailable",
                "Stripe payment gateway is temporarily unavailable.")));

        var handler = new CheckoutCartCommandHandler(context, authMock.Object, gatewayMock.Object);

        var idempotencyKey = "stripe-timeout-retry-key-12345";
        var command = new CheckoutCartCommand(
            AddressId: address.Id,
            ExpectedCartVersion: cart.Version,
            PaymentMethod: PaymentMethod.Stripe,
            Notes: null,
            IdempotencyKey: idempotencyKey
        );

        var result1 = await handler.Handle(command, CancellationToken.None);

        result1.IsFailure.Should().BeTrue();
        result1.Error.Type.Should().Be(ErrorType.Unavailable);
        result1.Error.Code.Should().Be("Payment.GatewayUnavailable");

        // Order and Pending attempt are persisted in DB!
        var orders = await context.Orders.Include(o => o.PaymentAttempts).ToListAsync();
        orders.Should().HaveCount(1);
        var order = orders.First();
        order.Status.Should().Be(OrderStatus.PendingPayment);
        order.PaymentAttempts.Should().HaveCount(1);
        var attempt = order.PaymentAttempts.First();
        attempt.Status.Should().Be(PaymentAttemptStatus.Pending);
        attempt.ProviderReference.Should().BeNull(); // failed before reference saved

        // Product stock is decremented
        var productDb = await context.Products.FindAsync(product.Id);
        productDb!.StockQuantity.Should().Be(48);

        // Cart is cleared
        var cartDb = await context.Carts.Include(c => c.Items).FirstAsync(c => c.UserId == user.Id);
        cartDb.Items.Should().BeEmpty();

        // RETRY: Now gateway succeeds!
        gatewayMock.Setup(g => g.EnsureCheckoutSessionAsync(
                It.IsAny<CreateStripeCheckoutSessionRequest>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((CreateStripeCheckoutSessionRequest req, string? r, CancellationToken _) =>
                Result.Success(new StripeCheckoutSessionResult(
                    SessionId: "cs_test_recovered_session",
                    Url: "https://checkout.stripe.com/pay/cs_test_recovered_session",
                    ExpiresAt: req.ExpiresAt
                )));

        var result2 = await handler.Handle(command, CancellationToken.None);

        result2.IsSuccess.Should().BeTrue();
        result2.Value.Id.Should().Be(order.Id);
        result2.Value.PaymentAction!.Url.Should().Be("https://checkout.stripe.com/pay/cs_test_recovered_session");

        // Verify provider reference is now saved to existing attempt
        var updatedAttempt = await context.PaymentAttempts.FirstAsync(p => p.Id == attempt.Id);
        updatedAttempt.ProviderReference.Should().Be("cs_test_recovered_session");

        // Ensure stock not decremented again
        productDb = await context.Products.FindAsync(product.Id);
        productDb!.StockQuantity.Should().Be(48);
    }

    [Fact]
    public async Task CheckoutCart_WhenExistingAttemptAlreadySucceeded_ReturnsOrderWithoutCreatingSession()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, _, cart) = await SeedPrerequisitesAsync(context);
        var authMock = CreateCurrentUserMock(user.Id);

        var idempotencyKey = "stripe-already-succeeded-key-12345";
        var order = new Order(
            orderNumber: "ORD-EXISTING-1",
            userId: user.Id,
            checkoutIdempotencyKey: idempotencyKey,
            customerName: user.FullName,
            customerEmail: user.Email,
            customerPhone: user.PhoneNumber!,
            shippingAddress: "123 Street",
            paymentMethod: PaymentMethod.Stripe,
            notes: null
        );
        order.AddItem(Guid.NewGuid(), "Item", "SKU", 100_000m, 1);
        var attemptRes = order.CreatePaymentAttempt("key", DateTime.UtcNow.AddMinutes(30));
        attemptRes.Value.SetProviderReference("cs_test_succeeded_ref");
        var markRes = order.MarkPaymentSucceeded(attemptRes.Value.Id, DateTime.UtcNow);
        markRes.IsSuccess.Should().BeTrue();

        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var gatewayMock = new Mock<IStripePaymentGateway>();
        gatewayMock.Setup(g => g.IsEnabled).Returns(true);

        var handler = new CheckoutCartCommandHandler(context, authMock.Object, gatewayMock.Object);

        var command = new CheckoutCartCommand(
            AddressId: address.Id,
            ExpectedCartVersion: cart.Version,
            PaymentMethod: PaymentMethod.Stripe,
            Notes: null,
            IdempotencyKey: idempotencyKey
        );

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PaymentAction.Should().BeNull(); // Succeeded already, no redirect action
        gatewayMock.Verify(g => g.EnsureCheckoutSessionAsync(
            It.IsAny<CreateStripeCheckoutSessionRequest>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckoutCart_WhenExistingAttemptAlreadyExpired_ReturnsIdempotencyConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, _, cart) = await SeedPrerequisitesAsync(context);
        var authMock = CreateCurrentUserMock(user.Id);

        var idempotencyKey = "stripe-already-expired-key-12345";
        var order = new Order(
            orderNumber: "ORD-EXISTING-2",
            userId: user.Id,
            checkoutIdempotencyKey: idempotencyKey,
            customerName: user.FullName,
            customerEmail: user.Email,
            customerPhone: user.PhoneNumber!,
            shippingAddress: "123 Street",
            paymentMethod: PaymentMethod.Stripe,
            notes: null
        );
        order.AddItem(Guid.NewGuid(), "Item", "SKU", 100_000m, 1);
        var attemptRes = order.CreatePaymentAttempt("key", DateTime.UtcNow.AddMinutes(30));
        var expireRes = order.ExpirePaymentAttempt(attemptRes.Value.Id, attemptRes.Value.ExpiresAt);
        expireRes.IsSuccess.Should().BeTrue();

        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var gatewayMock = new Mock<IStripePaymentGateway>();
        gatewayMock.Setup(g => g.IsEnabled).Returns(true);

        var handler = new CheckoutCartCommandHandler(context, authMock.Object, gatewayMock.Object);

        var command = new CheckoutCartCommand(
            AddressId: address.Id,
            ExpectedCartVersion: cart.Version,
            PaymentMethod: PaymentMethod.Stripe,
            Notes: null,
            IdempotencyKey: idempotencyKey
        );

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.IdempotencyConflict");
    }
}
