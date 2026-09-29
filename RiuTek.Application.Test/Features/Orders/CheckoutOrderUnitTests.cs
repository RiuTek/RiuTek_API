using FluentAssertions;
using FluentValidation.TestHelper;
using Microsoft.EntityFrameworkCore;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Orders.Commands;
using RiuTek.Application.Features.Orders.Queries;
using RiuTek.Application.Features.Products.Commands;
using RiuTek.Application.Test.Features.Products;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using Xunit;

namespace RiuTek.Application.Test.Features.Orders;

public class CheckoutOrderUnitTests
{
    private static async Task<(User User, UserAddress Address, Product Product, Cart Cart)> SeedPrerequisitesAsync(
        TestApplicationDbContext context,
        bool isUserActive = true,
        bool isProductActive = true,
        int stockQuantity = 50,
        decimal price = 200_000m,
        int cartQuantity = 2)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(
            email: $"user_{suffix}@riutek.test",
            passwordHash: "dummy_hash",
            fullName: $"Test User {suffix}",
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
        if (cartQuantity > 0)
        {
            cart.AddItem(product.Id, cartQuantity);
        }
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
    public void GetCheckoutQuoteQueryValidator_WhenAddressIdEmpty_HasValidationError()
    {
        var validator = new GetCheckoutQuoteQueryValidator();
        var result = validator.TestValidate(new GetCheckoutQuoteQuery(Guid.Empty));
        result.ShouldHaveValidationErrorFor(x => x.AddressId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CheckoutCartCommandValidator_WhenExpectedCartVersionInvalid_HasValidationError(int version)
    {
        var validator = new CheckoutCartCommandValidator();
        var command = new CheckoutCartCommand(
            AddressId: Guid.NewGuid(),
            ExpectedCartVersion: version,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: "valid-idempotency-key-12345"
        );
        var result = validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.ExpectedCartVersion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("too-short-key")] // 14 chars
    public void CheckoutCartCommandValidator_WhenIdempotencyKeyTooShortOrEmpty_HasValidationError(string key)
    {
        var validator = new CheckoutCartCommandValidator();
        var command = new CheckoutCartCommand(
            AddressId: Guid.NewGuid(),
            ExpectedCartVersion: 1,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: key
        );
        var result = validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.IdempotencyKey);
    }

    [Fact]
    public void CheckoutCartCommandValidator_WhenIdempotencyKeyTooLong_HasValidationError()
    {
        var validator = new CheckoutCartCommandValidator();
        var longKey = new string('x', 129);
        var command = new CheckoutCartCommand(
            AddressId: Guid.NewGuid(),
            ExpectedCartVersion: 1,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: longKey
        );
        var result = validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.IdempotencyKey);
    }

    [Fact]
    public void CheckoutCartCommandValidator_WhenNotesExceed1000Chars_HasValidationError()
    {
        var validator = new CheckoutCartCommandValidator();
        var command = new CheckoutCartCommand(
            AddressId: Guid.NewGuid(),
            ExpectedCartVersion: 1,
            PaymentMethod: PaymentMethod.COD,
            Notes: new string('a', 1001),
            IdempotencyKey: "valid-idempotency-key-12345"
        );
        var result = validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Notes);
    }

    #endregion

    #region Quote Query Tests

    [Fact]
    public async Task GetCheckoutQuote_WhenUnauthenticated_ReturnsUnauthorized()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var authMock = CreateCurrentUserMock(null, isAuthenticated: false);
        var handler = new GetCheckoutQuoteQueryHandler(context, authMock.Object);

        var result = await handler.Handle(new GetCheckoutQuoteQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.Unauthorized");
    }

    [Fact]
    public async Task GetCheckoutQuote_WhenUserNotFound_ReturnsNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var authMock = CreateCurrentUserMock(Guid.NewGuid(), isAuthenticated: true);
        var handler = new GetCheckoutQuoteQueryHandler(context, authMock.Object);

        var result = await handler.Handle(new GetCheckoutQuoteQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.UserNotFound");
    }

