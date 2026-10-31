using FluentAssertions;
using FluentValidation.TestHelper;
using Microsoft.EntityFrameworkCore;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Orders.Commands;
using RiuTek.Application.Features.Orders.Queries;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using Xunit;

namespace RiuTek.Application.Test.Features.Orders;

public class SelectedCheckoutTests
{
    private static Mock<ICurrentUserService> CreateCurrentUserMock(Guid userId, bool isAuthenticated = true)
    {
        var mock = new Mock<ICurrentUserService>();
        mock.Setup(u => u.IsAuthenticated).Returns(isAuthenticated);
        mock.Setup(u => u.UserId).Returns(userId);
        mock.Setup(u => u.UserRole).Returns(UserRole.Customer.ToString());
        return mock;
    }

    private static async Task<(User User, UserAddress Address, Product ProductA, Product ProductB, Product ProductC, Cart Cart)> SeedMultiProductCartAsync(
        TestApplicationDbContext context,
        bool isProductBActive = true,
        int stockB = 50,
        decimal priceB = 200_000m)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(
            email: $"user_{suffix}@riutek.test",
            passwordHash: "dummy_hash",
            fullName: $"User {suffix}",
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
            addressLine: "123 Main Street",
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

        var productA = new Product(
            categoryId: category.Id,
            name: $"Product A {suffix}",
            slug: $"prod-a-{suffix}",
            sku: $"SKU-A-{suffix}",
            brand: "BrandA",
            price: 100_000m,
            stockQuantity: 20,
            imageUrl: "https://riutek.test/a.png",
            componentType: ComponentType.Accessory,
            specifications: new AccessorySpecification { Details = "A" }
        ) { IsActive = true };

        var productB = new Product(
            categoryId: category.Id,
            name: $"Product B {suffix}",
            slug: $"prod-b-{suffix}",
            sku: $"SKU-B-{suffix}",
            brand: "BrandB",
            price: priceB,
            stockQuantity: stockB,
            imageUrl: "https://riutek.test/b.png",
            componentType: ComponentType.Accessory,
            specifications: new AccessorySpecification { Details = "B" }
        ) { IsActive = isProductBActive };

        var productC = new Product(
            categoryId: category.Id,
            name: $"Product C {suffix}",
            slug: $"prod-c-{suffix}",
            sku: $"SKU-C-{suffix}",
            brand: "BrandC",
            price: 300_000m,
            stockQuantity: 15,
            imageUrl: "https://riutek.test/c.png",
            componentType: ComponentType.Accessory,
            specifications: new AccessorySpecification { Details = "C" }
        ) { IsActive = true };

        context.Products.AddRange(productA, productB, productC);

        var cart = new Cart(user.Id);
        cart.AddItem(productA.Id, 2);
        cart.AddItem(productB.Id, 3);
        cart.AddItem(productC.Id, 1);
        context.Carts.Add(cart);

        await context.SaveChangesAsync();
        return (user, address, productA, productB, productC, cart);
    }

    #region B4: Contract & Validation Tests

