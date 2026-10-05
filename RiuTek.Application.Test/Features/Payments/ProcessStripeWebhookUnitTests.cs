using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Payments.Commands;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using Xunit;

namespace RiuTek.Application.Test.Features.Payments;

public class ProcessStripeWebhookUnitTests
{
    private static async Task<(Order Order, PaymentAttempt Attempt, Product Product)> SeedOrderAndAttemptAsync(
        TestApplicationDbContext context,
        PaymentMethod method = PaymentMethod.Stripe,
        decimal amount = 200_000m,
        int stockQuantity = 48,
        int itemQuantity = 2,
        string? providerReference = "cs_test_session_123")
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(
            email: $"webhook_user_{suffix}@riutek.test",
            passwordHash: "dummy_hash",
            fullName: $"Webhook User {suffix}",
            role: UserRole.Customer,
            phoneNumber: "0901234567"
        );
        context.Users.Add(user);

        var category = new Category($"Cat {suffix}", $"cat-{suffix}", ComponentType.Accessory);
        context.Categories.Add(category);

        var product = new Product(
            categoryId: category.Id,
            name: $"Product {suffix}",
            slug: $"prod-{suffix}",
            sku: $"SKU-{suffix}",
            brand: "TestBrand",
            price: 100_000m,
            stockQuantity: stockQuantity,
            imageUrl: "https://riutek.test/img.png",
            componentType: ComponentType.Accessory,
            specifications: new AccessorySpecification { Details = "Specs" }
        );
        context.Products.Add(product);

        var order = new Order(
            orderNumber: $"ORD-WH-{suffix}",
            userId: user.Id,
            checkoutIdempotencyKey: $"key-{suffix}-12345",
            customerName: user.FullName,
            customerEmail: user.Email,
            customerPhone: "0901234567",
            shippingAddress: "123 Test Street",
            paymentMethod: method,
            notes: null
        );
        order.AddItem(product.Id, product.Name, product.Sku, product.Price, itemQuantity);

        var attemptRes = order.CreatePaymentAttempt($"stripe-{order.Id:N}", DateTime.UtcNow.AddMinutes(30));
        var attempt = attemptRes.Value;
        if (!string.IsNullOrWhiteSpace(providerReference))
        {
            attempt.SetProviderReference(providerReference);
        }

        context.Orders.Add(order);
        await context.SaveChangesAsync();