    [Fact]
    public async Task GetCheckoutQuote_WhenUserInactive_ReturnsForbidden()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, _, _) = await SeedPrerequisitesAsync(context, isUserActive: false);
        var authMock = CreateCurrentUserMock(user.Id);
        var handler = new GetCheckoutQuoteQueryHandler(context, authMock.Object);

        var result = await handler.Handle(new GetCheckoutQuoteQuery(address.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.AccountInactive");
    }

    [Fact]
    public async Task GetCheckoutQuote_WhenAddressBelongsToAnotherUser_ReturnsNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user1, _, _, _) = await SeedPrerequisitesAsync(context);
        var (_, address2, _, _) = await SeedPrerequisitesAsync(context);

        var authMock = CreateCurrentUserMock(user1.Id);
        var handler = new GetCheckoutQuoteQueryHandler(context, authMock.Object);

        var result = await handler.Handle(new GetCheckoutQuoteQuery(address2.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.AddressNotFound");
    }

    [Fact]
    public async Task GetCheckoutQuote_WhenCartIsEmpty_ReturnsEmptyQuoteWithCanCheckoutFalse()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, _, _) = await SeedPrerequisitesAsync(context, cartQuantity: 0);
        var authMock = CreateCurrentUserMock(user.Id);
        var handler = new GetCheckoutQuoteQueryHandler(context, authMock.Object);

        var result = await handler.Handle(new GetCheckoutQuoteQuery(address.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CanCheckout.Should().BeFalse();
        result.Value.Items.Should().BeEmpty();
        result.Value.Subtotal.Should().Be(0m);
        result.Value.Issues.Should().Contain("EMPTY_CART");
    }

    [Fact]
    public async Task GetCheckoutQuote_WhenProductInactive_ReturnsCanCheckoutFalseWithProductInactiveIssue()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, _) = await SeedPrerequisitesAsync(context, isProductActive: false);
        var authMock = CreateCurrentUserMock(user.Id);
        var handler = new GetCheckoutQuoteQueryHandler(context, authMock.Object);

        var result = await handler.Handle(new GetCheckoutQuoteQuery(address.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CanCheckout.Should().BeFalse();
        result.Value.Items.Should().ContainSingle();
        result.Value.Items[0].CanPurchase.Should().BeFalse();
        result.Value.Items[0].IssueCode.Should().Be("PRODUCT_INACTIVE");
        result.Value.Issues.Should().Contain("PRODUCT_INACTIVE");
    }

    [Fact]
    public async Task GetCheckoutQuote_WhenInsufficientStock_ReturnsCanCheckoutFalseWithInsufficientStockIssue()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, _) = await SeedPrerequisitesAsync(context, stockQuantity: 1, cartQuantity: 5);
        var authMock = CreateCurrentUserMock(user.Id);
        var handler = new GetCheckoutQuoteQueryHandler(context, authMock.Object);

        var result = await handler.Handle(new GetCheckoutQuoteQuery(address.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CanCheckout.Should().BeFalse();
        result.Value.Items.Should().ContainSingle();
        result.Value.Items[0].CanPurchase.Should().BeFalse();
        result.Value.Items[0].IssueCode.Should().Be("INSUFFICIENT_STOCK");
        result.Value.Issues.Should().Contain("INSUFFICIENT_STOCK");
    }

    [Fact]
    public async Task GetCheckoutQuote_HappyPath_ReturnsDeterministicOrderAndDoesNotMutateDatabase()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product1, cart) = await SeedPrerequisitesAsync(context, price: 100_000m, stockQuantity: 10, cartQuantity: 2);

        // Add second product with alphabetical sorting test: "Alpha" vs "Product"
        var category = await context.Categories.FirstAsync();
        var product2 = new Product(
            categoryId: category.Id,
            name: "Alpha Product",
            slug: "alpha-product",
            sku: "SKU-ALPHA",
            brand: "Brand",
            price: 50_000m,
            stockQuantity: 5,
            imageUrl: "https://riutek.test/alpha.png",
            componentType: ComponentType.Accessory,
            specifications: new AccessorySpecification { Details = "Alpha specs" }
        );
        context.Products.Add(product2);
        cart.AddItem(product2.Id, 1);
        await context.SaveChangesAsync();

        var initialCartVersion = cart.Version;
        var initialStock1 = product1.StockQuantity;
        var initialStock2 = product2.StockQuantity;

        var authMock = CreateCurrentUserMock(user.Id);
        var handler = new GetCheckoutQuoteQueryHandler(context, authMock.Object);

        var result = await handler.Handle(new GetCheckoutQuoteQuery(address.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var quote = result.Value;
        quote.CanCheckout.Should().BeTrue();
        quote.Issues.Should().BeEmpty();
        quote.Items.Should().HaveCount(2);

        // Deterministic sorting: "Alpha Product" before "Product..."
        quote.Items[0].Name.Should().Be("Alpha Product");
        quote.Items[0].UnitPrice.Should().Be(50_000m);
        quote.Items[0].LineTotal.Should().Be(50_000m);

        quote.Items[1].Name.Should().Be(product1.Name);
        quote.Items[1].UnitPrice.Should().Be(100_000m);
        quote.Items[1].LineTotal.Should().Be(200_000m);

        quote.Subtotal.Should().Be(250_000m);
        quote.DiscountAmount.Should().Be(0m);
        quote.FinalAmount.Should().Be(250_000m);
        quote.ShippingAddress.Should().Be("123 Test Street, Ward 1, District 1, Ho Chi Minh City");

        // Verify NO mutations
        var freshCart = await context.Carts.Include(c => c.Items).FirstAsync(c => c.UserId == user.Id);
        freshCart.Version.Should().Be(initialCartVersion);
        freshCart.Items.Should().HaveCount(2);

        var freshP1 = await context.Products.FindAsync(product1.Id);
        freshP1!.StockQuantity.Should().Be(initialStock1);

        var freshP2 = await context.Products.FindAsync(product2.Id);
        freshP2!.StockQuantity.Should().Be(initialStock2);

        (await context.Orders.CountAsync()).Should().Be(0);
    }

    #endregion

    #region Checkout Command Tests

    [Fact]
    public async Task CheckoutCart_WhenPaymentMethodNotCOD_ReturnsPaymentMethodNotAvailable()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, _, cart) = await SeedPrerequisitesAsync(context);
        var authMock = CreateCurrentUserMock(user.Id);
        var handler = new CheckoutCartCommandHandler(context, authMock.Object);

        var command = new CheckoutCartCommand(
            AddressId: address.Id,
            ExpectedCartVersion: cart.Version,
            PaymentMethod: PaymentMethod.Stripe,
            Notes: null,
            IdempotencyKey: "valid-idempotency-key-stripe"
        );

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.PaymentMethodNotAvailable");
        (await context.Orders.CountAsync()).Should().Be(0);
        (await context.PaymentAttempts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CheckoutCart_WhenAddressBelongsToOtherUser_ReturnsAddressNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user1, _, _, cart1) = await SeedPrerequisitesAsync(context);
        var (_, address2, _, _) = await SeedPrerequisitesAsync(context);

        var authMock = CreateCurrentUserMock(user1.Id);
        var handler = new CheckoutCartCommandHandler(context, authMock.Object);

        var command = new CheckoutCartCommand(
            AddressId: address2.Id,
            ExpectedCartVersion: cart1.Version,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: "valid-idempotency-key-addr"
        );

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.AddressNotFound");
    }

    [Fact]
    public async Task CheckoutCart_WhenCartVersionMismatch_ReturnsCartChangedConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, _, cart) = await SeedPrerequisitesAsync(context);
        var authMock = CreateCurrentUserMock(user.Id);
        var handler = new CheckoutCartCommandHandler(context, authMock.Object);

        var command = new CheckoutCartCommand(
            AddressId: address.Id,
            ExpectedCartVersion: cart.Version + 10,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: "valid-idempotency-key-stale"
        );

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("Checkout.CartChanged");
        (await context.Orders.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CheckoutCart_WhenCartEmpty_ReturnsEmptyCart()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, _, cart) = await SeedPrerequisitesAsync(context, cartQuantity: 0);
        var authMock = CreateCurrentUserMock(user.Id);
        var handler = new CheckoutCartCommandHandler(context, authMock.Object);

        var command = new CheckoutCartCommand(
            AddressId: address.Id,
            ExpectedCartVersion: cart.Version,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: "valid-idempotency-key-empty"
        );

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.EmptyCart");
    }

    [Fact]
    public async Task CheckoutCart_WhenProductInactive_ReturnsProductInactive()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, cart) = await SeedPrerequisitesAsync(context, isProductActive: false);
        var authMock = CreateCurrentUserMock(user.Id);
        var handler = new CheckoutCartCommandHandler(context, authMock.Object);

        var command = new CheckoutCartCommand(
            AddressId: address.Id,
            ExpectedCartVersion: cart.Version,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: "valid-idempotency-key-inactive"
        );

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.ProductInactive");
        (await context.Orders.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CheckoutCart_WhenInsufficientStock_ReturnsInsufficientStock()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, cart) = await SeedPrerequisitesAsync(context, stockQuantity: 1, cartQuantity: 3);
        var authMock = CreateCurrentUserMock(user.Id);
        var handler = new CheckoutCartCommandHandler(context, authMock.Object);

        var command = new CheckoutCartCommand(
            AddressId: address.Id,
            ExpectedCartVersion: cart.Version,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: "valid-idempotency-key-lowstock"
        );

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.InsufficientStock");
        (await context.Orders.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CheckoutCart_HappyPath_CreatesOrder_DecrementsStock_ClearsCart()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var initialStock = 50;
        var cartQty = 2;
        var unitPrice = 200_000m;
        var (user, address, product, cart) = await SeedPrerequisitesAsync(
            context,
            stockQuantity: initialStock,
            price: unitPrice,
            cartQuantity: cartQty
        );

        var authMock = CreateCurrentUserMock(user.Id);
        var handler = new CheckoutCartCommandHandler(context, authMock.Object);

        const string idempotencyKey = "happy-path-idempotency-key-01";
        var command = new CheckoutCartCommand(
            AddressId: address.Id,
            ExpectedCartVersion: cart.Version,
            PaymentMethod: PaymentMethod.COD,
            Notes: "Leave at door",
            IdempotencyKey: idempotencyKey
        );

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var orderDto = result.Value;
        orderDto.Status.Should().Be(OrderStatus.Confirmed);
        orderDto.PaymentMethod.Should().Be(PaymentMethod.COD);
        orderDto.PaymentStatus.Should().Be(PaymentStatus.Pending);
        orderDto.TotalAmount.Should().Be(unitPrice * cartQty);
        orderDto.DiscountAmount.Should().Be(0m);
        orderDto.FinalAmount.Should().Be(unitPrice * cartQty);
        orderDto.Notes.Should().Be("Leave at door");
        orderDto.CustomerName.Should().Be(address.ReceiverName);
        orderDto.CustomerEmail.Should().Be(user.Email);
        orderDto.CustomerPhone.Should().Be(address.PhoneNumber);
        orderDto.ShippingAddress.Should().Be("123 Test Street, Ward 1, District 1, Ho Chi Minh City");
        orderDto.OrderNumber.Should().StartWith("ORD-").And.HaveLength(27);
        orderDto.Items.Should().ContainSingle(i =>
            i.ProductId == product.Id &&
            i.UnitPrice == unitPrice &&
            i.Quantity == cartQty &&
            i.TotalPrice == unitPrice * cartQty &&
            i.ProductName == product.Name &&
            i.ProductSku == product.Sku.ToUpperInvariant()
        );

        // Verify DB mutations
        var dbOrder = await context.Orders.Include(o => o.Items).SingleAsync(o => o.Id == orderDto.Id);
        dbOrder.CheckoutIdempotencyKey.Should().Be(idempotencyKey);

        var freshProduct = await context.Products.FindAsync(product.Id);
        freshProduct!.StockQuantity.Should().Be(initialStock - cartQty);

        var freshCart = await context.Carts.Include(c => c.Items).SingleAsync(c => c.UserId == user.Id);
        freshCart.Items.Should().BeEmpty();
        freshCart.Version.Should().Be(cart.Version); // Note: cart.Clear() touched version before SaveChanges
    }

    [Fact]
    public async Task CheckoutCart_IdempotentReplay_ReturnsSameOrder_EvenWhenCartIsEmpty()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, product, cart) = await SeedPrerequisitesAsync(context, stockQuantity: 20, cartQuantity: 2);
        var authMock = CreateCurrentUserMock(user.Id);
        var handler = new CheckoutCartCommandHandler(context, authMock.Object);

        const string idempotencyKey = "replay-test-idempotency-key-01";
        var command = new CheckoutCartCommand(
            AddressId: address.Id,
            ExpectedCartVersion: cart.Version,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: idempotencyKey
        );

        // First call: succeeds and creates order
        var result1 = await handler.Handle(command, CancellationToken.None);
        result1.IsSuccess.Should().BeTrue();

        var stockAfterFirst = (await context.Products.FindAsync(product.Id))!.StockQuantity;

        // Second call with same key: Cart is now empty, but must return same order
        var result2 = await handler.Handle(command, CancellationToken.None);
        result2.IsSuccess.Should().BeTrue();
        result2.Value.Id.Should().Be(result1.Value.Id);
        result2.Value.OrderNumber.Should().Be(result1.Value.OrderNumber);

        // Verify stock was NOT decremented again
        var stockAfterSecond = (await context.Products.FindAsync(product.Id))!.StockQuantity;
        stockAfterSecond.Should().Be(stockAfterFirst);

        (await context.Orders.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CheckoutCart_SameKeyDifferentUsers_AreIndependent()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user1, address1, product1, cart1) = await SeedPrerequisitesAsync(context, stockQuantity: 50, cartQuantity: 1);
        var (user2, address2, _, cart2) = await SeedPrerequisitesAsync(context, stockQuantity: 50, cartQuantity: 1);

        const string sharedKey = "shared-idempotency-key-different-users";

        var authMock1 = CreateCurrentUserMock(user1.Id);
        var handler1 = new CheckoutCartCommandHandler(context, authMock1.Object);

        var result1 = await handler1.Handle(new CheckoutCartCommand(
            AddressId: address1.Id,
            ExpectedCartVersion: cart1.Version,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: sharedKey
        ), CancellationToken.None);

        var authMock2 = CreateCurrentUserMock(user2.Id);
        var handler2 = new CheckoutCartCommandHandler(context, authMock2.Object);

        var result2 = await handler2.Handle(new CheckoutCartCommand(
            AddressId: address2.Id,
            ExpectedCartVersion: cart2.Version,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: sharedKey
        ), CancellationToken.None);

        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();
        result1.Value.Id.Should().NotBe(result2.Value.Id);
        result1.Value.OrderNumber.Should().NotBe(result2.Value.OrderNumber);

        (await context.Orders.CountAsync()).Should().Be(2);
    }

    #endregion

    #region UpdateProduct Concurrency Regression Test

    private class ConcurrencyThrowingDbContext : TestApplicationDbContext
    {
        public bool ShouldThrow { get; set; }

        public ConcurrencyThrowingDbContext(DbContextOptions<TestApplicationDbContext> options) : base(options) { }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ShouldThrow)
            {
                throw new DbUpdateConcurrencyException("Concurrency conflict");
            }
            return base.SaveChangesAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task UpdateProduct_WhenDbUpdateConcurrencyExceptionOccurs_ReturnsConcurrencyConflict()
    {
        var options = new DbContextOptionsBuilder<TestApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ConcurrencyThrowingDbContext(options);

        var category = new Category("Cat", "cat", ComponentType.Cpu);
        context.Categories.Add(category);

        var product = new Product(
            categoryId: category.Id,
            name: "Old Name",
            slug: "old-name",
            sku: "OLD-SKU",
            brand: "Brand",
            price: 100m,
            stockQuantity: 10,
            imageUrl: "https://test.com/img.png",
            componentType: ComponentType.Cpu,
            specifications: ProductCommandValidatorTests.CreateValidCpuSpec()
        );
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var userId = Guid.NewGuid();
        var mockUserService = new Mock<ICurrentUserService>();
        mockUserService.Setup(u => u.IsAuthenticated).Returns(true);
        mockUserService.Setup(u => u.UserId).Returns(userId);
        mockUserService.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new UpdateProductCommandHandler(context, mockUserService.Object);

        // Turn on throwing before update
        context.ShouldThrow = true;

        var command = new UpdateProductCommand(
            Id: product.Id,
            CategoryId: category.Id,
            Name: "New Name",
            Sku: "NEW-SKU",
            Brand: "Brand",
            Price: 150m,
            OriginalPrice: null,
            StockQuantity: 15,
            IsActive: true,
            ImageUrl: "https://test.com/new.png",
            AdditionalImages: [],
            ComponentType: ComponentType.Cpu,
            Specifications: ProductCommandValidatorTests.CreateValidCpuSpec()
        );

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("Product.ConcurrencyConflict");
    }

    #endregion
}