    [Fact]
    public void B4_QuoteValidator_WhenSelectedProductIdsIsEmpty_FailsValidation()
    {
        var validator = new GetCheckoutQuoteQueryValidator();
        var query = new GetCheckoutQuoteQuery(Guid.NewGuid(), []);
        var result = validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.SelectedProductIds)
            .WithErrorMessage("SelectedProductIds cannot be empty.");
    }

    [Fact]
    public void B4_QuoteValidator_WhenSelectedProductIdsContainsEmptyGuid_FailsValidation()
    {
        var validator = new GetCheckoutQuoteQueryValidator();
        var query = new GetCheckoutQuoteQuery(Guid.NewGuid(), [Guid.Empty]);
        var result = validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.SelectedProductIds)
            .WithErrorMessage("SelectedProductIds cannot contain empty GUID.");
    }

    [Fact]
    public void B4_QuoteValidator_WhenSelectedProductIdsHasDuplicates_FailsValidation()
    {
        var id = Guid.NewGuid();
        var validator = new GetCheckoutQuoteQueryValidator();
        var query = new GetCheckoutQuoteQuery(Guid.NewGuid(), [id, id]);
        var result = validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.SelectedProductIds)
            .WithErrorMessage("SelectedProductIds cannot contain duplicate product IDs.");
    }

    [Fact]
    public void B4_QuoteValidator_WhenSelectedProductIdsExceeds50_FailsValidation()
    {
        var ids = Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToList();
        var validator = new GetCheckoutQuoteQueryValidator();
        var query = new GetCheckoutQuoteQuery(Guid.NewGuid(), ids);
        var result = validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.SelectedProductIds)
            .WithErrorMessage("SelectedProductIds cannot contain more than 50 items.");
    }

    [Fact]
    public void B4_CheckoutCommandValidator_WhenSelectedProductIdsIsEmpty_FailsValidation()
    {
        var validator = new CheckoutCartCommandValidator();
        var cmd = new CheckoutCartCommand(Guid.NewGuid(), 1, PaymentMethod.COD, null, "valid-idempotency-key-12345", []);
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.SelectedProductIds)
            .WithErrorMessage("SelectedProductIds cannot be empty.");
    }

    [Fact]
    public void B4_CheckoutCommandValidator_WhenSelectedProductIdsHasDuplicates_FailsValidation()
    {
        var id = Guid.NewGuid();
        var validator = new CheckoutCartCommandValidator();
        var cmd = new CheckoutCartCommand(Guid.NewGuid(), 1, PaymentMethod.COD, null, "valid-idempotency-key-12345", [id, id]);
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.SelectedProductIds)
            .WithErrorMessage("SelectedProductIds cannot contain duplicate product IDs.");
    }

    [Fact]
    public async Task B4_WhenSelectedProductIdNotInCart_Returns409SelectionChanged_ZeroMutation()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, productA, _, _, cart) = await SeedMultiProductCartAsync(context);
        var authMock = CreateCurrentUserMock(user.Id);

        var outsideProductId = Guid.NewGuid();

        // 1. Quote check
        var quoteHandler = new GetCheckoutQuoteQueryHandler(context, authMock.Object);
        var quoteResult = await quoteHandler.Handle(new GetCheckoutQuoteQuery(address.Id, [productA.Id, outsideProductId]), CancellationToken.None);
        quoteResult.IsFailure.Should().BeTrue();
        quoteResult.Error.Code.Should().Be("Checkout.SelectionChanged");

        // 2. Checkout check
        var checkoutHandler = new CheckoutCartCommandHandler(context, authMock.Object);
        var checkoutResult = await checkoutHandler.Handle(new CheckoutCartCommand(
            AddressId: address.Id,
            ExpectedCartVersion: cart.Version,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: "valid-key-outside-item-12345",
            SelectedProductIds: [productA.Id, outsideProductId]
        ), CancellationToken.None);

        checkoutResult.IsFailure.Should().BeTrue();
        checkoutResult.Error.Code.Should().Be("Checkout.SelectionChanged");

        // Zero mutation verification
        (await context.Orders.CountAsync()).Should().Be(0);
        (await context.StockMovements.CountAsync()).Should().Be(0);
        var cartDb = await context.Carts.Include(c => c.Items).FirstAsync(c => c.Id == cart.Id);
        cartDb.Items.Should().HaveCount(3);
    }

    #endregion

    #region B1: Selected Quote Calculation

    [Fact]
    public async Task B1_GetCheckoutQuote_WhenSelectedAAndC_CalculatesQuoteOnlyForSelectedLines_PreservesCartVersion()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, productA, _, productC, cart) = await SeedMultiProductCartAsync(context);
        var authMock = CreateCurrentUserMock(user.Id);

        var handler = new GetCheckoutQuoteQueryHandler(context, authMock.Object);
        var result = await handler.Handle(new GetCheckoutQuoteQuery(address.Id, [productA.Id, productC.Id]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var quote = result.Value;

        // Quote only includes selected items A & C
        quote.Items.Should().HaveCount(2);
        quote.Items.Select(i => i.ProductId).Should().BeEquivalentTo([productA.Id, productC.Id]);

        // Total: A (qty 2 * 100k) + C (qty 1 * 300k) = 500k
        quote.Subtotal.Should().Be(500_000m);
        quote.FinalAmount.Should().Be(500_000m);
        quote.DiscountAmount.Should().Be(0m);

        // CartVersion remains whole cart version
        quote.CartVersion.Should().Be(cart.Version);
        quote.CanCheckout.Should().BeTrue();
        quote.Issues.Should().BeEmpty();
    }

    #endregion

    #region B2: Unselected Inactive / Out of Stock Product Does Not Block Selected Lines

    [Fact]
    public async Task B2_WhenUnselectedProductBIsInactiveOrOutOfStock_SelectedLinesAAndCCanQuoteAndCheckoutSuccessfully()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        // Product B is inactive and has 0 stock
        var (user, address, productA, productB, productC, cart) = await SeedMultiProductCartAsync(context, isProductBActive: false, stockB: 0);
        var authMock = CreateCurrentUserMock(user.Id);

        // 1. Quote for A & C
        var quoteHandler = new GetCheckoutQuoteQueryHandler(context, authMock.Object);
        var quoteResult = await quoteHandler.Handle(new GetCheckoutQuoteQuery(address.Id, [productA.Id, productC.Id]), CancellationToken.None);

        quoteResult.IsSuccess.Should().BeTrue();
        quoteResult.Value.CanCheckout.Should().BeTrue("Unselected inactive/out-of-stock product B must not block quote");
        quoteResult.Value.Issues.Should().BeEmpty();
        quoteResult.Value.Items.Should().HaveCount(2);

        // 2. Checkout for A & C
        var checkoutHandler = new CheckoutCartCommandHandler(context, authMock.Object);
        var checkoutResult = await checkoutHandler.Handle(new CheckoutCartCommand(
            AddressId: address.Id,
            ExpectedCartVersion: cart.Version,
            PaymentMethod: PaymentMethod.COD,
            Notes: "Selected items only",
            IdempotencyKey: "valid-key-b2-success-12345",
            SelectedProductIds: [productA.Id, productC.Id]
        ), CancellationToken.None);

        checkoutResult.IsSuccess.Should().BeTrue("Unselected inactive/out-of-stock product B must not block checkout");
        var orderDto = checkoutResult.Value;
        orderDto.Items.Should().HaveCount(2);
        orderDto.Items.Select(i => i.ProductId).Should().BeEquivalentTo([productA.Id, productC.Id]);
    }

    #endregion

    #region B3: Selecting Inactive / Insufficient Stock Item Blocks Checkout with Zero Mutation

    [Fact]
    public async Task B3_WhenSelectingInactiveOrInsufficientStockProductB_CheckoutFails_ZeroMutation()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        // Product B is inactive
        var (user, address, productA, productB, _, cart) = await SeedMultiProductCartAsync(context, isProductBActive: false);
        var authMock = CreateCurrentUserMock(user.Id);

        var initialCartVersion = cart.Version;

        var handler = new CheckoutCartCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new CheckoutCartCommand(
            AddressId: address.Id,
            ExpectedCartVersion: cart.Version,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: "valid-key-b3-inactive-12345",
            SelectedProductIds: [productA.Id, productB.Id]
        ), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.ProductInactive");

        // Zero mutation verification
        (await context.Orders.CountAsync()).Should().Be(0);
        (await context.StockMovements.CountAsync()).Should().Be(0);

        var cartDb = await context.Carts.Include(c => c.Items).FirstAsync(c => c.Id == cart.Id);
        cartDb.Items.Should().HaveCount(3);
        cartDb.Version.Should().Be(initialCartVersion);

        var prodADb = await context.Products.FindAsync(productA.Id);
        prodADb!.StockQuantity.Should().Be(20, "Stock of A must not be partially decremented");
    }

    #endregion

    #region B5: COD Selected A & C Atomic Partial Removal & Version Bump

    [Fact]
    public async Task B5_CheckoutCOD_WhenSelectedAAndC_CreatesOrderForAAndC_DecrementsStock_PreservesBInCart_RemovesAAndCFromDb()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, productA, productB, productC, cart) = await SeedMultiProductCartAsync(context);
        var authMock = CreateCurrentUserMock(user.Id);
        var initialCartVersion = cart.Version;

        var handler = new CheckoutCartCommandHandler(context, authMock.Object);
        var command = new CheckoutCartCommand(
            AddressId: address.Id,
            ExpectedCartVersion: cart.Version,
            PaymentMethod: PaymentMethod.COD,
            Notes: "COD partial checkout",
            IdempotencyKey: "valid-key-b5-cod-12345678",
            SelectedProductIds: [productA.Id, productC.Id]
        );

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var order = result.Value;
        order.Status.Should().Be(OrderStatus.Confirmed);
        order.PaymentStatus.Should().Be(PaymentStatus.Pending);
        order.Items.Should().HaveCount(2);
        order.Items.Select(i => i.ProductId).Should().BeEquivalentTo([productA.Id, productC.Id]);

        // Stock verification
        var prodA = await context.Products.FindAsync(productA.Id);
        prodA!.StockQuantity.Should().Be(18); // 20 - 2
        var prodB = await context.Products.FindAsync(productB.Id);
        prodB!.StockQuantity.Should().Be(50); // Untouched
        var prodC = await context.Products.FindAsync(productC.Id);
        prodC!.StockQuantity.Should().Be(14); // 15 - 1

        // Stock movements: exactly 2 for A and C
        var movements = await context.StockMovements.ToListAsync();
        movements.Should().HaveCount(2);
        movements.Should().Contain(m => m.ProductId == productA.Id && m.QuantityDelta == -2 && m.Reason == StockMovementReason.Checkout);
        movements.Should().Contain(m => m.ProductId == productC.Id && m.QuantityDelta == -1 && m.Reason == StockMovementReason.Checkout);

        // Cart verification: Item B remains with quantity 3, A & C removed, Version monotonic bump
        var cartDb = await context.Carts.Include(c => c.Items).FirstAsync(c => c.Id == cart.Id);
        cartDb.Items.Should().HaveCount(1);
        cartDb.Items.Single().ProductId.Should().Be(productB.Id);
        cartDb.Items.Single().Quantity.Should().Be(3);
        cartDb.Version.Should().BeGreaterThan(initialCartVersion);

        // Verify CartItems table in DB actually deleted A & C
        (await context.CartItems.AnyAsync(ci => ci.ProductId == productA.Id)).Should().BeFalse();
        (await context.CartItems.AnyAsync(ci => ci.ProductId == productC.Id)).Should().BeFalse();
        (await context.CartItems.AnyAsync(ci => ci.ProductId == productB.Id)).Should().BeTrue();
    }

    #endregion

    #region B6: Stripe Selected A & C Pending + Cancel Stock Restoration

    [Fact]
    public async Task B6_CheckoutStripe_SelectedAAndC_CartRemovesAAndC_CancelRestoresStockOnce_LeavesCartUntouched()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, productA, productB, productC, cart) = await SeedMultiProductCartAsync(context);
        var authMock = CreateCurrentUserMock(user.Id);

        var stripeMock = new Mock<IStripePaymentGateway>();
        stripeMock.Setup(s => s.IsEnabled).Returns(true);
        stripeMock.Setup(s => s.CheckoutSessionLifetime).Returns(TimeSpan.FromMinutes(30));
        stripeMock.Setup(s => s.EnsureCheckoutSessionAsync(
                It.IsAny<CreateStripeCheckoutSessionRequest>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((CreateStripeCheckoutSessionRequest req, string? r, CancellationToken _) =>
                Result.Success(new StripeCheckoutSessionResult(
                    SessionId: "cs_test_b6_session",
                    Url: "https://checkout.stripe.com/pay/cs_test_b6",
                    ExpiresAt: req.ExpiresAt
                )));
        stripeMock.Setup(s => s.ExpireCheckoutSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var handler = new CheckoutCartCommandHandler(context, authMock.Object, stripeMock.Object);
        var command = new CheckoutCartCommand(
            AddressId: address.Id,
            ExpectedCartVersion: cart.Version,
            PaymentMethod: PaymentMethod.Stripe,
            Notes: "Stripe test",
            IdempotencyKey: "valid-key-b6-stripe-123456",
            SelectedProductIds: [productA.Id, productC.Id]
        );

        var result = await handler.Handle(command, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        var order = result.Value;
        order.Status.Should().Be(OrderStatus.PendingPayment);
        order.PaymentStatus.Should().Be(PaymentStatus.Pending);

        // Cart only has B; A and C were removed at checkout time
        var cartDb = await context.Carts.Include(c => c.Items).FirstAsync(c => c.Id == cart.Id);
        cartDb.Items.Should().HaveCount(1);
        cartDb.Items.Single().ProductId.Should().Be(productB.Id);

        // Cancel order via CancelMyOrderCommand
        var cancelHandler = new CancelMyOrderCommandHandler(context, authMock.Object, stripeMock.Object);
        var cancelResult = await cancelHandler.Handle(new CancelMyOrderCommand(order.Id), CancellationToken.None);
        cancelResult.IsSuccess.Should().BeTrue();
        cancelResult.Value.Status.Should().Be(OrderStatus.Cancelled);

        // Stock restored for A and C
        var prodA = await context.Products.FindAsync(productA.Id);
        prodA!.StockQuantity.Should().Be(20); // restored
        var prodC = await context.Products.FindAsync(productC.Id);
        prodC!.StockQuantity.Should().Be(15); // restored
        var prodB = await context.Products.FindAsync(productB.Id);
        prodB!.StockQuantity.Should().Be(50); // untouched

        // Cart is NOT re-added with A and C; cart still only has B
        var cartAfterCancel = await context.Carts.Include(c => c.Items).FirstAsync(c => c.Id == cart.Id);
        cartAfterCancel.Items.Should().HaveCount(1);
        cartAfterCancel.Items.Single().ProductId.Should().Be(productB.Id);

        // Cancel a second time is idempotent: returns success without restoring stock a second time
        var cancelAgain = await cancelHandler.Handle(new CancelMyOrderCommand(order.Id), CancellationToken.None);
        cancelAgain.IsSuccess.Should().BeTrue();
        var prodAAfterSecondCancel = await context.Products.FindAsync(productA.Id);
        prodAAfterSecondCancel!.StockQuantity.Should().Be(20, "Stock must be restored only once, not doubled");
    }

    #endregion

    #region B7: Idempotent Replay Even When Cart Changed or Empty

    [Fact]
    public async Task B7_IdempotentReplay_SameKey_ReturnsSameOrder_EvenWhenCartChangedOrEmpty()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, productA, _, productC, cart) = await SeedMultiProductCartAsync(context);
        var authMock = CreateCurrentUserMock(user.Id);

        var idempotencyKey = "valid-key-b7-replay-1234567";
        var handler = new CheckoutCartCommandHandler(context, authMock.Object);

        // 1. Initial checkout
        var result1 = await handler.Handle(new CheckoutCartCommand(
            AddressId: address.Id,
            ExpectedCartVersion: cart.Version,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: idempotencyKey,
            SelectedProductIds: [productA.Id, productC.Id]
        ), CancellationToken.None);
        result1.IsSuccess.Should().BeTrue();

        // 2. Clear remaining cart completely
        var cartDb = await context.Carts.Include(c => c.Items).FirstAsync(c => c.Id == cart.Id);
        cartDb.Clear();
        await context.SaveChangesAsync();

        // 3. Replay with the same key
        var result2 = await handler.Handle(new CheckoutCartCommand(
            AddressId: address.Id,
            ExpectedCartVersion: 999, // Stale version in request
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: idempotencyKey,
            SelectedProductIds: [productA.Id, productC.Id] // Items no longer in cart!
        ), CancellationToken.None);

        result2.IsSuccess.Should().BeTrue("Idempotent replay must succeed without checking cart or selection");
        result2.Value.Id.Should().Be(result1.Value.Id);

        // Stock decremented only once
        var prodA = await context.Products.FindAsync(productA.Id);
        prodA!.StockQuantity.Should().Be(18); // 20 - 2, not 16!
    }

    #endregion

    #region B8: Concurrent Checkouts with Different Keys Against Same Cart Version

    [Fact]
    public async Task B8_TwoCheckoutsDifferentKeys_SameCartVersion_OneSucceeds_OneConflicts()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, productA, _, productC, cart) = await SeedMultiProductCartAsync(context);
        var authMock = CreateCurrentUserMock(user.Id);

        var initialCartVersion = cart.Version;
        var handler = new CheckoutCartCommandHandler(context, authMock.Object);

        // First checkout succeeds
        var res1 = await handler.Handle(new CheckoutCartCommand(
            AddressId: address.Id,
            ExpectedCartVersion: initialCartVersion,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: "valid-key-b8-first-12345678",
            SelectedProductIds: [productA.Id]
        ), CancellationToken.None);
        res1.IsSuccess.Should().BeTrue();

        // Second checkout with the OLD cart version fails with CartChanged
        var res2 = await handler.Handle(new CheckoutCartCommand(
            AddressId: address.Id,
            ExpectedCartVersion: initialCartVersion, // Stale!
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: "valid-key-b8-second-12345678",
            SelectedProductIds: [productC.Id]
        ), CancellationToken.None);

        res2.IsFailure.Should().BeTrue();
        res2.Error.Code.Should().Be("Checkout.CartChanged");
    }

    #endregion

    #region B9: Concurrency Conflict During SaveChanges Rolls Back Completely

    [Fact]
    public async Task B9_ConcurrencyConflictDuringSaveChanges_RollsBackAllMutations_PreservesUnselectedRows()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, productA, productB, productC, cart) = await SeedMultiProductCartAsync(context);
        var authMock = CreateCurrentUserMock(user.Id);

        // Inject interceptor throwing DbUpdateConcurrencyException
        context.SaveChangesInterceptor = () => throw new DbUpdateConcurrencyException("Simulated concurrency conflict");

        var handler = new CheckoutCartCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new CheckoutCartCommand(
            AddressId: address.Id,
            ExpectedCartVersion: cart.Version,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: "valid-key-b9-conflict-12345",
            SelectedProductIds: [productA.Id, productC.Id]
        ), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.InventoryChanged");

        // Clear interceptor and verify DB is completely intact
        context.SaveChangesInterceptor = null;
        (await context.Orders.CountAsync()).Should().Be(0);
        (await context.StockMovements.CountAsync()).Should().Be(0);

        var cartDb = await context.Carts.Include(c => c.Items).FirstAsync(c => c.Id == cart.Id);
        cartDb.Items.Should().HaveCount(3, "Unselected and selected rows must not be lost");
        cartDb.Items.Select(i => i.ProductId).Should().BeEquivalentTo([productA.Id, productB.Id, productC.Id]);
    }

    #endregion

    #region B10: Legacy Behavior (Null SelectedProductIds)

    [Fact]
    public async Task B10_LegacyCheckout_WhenSelectedProductIdsIsNull_ChecksOutEntireCart()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, productA, productB, productC, cart) = await SeedMultiProductCartAsync(context);
        var authMock = CreateCurrentUserMock(user.Id);

        var handler = new CheckoutCartCommandHandler(context, authMock.Object);
        var command = new CheckoutCartCommand(
            AddressId: address.Id,
            ExpectedCartVersion: cart.Version,
            PaymentMethod: PaymentMethod.COD,
            Notes: "Legacy full cart",
            IdempotencyKey: "valid-key-b10-legacy-12345",
            SelectedProductIds: null
        );

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(3);

        // Entire cart emptied
        var cartDb = await context.Carts.Include(c => c.Items).FirstAsync(c => c.Id == cart.Id);
        cartDb.Items.Should().BeEmpty();
    }

    #endregion

    #region B11: Authentication & Address Ownership Security

    [Fact]
    public async Task B11_WhenUserNotAuthenticated_ReturnsUnauthorized()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (_, address, productA, _, _, cart) = await SeedMultiProductCartAsync(context);
        var authMock = CreateCurrentUserMock(Guid.Empty, isAuthenticated: false);

        var handler = new CheckoutCartCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new CheckoutCartCommand(
            AddressId: address.Id,
            ExpectedCartVersion: cart.Version,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: "valid-key-b11-unauth-12345",
            SelectedProductIds: [productA.Id]
        ), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.Unauthorized");
    }

    [Fact]
    public async Task B11_WhenAddressBelongsToAnotherUser_ReturnsNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, productA, _, _, cart) = await SeedMultiProductCartAsync(context);
        var authMock = CreateCurrentUserMock(user.Id);

        var foreignAddress = new UserAddress(
            userId: Guid.NewGuid(), // Other user
            receiverName: "Foreign Receiver",
            phoneNumber: "0908888888",
            addressLine: "Foreign St",
            ward: "W",
            district: "D",
            city: "C"
        );
        context.UserAddresses.Add(foreignAddress);
        await context.SaveChangesAsync();

        var handler = new CheckoutCartCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new CheckoutCartCommand(
            AddressId: foreignAddress.Id,
            ExpectedCartVersion: cart.Version,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: "valid-key-b11-foreign-addr-12",
            SelectedProductIds: [productA.Id]
        ), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.AddressNotFound");
    }

    #endregion
}