        return (order, attempt, product);
    }

    [Fact]
    public async Task Handle_WhenInvalidSignature_ReturnsFailure()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var gatewayMock = new Mock<IStripePaymentGateway>();
        gatewayMock.Setup(g => g.ParseAndVerifyWebhook(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Result.Failure<StripeWebhookEvent>(Error.Validation(
                "Webhook.InvalidSignature",
                "Stripe webhook signature verification failed.")));

        var handler = new ProcessStripeWebhookCommandHandler(context, gatewayMock.Object);

        var result = await handler.Handle(new ProcessStripeWebhookCommand("{}", "bad_sig"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Webhook.InvalidSignature");
    }

    [Fact]
    public async Task Handle_WhenUnknownEvent_ReturnsSuccessWithoutMutation()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var gatewayMock = new Mock<IStripePaymentGateway>();
        gatewayMock.Setup(g => g.ParseAndVerifyWebhook(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Result.Success(new StripeWebhookEvent(
                EventType: StripeWebhookEventType.Unknown,
                EventId: "evt_unknown",
                SessionId: null,
                OrderIdString: null,
                PaymentAttemptIdString: null,
                AmountTotal: null,
                Currency: null,
                PaymentStatus: null
            )));

        var handler = new ProcessStripeWebhookCommandHandler(context, gatewayMock.Object);

        var result = await handler.Handle(new ProcessStripeWebhookCommand("{}", "sig"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(null, "valid-guid")]
    [InlineData("valid-guid", null)]
    [InlineData("not-a-guid", "valid-guid")]
    [InlineData("valid-guid", "not-a-guid")]
    public async Task Handle_WhenInvalidMetadata_ReturnsValidationError(string? orderIdStr, string? attemptIdStr)
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var validGuid = Guid.NewGuid().ToString();

        var gatewayMock = new Mock<IStripePaymentGateway>();
        gatewayMock.Setup(g => g.ParseAndVerifyWebhook(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Result.Success(new StripeWebhookEvent(
                EventType: StripeWebhookEventType.CheckoutSessionCompleted,
                EventId: "evt_1",
                SessionId: "cs_1",
                OrderIdString: orderIdStr == "valid-guid" ? validGuid : orderIdStr,
                PaymentAttemptIdString: attemptIdStr == "valid-guid" ? validGuid : attemptIdStr,
                AmountTotal: 100_000m,
                Currency: "vnd",
                PaymentStatus: "paid"
            )));

        var handler = new ProcessStripeWebhookCommandHandler(context, gatewayMock.Object);

        var result = await handler.Handle(new ProcessStripeWebhookCommand("{}", "sig"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Webhook.InvalidMetadata");
    }

    [Fact]
    public async Task Handle_CheckoutSessionCompleted_HappyPath_UpdatesAttemptAndOrder()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (order, attempt, _) = await SeedOrderAndAttemptAsync(context);

        var gatewayMock = new Mock<IStripePaymentGateway>();
        gatewayMock.Setup(g => g.ParseAndVerifyWebhook(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Result.Success(new StripeWebhookEvent(
                EventType: StripeWebhookEventType.CheckoutSessionCompleted,
                EventId: "evt_completed_1",
                SessionId: attempt.ProviderReference,
                OrderIdString: order.Id.ToString(),
                PaymentAttemptIdString: attempt.Id.ToString(),
                AmountTotal: attempt.Amount,
                Currency: "vnd",
                PaymentStatus: "paid"
            )));

        var handler = new ProcessStripeWebhookCommandHandler(context, gatewayMock.Object);

        var result = await handler.Handle(new ProcessStripeWebhookCommand("{}", "sig"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var updatedOrder = await context.Orders.Include(o => o.PaymentAttempts).FirstAsync(o => o.Id == order.Id);
        updatedOrder.Status.Should().Be(OrderStatus.Confirmed);
        updatedOrder.PaymentStatus.Should().Be(PaymentStatus.Completed);

        var updatedAttempt = updatedOrder.PaymentAttempts.First(p => p.Id == attempt.Id);
        updatedAttempt.Status.Should().Be(PaymentAttemptStatus.Succeeded);
    }

    [Fact]
    public async Task Handle_CheckoutSessionCompleted_Duplicate_IsIdempotent()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (order, attempt, _) = await SeedOrderAndAttemptAsync(context);

        // Pre-mark attempt as Succeeded and order as Completed
        order.MarkPaymentSucceeded(attempt.Id, DateTime.UtcNow);
        await context.SaveChangesAsync();

        var gatewayMock = new Mock<IStripePaymentGateway>();
        gatewayMock.Setup(g => g.ParseAndVerifyWebhook(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Result.Success(new StripeWebhookEvent(
                EventType: StripeWebhookEventType.CheckoutSessionCompleted,
                EventId: "evt_completed_dup",
                SessionId: attempt.ProviderReference,
                OrderIdString: order.Id.ToString(),
                PaymentAttemptIdString: attempt.Id.ToString(),
                AmountTotal: attempt.Amount,
                Currency: "vnd",
                PaymentStatus: "paid"
            )));

        var handler = new ProcessStripeWebhookCommandHandler(context, gatewayMock.Object);

        var result = await handler.Handle(new ProcessStripeWebhookCommand("{}", "sig"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_CheckoutSessionCompleted_WhenAmountMismatch_ReturnsConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (order, attempt, _) = await SeedOrderAndAttemptAsync(context, amount: 200_000m);

        var gatewayMock = new Mock<IStripePaymentGateway>();
        gatewayMock.Setup(g => g.ParseAndVerifyWebhook(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Result.Success(new StripeWebhookEvent(
                EventType: StripeWebhookEventType.CheckoutSessionCompleted,
                EventId: "evt_amount_mismatch",
                SessionId: attempt.ProviderReference,
                OrderIdString: order.Id.ToString(),
                PaymentAttemptIdString: attempt.Id.ToString(),
                AmountTotal: 150_000m, // mismatch
                Currency: "vnd",
                PaymentStatus: "paid"
            )));

        var handler = new ProcessStripeWebhookCommandHandler(context, gatewayMock.Object);

        var result = await handler.Handle(new ProcessStripeWebhookCommand("{}", "sig"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Payment.AmountMismatch");

        var dbOrder = await context.Orders.FirstAsync(o => o.Id == order.Id);
        dbOrder.Status.Should().Be(OrderStatus.PendingPayment);
    }

    [Fact]
    public async Task Handle_CheckoutSessionCompleted_WhenCurrencyMismatch_ReturnsConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (order, attempt, _) = await SeedOrderAndAttemptAsync(context);

        var gatewayMock = new Mock<IStripePaymentGateway>();
        gatewayMock.Setup(g => g.ParseAndVerifyWebhook(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Result.Success(new StripeWebhookEvent(
                EventType: StripeWebhookEventType.CheckoutSessionCompleted,
                EventId: "evt_curr_mismatch",
                SessionId: attempt.ProviderReference,
                OrderIdString: order.Id.ToString(),
                PaymentAttemptIdString: attempt.Id.ToString(),
                AmountTotal: attempt.Amount,
                Currency: "usd", // mismatch
                PaymentStatus: "paid"
            )));

        var handler = new ProcessStripeWebhookCommandHandler(context, gatewayMock.Object);

        var result = await handler.Handle(new ProcessStripeWebhookCommand("{}", "sig"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Payment.CurrencyMismatch");
    }

    [Fact]
    public async Task Handle_CheckoutSessionCompleted_WhenProviderReferenceMismatch_ReturnsConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (order, attempt, _) = await SeedOrderAndAttemptAsync(context, providerReference: "cs_real_ref");

        var gatewayMock = new Mock<IStripePaymentGateway>();
        gatewayMock.Setup(g => g.ParseAndVerifyWebhook(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Result.Success(new StripeWebhookEvent(
                EventType: StripeWebhookEventType.CheckoutSessionCompleted,
                EventId: "evt_ref_mismatch",
                SessionId: "cs_forged_ref", // mismatch
                OrderIdString: order.Id.ToString(),
                PaymentAttemptIdString: attempt.Id.ToString(),
                AmountTotal: attempt.Amount,
                Currency: "vnd",
                PaymentStatus: "paid"
            )));

        var handler = new ProcessStripeWebhookCommandHandler(context, gatewayMock.Object);

        var result = await handler.Handle(new ProcessStripeWebhookCommand("{}", "sig"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Payment.ProviderReferenceMismatch");
    }

    [Fact]
    public async Task Handle_CheckoutSessionExpired_HappyPath_CancelsOrderAndRestoresStock()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (order, attempt, product) = await SeedOrderAndAttemptAsync(context, stockQuantity: 48, itemQuantity: 2);

        var gatewayMock = new Mock<IStripePaymentGateway>();
        gatewayMock.Setup(g => g.ParseAndVerifyWebhook(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Result.Success(new StripeWebhookEvent(
                EventType: StripeWebhookEventType.CheckoutSessionExpired,
                EventId: "evt_expired_1",
                SessionId: attempt.ProviderReference,
                OrderIdString: order.Id.ToString(),
                PaymentAttemptIdString: attempt.Id.ToString(),
                AmountTotal: attempt.Amount,
                Currency: "vnd",
                PaymentStatus: "unpaid"
            )));

        var handler = new ProcessStripeWebhookCommandHandler(context, gatewayMock.Object);

        var result = await handler.Handle(new ProcessStripeWebhookCommand("{}", "sig"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var updatedOrder = await context.Orders.Include(o => o.PaymentAttempts).FirstAsync(o => o.Id == order.Id);
        updatedOrder.Status.Should().Be(OrderStatus.Cancelled);
        updatedOrder.PaymentStatus.Should().Be(PaymentStatus.Failed);

        var updatedAttempt = updatedOrder.PaymentAttempts.First(p => p.Id == attempt.Id);
        updatedAttempt.Status.Should().Be(PaymentAttemptStatus.Expired);

        // Stock restored from 48 to 50
        var updatedProduct = await context.Products.FindAsync(product.Id);
        updatedProduct!.StockQuantity.Should().Be(50);
    }

    [Fact]
    public async Task Handle_CheckoutSessionExpired_Duplicate_DoesNotRestoreStockTwice()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (order, attempt, product) = await SeedOrderAndAttemptAsync(context, stockQuantity: 48, itemQuantity: 2);

        var gatewayMock = new Mock<IStripePaymentGateway>();
        gatewayMock.Setup(g => g.ParseAndVerifyWebhook(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Result.Success(new StripeWebhookEvent(
                EventType: StripeWebhookEventType.CheckoutSessionExpired,
                EventId: "evt_expired_dup",
                SessionId: attempt.ProviderReference,
                OrderIdString: order.Id.ToString(),
                PaymentAttemptIdString: attempt.Id.ToString(),
                AmountTotal: attempt.Amount,
                Currency: "vnd",
                PaymentStatus: "unpaid"
            )));

        var handler = new ProcessStripeWebhookCommandHandler(context, gatewayMock.Object);

        // First expiration
        var res1 = await handler.Handle(new ProcessStripeWebhookCommand("{}", "sig"), CancellationToken.None);
        res1.IsSuccess.Should().BeTrue();

        var prodAfter1 = await context.Products.FindAsync(product.Id);
        prodAfter1!.StockQuantity.Should().Be(50);

        // Second duplicate expiration event
        var res2 = await handler.Handle(new ProcessStripeWebhookCommand("{}", "sig"), CancellationToken.None);
        res2.IsSuccess.Should().BeTrue();

        var prodAfter2 = await context.Products.FindAsync(product.Id);
        prodAfter2!.StockQuantity.Should().Be(50); // MUST NOT become 52!
    }

    [Fact]
    public async Task Handle_CheckoutSessionExpired_WhenAttemptAlreadySucceeded_DoesNotCancelOrRestoreStock()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (order, attempt, product) = await SeedOrderAndAttemptAsync(context, stockQuantity: 48, itemQuantity: 2);

        order.MarkPaymentSucceeded(attempt.Id, DateTime.UtcNow);
        await context.SaveChangesAsync();

        var gatewayMock = new Mock<IStripePaymentGateway>();
        gatewayMock.Setup(g => g.ParseAndVerifyWebhook(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Result.Success(new StripeWebhookEvent(
                EventType: StripeWebhookEventType.CheckoutSessionExpired,
                EventId: "evt_expired_after_success",
                SessionId: attempt.ProviderReference,
                OrderIdString: order.Id.ToString(),
                PaymentAttemptIdString: attempt.Id.ToString(),
                AmountTotal: attempt.Amount,
                Currency: "vnd",
                PaymentStatus: "unpaid"
            )));

        var handler = new ProcessStripeWebhookCommandHandler(context, gatewayMock.Object);

        var result = await handler.Handle(new ProcessStripeWebhookCommand("{}", "sig"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var dbOrder = await context.Orders.Include(o => o.PaymentAttempts).FirstAsync(o => o.Id == order.Id);
        dbOrder.Status.Should().Be(OrderStatus.Confirmed);
        dbOrder.PaymentStatus.Should().Be(PaymentStatus.Completed);

        var dbProd = await context.Products.FindAsync(product.Id);
        dbProd!.StockQuantity.Should().Be(48); // untouched
    }

    [Fact]
    public async Task Handle_CheckoutSessionCompleted_WhenAttemptAlreadyExpired_ReturnsConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (order, attempt, _) = await SeedOrderAndAttemptAsync(context);

        var expireRes = order.ExpirePaymentAttempt(attempt.Id, attempt.ExpiresAt);
        expireRes.IsSuccess.Should().BeTrue();
        await context.SaveChangesAsync();

        var gatewayMock = new Mock<IStripePaymentGateway>();
        gatewayMock.Setup(g => g.ParseAndVerifyWebhook(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Result.Success(new StripeWebhookEvent(
                EventType: StripeWebhookEventType.CheckoutSessionCompleted,
                EventId: "evt_completed_after_expiry",
                SessionId: attempt.ProviderReference,
                OrderIdString: order.Id.ToString(),
                PaymentAttemptIdString: attempt.Id.ToString(),
                AmountTotal: attempt.Amount,
                Currency: "vnd",
                PaymentStatus: "paid"
            )));

        var handler = new ProcessStripeWebhookCommandHandler(context, gatewayMock.Object);

        var result = await handler.Handle(new ProcessStripeWebhookCommand("{}", "sig"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Payment.InvalidState");

        var dbOrder = await context.Orders.FirstAsync(o => o.Id == order.Id);
        dbOrder.Status.Should().Be(OrderStatus.Cancelled);
    }
}
