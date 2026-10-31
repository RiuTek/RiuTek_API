using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Orders.Commands;
using RiuTek.Application.Features.Orders.Common;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using Xunit;

namespace RiuTek.Application.Test.Features.Orders;

public class CheckoutOrderProcessorUnitTests
{
    private static async Task<(User User, UserAddress Address, Product Product, Cart Cart)> SeedPrerequisitesAsync(
        TestApplicationDbContext context,
        bool isProductActive = true,
        int stockQuantity = 50,
        decimal price = 200_000m)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(
            email: $"user_{suffix}@riutek.test",
            passwordHash: "dummy_hash",
            fullName: $"Test User {suffix}",
            role: UserRole.Customer,
            phoneNumber: "0901234567"
        );
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
            IsActive = isProductActive
        };
        context.Products.Add(product);

        var cart = new Cart(user.Id);
        cart.AddItem(product.Id, 2);
        context.Carts.Add(cart);

        await context.SaveChangesAsync();
        return (user, address, product, cart);
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenLinesEmpty_ReturnsValidationFailure()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, _, _) = await SeedPrerequisitesAsync(context);

        var processor = new CheckoutOrderProcessor(context);
        var input = new PlaceCheckoutOrderInput(
            User: user,
            Address: address,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: "test-idempotency-key",
            Lines: [],
            SourceCart: null
        );

        var result = await processor.PlaceOrderAsync(input);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.EmptyLines");
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenDuplicateProductsInLines_ReturnsValidationFailure()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, _) = await SeedPrerequisitesAsync(context);

        var processor = new CheckoutOrderProcessor(context);
        var input = new PlaceCheckoutOrderInput(
            User: user,
            Address: address,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: "test-idempotency-key",
            Lines:
            [
                new CheckoutOrderLine(product, 1),
                new CheckoutOrderLine(product, 2)
            ],
            SourceCart: null
        );

        var result = await processor.PlaceOrderAsync(input);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.DuplicateProduct");
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenProductInactive_ReturnsConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, _) = await SeedPrerequisitesAsync(context, isProductActive: false);

        var processor = new CheckoutOrderProcessor(context);
        var input = new PlaceCheckoutOrderInput(
            User: user,
            Address: address,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: "test-idempotency-key",
            Lines: [new CheckoutOrderLine(product, 1)],
            SourceCart: null
        );

        var result = await processor.PlaceOrderAsync(input);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.ProductInactive");
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenProductPriceInvalid_ReturnsConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, _) = await SeedPrerequisitesAsync(context, price: 0);

        var processor = new CheckoutOrderProcessor(context);
        var input = new PlaceCheckoutOrderInput(
            User: user,
            Address: address,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: "test-idempotency-key",
            Lines: [new CheckoutOrderLine(product, 1)],
            SourceCart: null
        );

        var result = await processor.PlaceOrderAsync(input);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.InvalidPrice");
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenProductInsufficientStock_ReturnsConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, _) = await SeedPrerequisitesAsync(context, stockQuantity: 2);

        var processor = new CheckoutOrderProcessor(context);
        var input = new PlaceCheckoutOrderInput(
            User: user,
            Address: address,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: "test-idempotency-key",
            Lines: [new CheckoutOrderLine(product, 5)],
            SourceCart: null
        );

        var result = await processor.PlaceOrderAsync(input);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.InsufficientStock");
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenStripeDisabled_ReturnsPaymentMethodNotAvailable()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, _) = await SeedPrerequisitesAsync(context);

        var gatewayMock = new Mock<IStripePaymentGateway>();
        gatewayMock.Setup(g => g.IsEnabled).Returns(false);

        var processor = new CheckoutOrderProcessor(context, gatewayMock.Object);
        var input = new PlaceCheckoutOrderInput(
            User: user,
            Address: address,
            PaymentMethod: PaymentMethod.Stripe,
            Notes: null,
            IdempotencyKey: "test-idempotency-key",
            Lines: [new CheckoutOrderLine(product, 1)],
            SourceCart: null
        );

        var result = await processor.PlaceOrderAsync(input);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.PaymentMethodNotAvailable");
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenValidCOD_CreatesOrderWithCorrectSnapshots_DecrementsStock_RecordsMovement_AndClearsCart()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, cart) = await SeedPrerequisitesAsync(context, stockQuantity: 20);

        var processor = new CheckoutOrderProcessor(context);
        var input = new PlaceCheckoutOrderInput(
            User: user,
            Address: address,
            PaymentMethod: PaymentMethod.COD,
            Notes: "Leave at door",
            IdempotencyKey: "test-cod-key-12345",
            Lines: [new CheckoutOrderLine(product, 3, PCBuildId: null)],
            SourceCart: cart
        );

        var result = await processor.PlaceOrderAsync(input);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.Status.Should().Be(OrderStatus.Confirmed);
        dto.PaymentStatus.Should().Be(PaymentStatus.Pending);
        dto.PaymentMethod.Should().Be(PaymentMethod.COD);
        dto.Items.Should().HaveCount(1);
        dto.Items.First().Quantity.Should().Be(3);
        dto.Items.First().UnitPrice.Should().Be(product.Price);

        // Verify Cart cleared
        cart.Items.Should().BeEmpty();

        // Verify Stock decremented
        var productInDb = await context.Products.FirstAsync(p => p.Id == product.Id);
        productInDb.StockQuantity.Should().Be(17);

        // Verify StockMovement recorded
        var movement = await context.StockMovements.FirstOrDefaultAsync(m => m.ProductId == product.Id);
        movement.Should().NotBeNull();
        movement!.QuantityDelta.Should().Be(-3);
        movement.Reason.Should().Be(StockMovementReason.Checkout);
        movement.ActorUserId.Should().Be(user.Id);
        movement.OrderId.Should().Be(dto.Id);

        // Verify Order in DB has null PCBuildId
        var orderInDb = await context.Orders.Include(o => o.Items).FirstAsync(o => o.Id == dto.Id);
        orderInDb.Items.First().PCBuildId.Should().BeNull();
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenSourceCartIsNull_SucceedsWithoutError()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, _) = await SeedPrerequisitesAsync(context);

        var processor = new CheckoutOrderProcessor(context);
        var input = new PlaceCheckoutOrderInput(
            User: user,
            Address: address,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: "test-nocart-key-12345",
            Lines: [new CheckoutOrderLine(product, 1)],
            SourceCart: null
        );

        var result = await processor.PlaceOrderAsync(input);

        result.IsSuccess.Should().BeTrue();
        (await context.Orders.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenPCBuildIdProvidedInLine_PropagatesToOrderItem()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, _) = await SeedPrerequisitesAsync(context);
        var pcBuildId = Guid.NewGuid();

        var processor = new CheckoutOrderProcessor(context);
        var input = new PlaceCheckoutOrderInput(
            User: user,
            Address: address,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: "test-build-key-12345",
            Lines: [new CheckoutOrderLine(product, 1, PCBuildId: pcBuildId)],
            SourceCart: null
        );

        var result = await processor.PlaceOrderAsync(input);

        result.IsSuccess.Should().BeTrue();
        var orderInDb = await context.Orders.Include(o => o.Items).FirstAsync(o => o.Id == result.Value.Id);
        orderInDb.Items.First().PCBuildId.Should().Be(pcBuildId);
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenValidStripe_CreatesOrderAndPendingAttempt_AndInitiatesSession()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, _) = await SeedPrerequisitesAsync(context);

        var gatewayMock = new Mock<IStripePaymentGateway>();
        gatewayMock.Setup(g => g.IsEnabled).Returns(true);
        gatewayMock.Setup(g => g.CheckoutSessionLifetime).Returns(TimeSpan.FromMinutes(30));
        gatewayMock.Setup(g => g.EnsureCheckoutSessionAsync(
            It.IsAny<CreateStripeCheckoutSessionRequest>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new StripeCheckoutSessionResult(
                SessionId: "cs_test_session_123",
                Url: "https://checkout.stripe.com/pay/cs_test_session_123",
                ExpiresAt: DateTime.UtcNow.AddMinutes(30)
            )));

        var processor = new CheckoutOrderProcessor(context, gatewayMock.Object);
        var input = new PlaceCheckoutOrderInput(
            User: user,
            Address: address,
            PaymentMethod: PaymentMethod.Stripe,
            Notes: null,
            IdempotencyKey: "test-stripe-key-12345",
            Lines: [new CheckoutOrderLine(product, 1)],
            SourceCart: null
        );

        var result = await processor.PlaceOrderAsync(input);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.Status.Should().Be(OrderStatus.PendingPayment);
        dto.PaymentMethod.Should().Be(PaymentMethod.Stripe);
        dto.PaymentAction.Should().NotBeNull();
        dto.PaymentAction!.Type.Should().Be("Redirect");
        dto.PaymentAction.Url.Should().Be("https://checkout.stripe.com/pay/cs_test_session_123");

        var orderInDb = await context.Orders.Include(o => o.PaymentAttempts).FirstAsync(o => o.Id == dto.Id);
        orderInDb.PaymentAttempts.Should().HaveCount(1);
        orderInDb.PaymentAttempts.First().ProviderReference.Should().Be("cs_test_session_123");
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenStripeGatewayThrowsOperationCanceledException_AndCallerNotCancelled_ReturnsUnavailable()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, _) = await SeedPrerequisitesAsync(context);

        var gatewayMock = new Mock<IStripePaymentGateway>();
        gatewayMock.Setup(g => g.IsEnabled).Returns(true);
        gatewayMock.Setup(g => g.CheckoutSessionLifetime).Returns(TimeSpan.FromMinutes(30));
        gatewayMock.Setup(g => g.EnsureCheckoutSessionAsync(
            It.IsAny<CreateStripeCheckoutSessionRequest>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException("Gateway timeout"));

        var processor = new CheckoutOrderProcessor(context, gatewayMock.Object);
        var input = new PlaceCheckoutOrderInput(
            User: user,
            Address: address,
            PaymentMethod: PaymentMethod.Stripe,
            Notes: null,
            IdempotencyKey: "test-stripe-timeout-key",
            Lines: [new CheckoutOrderLine(product, 1)],
            SourceCart: null
        );

        var result = await processor.PlaceOrderAsync(input, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Payment.GatewayUnavailable");
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenCallerCancellationTokenCancelled_RethrowsOperationCanceledException()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, _) = await SeedPrerequisitesAsync(context);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var gatewayMock = new Mock<IStripePaymentGateway>();
        gatewayMock.Setup(g => g.IsEnabled).Returns(true);
        gatewayMock.Setup(g => g.CheckoutSessionLifetime).Returns(TimeSpan.FromMinutes(30));
        gatewayMock.Setup(g => g.EnsureCheckoutSessionAsync(
            It.IsAny<CreateStripeCheckoutSessionRequest>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var processor = new CheckoutOrderProcessor(context, gatewayMock.Object);
        var input = new PlaceCheckoutOrderInput(
            User: user,
            Address: address,
            PaymentMethod: PaymentMethod.Stripe,
            Notes: null,
            IdempotencyKey: "test-caller-cancel-key",
            Lines: [new CheckoutOrderLine(product, 1)],
            SourceCart: null
        );

        Func<Task> act = async () => await processor.PlaceOrderAsync(input, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ReplayExistingOrderAsync_WhenCOD_ReturnsDto()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, _) = await SeedPrerequisitesAsync(context);

        var order = new Order("ORD-1", user.Id, "key-1", "Customer", "c@test.com", "090", "Address", PaymentMethod.COD);
        order.AddItem(product.Id, product.Name, product.Sku, product.Price, 1);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var processor = new CheckoutOrderProcessor(context);
        var result = await processor.ReplayExistingOrderAsync(order);

        result.IsSuccess.Should().BeTrue();
        result.Value.OrderNumber.Should().Be("ORD-1");
        result.Value.PaymentAction.Should().BeNull();
    }

    [Fact]
    public async Task ReplayExistingOrderAsync_WhenStripeSucceeded_ReturnsDtoWithNullAction()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, _) = await SeedPrerequisitesAsync(context);

        var order = new Order("ORD-2", user.Id, "key-2", "Customer", "c@test.com", "090", "Address", PaymentMethod.Stripe);
        order.AddItem(product.Id, product.Name, product.Sku, product.Price, 1);
        var attemptResult = order.CreatePaymentAttempt("prov-key", DateTime.UtcNow.AddMinutes(30));
        attemptResult.Value.SetProviderReference("cs_test_session");
        attemptResult.Value.MarkSucceeded(DateTime.UtcNow);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var gatewayMock = new Mock<IStripePaymentGateway>();
        gatewayMock.Setup(g => g.IsEnabled).Returns(true);

        var processor = new CheckoutOrderProcessor(context, gatewayMock.Object);
        var result = await processor.ReplayExistingOrderAsync(order);

        result.IsSuccess.Should().BeTrue();
        result.Value.PaymentAction.Should().BeNull();
    }

    [Fact]
    public async Task ReplayExistingOrderAsync_WhenStripeAttemptEnded_ReturnsIdempotencyConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, _) = await SeedPrerequisitesAsync(context);

        var order = new Order("ORD-3", user.Id, "key-3", "Customer", "c@test.com", "090", "Address", PaymentMethod.Stripe);
        order.AddItem(product.Id, product.Name, product.Sku, product.Price, 1);
        var expiresAt = DateTime.UtcNow.AddMinutes(30);
        var attemptResult = order.CreatePaymentAttempt("prov-key", expiresAt);
        attemptResult.Value.MarkExpired(expiresAt.AddSeconds(1));
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var gatewayMock = new Mock<IStripePaymentGateway>();
        gatewayMock.Setup(g => g.IsEnabled).Returns(true);

        var processor = new CheckoutOrderProcessor(context, gatewayMock.Object);
        var result = await processor.ReplayExistingOrderAsync(order);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.IdempotencyConflict");
    }

    [Fact]
    public void CheckoutCartCommandHandler_Constructor_InjectsCheckoutOrderProcessor()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var authMock = new Mock<ICurrentUserService>();
        var processor = new CheckoutOrderProcessor(context);

        var handler = new CheckoutCartCommandHandler(context, authMock.Object, processor);
        handler.Should().NotBeNull();
    }
}
