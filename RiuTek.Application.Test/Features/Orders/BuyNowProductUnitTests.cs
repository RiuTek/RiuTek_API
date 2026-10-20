using FluentAssertions;
using FluentValidation.TestHelper;
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

public class BuyNowProductUnitTests
{
    private static async Task<(User User, UserAddress Address, Product Product, Cart Cart)> SeedPrerequisitesAsync(
        TestApplicationDbContext context,
        bool isUserActive = true,
        bool isProductActive = true,
        int stockQuantity = 50,
        decimal price = 500_000m,
        int initialCartQuantity = 2)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(
            email: $"buynow_{suffix}@riutek.test",
            passwordHash: "dummy_hash",
            fullName: $"BuyNow User {suffix}",
            role: UserRole.Customer,
            phoneNumber: "0901234567"
        )
        {
            IsActive = isUserActive
        };
        context.Users.Add(user);

        var address = new UserAddress(
            userId: user.Id,
            receiverName: $"Receiver {suffix}",
            phoneNumber: "0909999999",
            addressLine: "123 BuyNow Street",
            ward: "Ward 1",
            district: "District 1",
            city: "Ho Chi Minh City",
            isDefault: true
        );
        context.UserAddresses.Add(address);

        var category = new Category(
            name: $"Category {suffix}",
            slug: $"cat-{suffix}",
            componentType: ComponentType.Cpu
        );
        context.Categories.Add(category);

        var product = new Product(
            categoryId: category.Id,
            name: $"CPU {suffix}",
            slug: $"cpu-{suffix}",
            sku: $"SKU-CPU-{suffix}",
            brand: "Intel",
            price: price,
            stockQuantity: stockQuantity,
            imageUrl: "https://riutek.test/cpu.png",
            componentType: ComponentType.Cpu,
            specifications: new CpuSpecification { Socket = CpuSocket.LGA1700 }
        )
        {
            IsActive = isProductActive
        };
        context.Products.Add(product);

        // Pre-existing cart with a different accessory product to prove cart isolation
        var accessoryCategory = new Category($"Accessory {suffix}", $"acc-{suffix}", ComponentType.Accessory);
        context.Categories.Add(accessoryCategory);
        var cartProduct = new Product(
            categoryId: accessoryCategory.Id,
            name: $"Accessory {suffix}",
            slug: $"acc-{suffix}",
            sku: $"SKU-ACC-{suffix}",
            brand: "Razer",
            price: 150_000m,
            stockQuantity: 100,
            imageUrl: "https://riutek.test/acc.png",
            componentType: ComponentType.Accessory,
            specifications: new AccessorySpecification()
        );
        context.Products.Add(cartProduct);

        var cart = new Cart(user.Id);
        cart.AddItem(cartProduct.Id, initialCartQuantity);
        context.Carts.Add(cart);

        await context.SaveChangesAsync();
        return (user, address, product, cart);
    }

    private static Mock<ICurrentUserService> CreateCurrentUserMock(Guid? userId, bool isAuthenticated = true)
    {
        var mock = new Mock<ICurrentUserService>();
        mock.Setup(x => x.IsAuthenticated).Returns(isAuthenticated);
        mock.Setup(x => x.UserId).Returns(userId);
        return mock;
    }

    #region Validator Tests

    [Fact]
    public void Validator_WhenProductIdEmpty_HasValidationError()
    {
        var validator = new BuyNowProductCommandValidator();
        var command = new BuyNowProductCommand(
            ProductId: Guid.Empty,
            AddressId: Guid.NewGuid(),
            Quantity: 1,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: "1234567890123456"
        );

        var result = validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.ProductId);
    }

    [Fact]
    public void Validator_WhenAddressIdEmpty_HasValidationError()
    {
        var validator = new BuyNowProductCommandValidator();
        var command = new BuyNowProductCommand(
            ProductId: Guid.NewGuid(),
            AddressId: Guid.Empty,
            Quantity: 1,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: "1234567890123456"
        );

        var result = validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.AddressId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100)]
    public void Validator_WhenQuantityOutOfRange_HasValidationError(int quantity)
    {
        var validator = new BuyNowProductCommandValidator();
        var command = new BuyNowProductCommand(
            ProductId: Guid.NewGuid(),
            AddressId: Guid.NewGuid(),
            Quantity: quantity,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: "1234567890123456"
        );

        var result = validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Quantity);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("short")]
    [InlineData("123456789012345")] // 15 chars (< 16)
    public void Validator_WhenIdempotencyKeyTooShortOrEmpty_HasValidationError(string key)
    {
        var validator = new BuyNowProductCommandValidator();
        var command = new BuyNowProductCommand(
            ProductId: Guid.NewGuid(),
            AddressId: Guid.NewGuid(),
            Quantity: 1,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: key
        );

        var result = validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.IdempotencyKey);
    }

    [Fact]
    public void Validator_WhenNotesExceed1000Chars_HasValidationError()
    {
        var validator = new BuyNowProductCommandValidator();
        var command = new BuyNowProductCommand(
            ProductId: Guid.NewGuid(),
            AddressId: Guid.NewGuid(),
            Quantity: 1,
            PaymentMethod: PaymentMethod.COD,
            Notes: new string('a', 1001),
            IdempotencyKey: "1234567890123456"
        );

        var result = validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Notes);
    }

    [Fact]
    public void Validator_WhenValidInput_PassesValidation()
    {
        var validator = new BuyNowProductCommandValidator();
        var command = new BuyNowProductCommand(
            ProductId: Guid.NewGuid(),
            AddressId: Guid.NewGuid(),
            Quantity: 2,
            PaymentMethod: PaymentMethod.COD,
            Notes: "Valid note",
            IdempotencyKey: "1234567890123456"
        );

        var result = validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    #endregion

    #region Handler Auth & Source Tests

    [Fact]
    public async Task Handle_WhenAnonymous_ReturnsUnauthorized()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (_, address, product, _) = await SeedPrerequisitesAsync(context);
        var authMock = CreateCurrentUserMock(null, isAuthenticated: false);

        var processor = new CheckoutOrderProcessor(context);
        var handler = new BuyNowProductCommandHandler(context, authMock.Object, processor);

        var command = new BuyNowProductCommand(product.Id, address.Id, 1, PaymentMethod.COD, null, "1234567890123456");
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.Unauthorized");
    }

    [Fact]
    public async Task Handle_WhenUserInactive_ReturnsForbidden()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, _) = await SeedPrerequisitesAsync(context, isUserActive: false);
        var authMock = CreateCurrentUserMock(user.Id);

        var processor = new CheckoutOrderProcessor(context);
        var handler = new BuyNowProductCommandHandler(context, authMock.Object, processor);

        var command = new BuyNowProductCommand(product.Id, address.Id, 1, PaymentMethod.COD, null, "1234567890123456");
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.AccountInactive");
    }

    [Fact]
    public async Task Handle_WhenPaymentMethodDisabled_ReturnsValidationFailureBeforeReplayOrLookup()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, _) = await SeedPrerequisitesAsync(context);
        var authMock = CreateCurrentUserMock(user.Id);

        var gatewayMock = new Mock<IStripePaymentGateway>();
        gatewayMock.Setup(g => g.IsEnabled).Returns(false);

        var processor = new CheckoutOrderProcessor(context, gatewayMock.Object);
        var handler = new BuyNowProductCommandHandler(context, authMock.Object, processor);

        var command = new BuyNowProductCommand(product.Id, address.Id, 1, PaymentMethod.Stripe, null, "1234567890123456");
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.PaymentMethodNotAvailable");
    }

    [Fact]
    public async Task Handle_WhenAddressBelongsToAnotherUser_ReturnsNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (userA, _, product, _) = await SeedPrerequisitesAsync(context);
        var (userB, addressB, _, _) = await SeedPrerequisitesAsync(context);

        var authMock = CreateCurrentUserMock(userA.Id);
        var processor = new CheckoutOrderProcessor(context);
        var handler = new BuyNowProductCommandHandler(context, authMock.Object, processor);

        // User A tries to use User B's address
        var command = new BuyNowProductCommand(product.Id, addressB.Id, 1, PaymentMethod.COD, null, "1234567890123456");
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.AddressNotFound");
    }

    [Fact]
    public async Task Handle_WhenProductNotFound_ReturnsNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, _, _) = await SeedPrerequisitesAsync(context);
        var authMock = CreateCurrentUserMock(user.Id);

        var processor = new CheckoutOrderProcessor(context);
        var handler = new BuyNowProductCommandHandler(context, authMock.Object, processor);

        var command = new BuyNowProductCommand(Guid.NewGuid(), address.Id, 1, PaymentMethod.COD, null, "1234567890123456");
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.ProductNotFound");
    }

    [Fact]
    public async Task Handle_WhenProductInactive_ReturnsConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, _) = await SeedPrerequisitesAsync(context, isProductActive: false);
        var authMock = CreateCurrentUserMock(user.Id);

        var processor = new CheckoutOrderProcessor(context);
        var handler = new BuyNowProductCommandHandler(context, authMock.Object, processor);

        var command = new BuyNowProductCommand(product.Id, address.Id, 1, PaymentMethod.COD, null, "1234567890123456");
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.ProductInactive");
    }

    [Fact]
    public async Task Handle_WhenProductPriceInvalid_ReturnsConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, _) = await SeedPrerequisitesAsync(context, price: 0);
        var authMock = CreateCurrentUserMock(user.Id);

        var processor = new CheckoutOrderProcessor(context);
        var handler = new BuyNowProductCommandHandler(context, authMock.Object, processor);

        var command = new BuyNowProductCommand(product.Id, address.Id, 1, PaymentMethod.COD, null, "1234567890123456");
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.InvalidPrice");
    }

    [Fact]
    public async Task Handle_WhenInsufficientStock_ReturnsConflict_AndDoesNotMutateCart()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, cart) = await SeedPrerequisitesAsync(context, stockQuantity: 2);
        var initialCartVersion = cart.Version;
        var initialCartItemCount = cart.Items.Count;

        var authMock = CreateCurrentUserMock(user.Id);
        var processor = new CheckoutOrderProcessor(context);
        var handler = new BuyNowProductCommandHandler(context, authMock.Object, processor);

        var command = new BuyNowProductCommand(product.Id, address.Id, 5, PaymentMethod.COD, null, "1234567890123456");
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.InsufficientStock");

        // Verify cart completely unmutated
        var dbCart = await context.Carts.Include(c => c.Items).FirstAsync(c => c.Id == cart.Id);
        dbCart.Version.Should().Be(initialCartVersion);
        dbCart.Items.Should().HaveCount(initialCartItemCount);
    }

    #endregion

    #region Success & Cart Isolation Tests

    [Fact]
    public async Task Handle_WhenValidCOD_CreatesOrder_DecrementsStock_RecordsStockMovement_AndLeavesCartUntouched()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, cart) = await SeedPrerequisitesAsync(context, stockQuantity: 20);
        var initialCartVersion = cart.Version;
        var initialCartItem = cart.Items.First();
        var initialCartItemId = initialCartItem.ProductId;
        var initialCartItemQty = initialCartItem.Quantity;

        var authMock = CreateCurrentUserMock(user.Id);
        var processor = new CheckoutOrderProcessor(context);
        var handler = new BuyNowProductCommandHandler(context, authMock.Object, processor);

        var idempotencyKey = "123456789012345678";
        var command = new BuyNowProductCommand(
            ProductId: product.Id,
            AddressId: address.Id,
            Quantity: 3,
            PaymentMethod: PaymentMethod.COD,
            Notes: "Fragile item",
            IdempotencyKey: idempotencyKey
        );

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var orderDto = result.Value;
        orderDto.Status.Should().Be(OrderStatus.Confirmed);
        orderDto.PaymentMethod.Should().Be(PaymentMethod.COD);
        orderDto.PaymentStatus.Should().Be(PaymentStatus.Pending);
        orderDto.Items.Should().HaveCount(1);
        orderDto.Items.First().ProductId.Should().Be(product.Id);
        orderDto.Items.First().Quantity.Should().Be(3);
        orderDto.Items.First().UnitPrice.Should().Be(product.Price);
        orderDto.Notes.Should().Be("Fragile item");

        // Verify Order persisted in DB
        var dbOrder = await context.Orders.Include(o => o.Items).FirstAsync(o => o.Id == orderDto.Id);
        dbOrder.CheckoutIdempotencyKey.Should().Be(idempotencyKey);
        dbOrder.Items.First().PCBuildId.Should().BeNull();

        // Verify Stock decremented
        var dbProduct = await context.Products.FirstAsync(p => p.Id == product.Id);
        dbProduct.StockQuantity.Should().Be(17);

        // Verify StockMovement recorded
        var movement = await context.StockMovements.FirstOrDefaultAsync(m => m.ProductId == product.Id);
        movement.Should().NotBeNull();
        movement!.QuantityDelta.Should().Be(-3);
        movement.Reason.Should().Be(StockMovementReason.Checkout);
        movement.ActorUserId.Should().Be(user.Id);
        movement.OrderId.Should().Be(orderDto.Id);

        // CRITICAL INVARIANT: Verify Cart was completely untouched
        var dbCart = await context.Carts.Include(c => c.Items).FirstAsync(c => c.Id == cart.Id);
        dbCart.Version.Should().Be(initialCartVersion);
        dbCart.Items.Should().HaveCount(1);
        dbCart.Items.First().ProductId.Should().Be(initialCartItemId);
        dbCart.Items.First().Quantity.Should().Be(initialCartItemQty);
    }

    [Fact]
    public async Task Handle_WhenValidStripe_CreatesOrderAndAttempt_InitiatesSession_AndLeavesCartUntouched()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, cart) = await SeedPrerequisitesAsync(context);
        var initialCartVersion = cart.Version;

        var authMock = CreateCurrentUserMock(user.Id);
        var gatewayMock = new Mock<IStripePaymentGateway>();
        gatewayMock.Setup(g => g.IsEnabled).Returns(true);
        gatewayMock.Setup(g => g.CheckoutSessionLifetime).Returns(TimeSpan.FromMinutes(30));
        gatewayMock.Setup(g => g.EnsureCheckoutSessionAsync(
            It.IsAny<CreateStripeCheckoutSessionRequest>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new StripeCheckoutSessionResult(
                SessionId: "cs_buynow_123",
                Url: "https://checkout.stripe.com/pay/cs_buynow_123",
                ExpiresAt: DateTime.UtcNow.AddMinutes(30)
            )));

        var processor = new CheckoutOrderProcessor(context, gatewayMock.Object);
        var handler = new BuyNowProductCommandHandler(context, authMock.Object, processor);

        var command = new BuyNowProductCommand(
            ProductId: product.Id,
            AddressId: address.Id,
            Quantity: 1,
            PaymentMethod: PaymentMethod.Stripe,
            Notes: null,
            IdempotencyKey: "123456789012345678"
        );

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(OrderStatus.PendingPayment);
        result.Value.PaymentAction.Should().NotBeNull();
        result.Value.PaymentAction!.Type.Should().Be("Redirect");
        result.Value.PaymentAction.Url.Should().Be("https://checkout.stripe.com/pay/cs_buynow_123");

        // Verify cart untouched
        var dbCart = await context.Carts.Include(c => c.Items).FirstAsync(c => c.Id == cart.Id);
        dbCart.Version.Should().Be(initialCartVersion);
        dbCart.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_WhenIdempotencyKeyReplayed_ReplaysExistingOrderBeforeAddressOrProductLookup()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, _) = await SeedPrerequisitesAsync(context, stockQuantity: 20);
        var authMock = CreateCurrentUserMock(user.Id);

        var processor = new CheckoutOrderProcessor(context);
        var handler = new BuyNowProductCommandHandler(context, authMock.Object, processor);

        var idempotencyKey = "123456789012345678";
        var command1 = new BuyNowProductCommand(product.Id, address.Id, 2, PaymentMethod.COD, null, idempotencyKey);

        // First call
        var result1 = await handler.Handle(command1, CancellationToken.None);
        result1.IsSuccess.Should().BeTrue();

        // Second call with same key, but non-existent addressId and non-existent productId
        var command2 = new BuyNowProductCommand(Guid.NewGuid(), Guid.NewGuid(), 2, PaymentMethod.COD, null, idempotencyKey);
        var result2 = await handler.Handle(command2, CancellationToken.None);

        result2.IsSuccess.Should().BeTrue();
        result2.Value.Id.Should().Be(result1.Value.Id);
        result2.Value.OrderNumber.Should().Be(result1.Value.OrderNumber);

        // Verify stock deducted only once
        var dbProduct = await context.Products.FirstAsync(p => p.Id == product.Id);
        dbProduct.StockQuantity.Should().Be(18);

        // Verify exactly one order exists
        (await context.Orders.CountAsync()).Should().Be(1);
    }

    #endregion
}
