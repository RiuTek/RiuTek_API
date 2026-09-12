using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Carts.Commands;
using RiuTek.Application.Features.Carts.Queries;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using Xunit;

namespace RiuTek.Application.Test.Features.Carts;

public class CartApplicationTests
{
    private static async Task<(User User, Category Category, Product Product)> SeedPrerequisitesAsync(
        TestApplicationDbContext context,
        bool isUserActive = true,
        bool isProductActive = true,
        int stockQuantity = 50,
        decimal price = 200_000m)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(
            email: $"user_{suffix}@riutek.test",
            passwordHash: "dummy_hash",
            fullName: $"Test User {suffix}",
            role: UserRole.Customer
        )
        {
            IsActive = isUserActive
        };
        context.Users.Add(user);

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

        await context.SaveChangesAsync();
        return (user, category, product);
    }

    private static Mock<ICurrentUserService> CreateCurrentUserMock(Guid? userId, bool isAuthenticated = true)
    {
        var mock = new Mock<ICurrentUserService>();
        mock.Setup(x => x.IsAuthenticated).Returns(isAuthenticated);
        mock.Setup(x => x.UserId).Returns(userId);
        return mock;
    }

    // 1. GetCartQuery Tests
    [Fact]
    public async Task GetCart_WhenNoCartExists_ReturnsEmptyDtoWithoutInsertingRow()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, _) = await SeedPrerequisitesAsync(context);
        var authMock = CreateCurrentUserMock(user.Id);

        var handler = new GetCartQueryHandler(context, authMock.Object);
        var result = await handler.Handle(new GetCartQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CartId.Should().BeNull();
        result.Value.Version.Should().Be(0);
        result.Value.Items.Should().BeEmpty();
        result.Value.Subtotal.Should().Be(0m);
        result.Value.CanCheckout.Should().BeFalse();

        (await context.Carts.CountAsync()).Should().Be(0, "GET must never insert a Cart row");
    }

    [Fact]
    public async Task GetCart_WhenCartHasItems_ReturnsHydratedDtoWithCorrectSubtotal()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, product) = await SeedPrerequisitesAsync(context, price: 150_000m, stockQuantity: 20);
        var authMock = CreateCurrentUserMock(user.Id);

        var cart = new Cart(user.Id);
        cart.AddItem(product.Id, 3);
        context.Carts.Add(cart);
        await context.SaveChangesAsync();

        var handler = new GetCartQueryHandler(context, authMock.Object);
        var result = await handler.Handle(new GetCartQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CartId.Should().Be(cart.Id);
        result.Value.Items.Should().HaveCount(1);
        result.Value.Subtotal.Should().Be(450_000m);
        result.Value.CanCheckout.Should().BeTrue();

        var item = result.Value.Items.First();
        item.ProductId.Should().Be(product.Id);
        item.UnitPrice.Should().Be(150_000m);
        item.Quantity.Should().Be(3);
        item.LineTotal.Should().Be(450_000m);
        item.CanPurchase.Should().BeTrue();
        item.IssueCode.Should().BeNull();
    }

    [Fact]
    public async Task GetCart_WhenProductPriceChanges_ReturnsUpdatedPriceAndSubtotal()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, product) = await SeedPrerequisitesAsync(context, price: 100_000m);
        var authMock = CreateCurrentUserMock(user.Id);

        var cart = new Cart(user.Id);
        cart.AddItem(product.Id, 2);
        context.Carts.Add(cart);
        await context.SaveChangesAsync();

        // Product price changes in catalog
        product.Price = 180_000m;
        await context.SaveChangesAsync();

        var handler = new GetCartQueryHandler(context, authMock.Object);
        var result = await handler.Handle(new GetCartQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.First().UnitPrice.Should().Be(180_000m);
        result.Value.Items.First().LineTotal.Should().Be(360_000m);
        result.Value.Subtotal.Should().Be(360_000m);
    }

    [Fact]
    public async Task GetCart_WhenProductBecomesInactiveOrOutOfStock_LineRemainsWithCanPurchaseFalse()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, product) = await SeedPrerequisitesAsync(context, stockQuantity: 5);
        var authMock = CreateCurrentUserMock(user.Id);

        var cart = new Cart(user.Id);
        cart.AddItem(product.Id, 5);
        context.Carts.Add(cart);
        await context.SaveChangesAsync();

        // Product becomes inactive
        product.IsActive = false;
        await context.SaveChangesAsync();

        var handler = new GetCartQueryHandler(context, authMock.Object);
        var result = await handler.Handle(new GetCartQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(1);
        result.Value.CanCheckout.Should().BeFalse();

        var item = result.Value.Items.First();
        item.IsActive.Should().BeFalse();
        item.CanPurchase.Should().BeFalse();
        item.IssueCode.Should().Be("INACTIVE");
    }

    [Fact]
    public async Task GetCart_WhenUnauthenticated_ReturnsUnauthorizedError()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var authMock = CreateCurrentUserMock(null, isAuthenticated: false);

        var handler = new GetCartQueryHandler(context, authMock.Object);
        var result = await handler.Handle(new GetCartQuery(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task GetCart_WhenUserAccountIsInactive_ReturnsForbiddenError()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, _) = await SeedPrerequisitesAsync(context, isUserActive: false);
        var authMock = CreateCurrentUserMock(user.Id);

        var handler = new GetCartQueryHandler(context, authMock.Object);
        var result = await handler.Handle(new GetCartQuery(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    // 2. AddCartItemCommand Tests
    [Fact]
    public async Task AddItem_NewProduct_CreatesCartAndAddsItem()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, product) = await SeedPrerequisitesAsync(context, price: 200_000m, stockQuantity: 10);
        var authMock = CreateCurrentUserMock(user.Id);

        var handler = new AddCartItemCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new AddCartItemCommand(product.Id, 3), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CartId.Should().NotBeNull();
        result.Value.Items.Should().HaveCount(1);
        result.Value.Items.First().Quantity.Should().Be(3);
        result.Value.Subtotal.Should().Be(600_000m);

        var savedCart = await context.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.UserId == user.Id);
        savedCart.Should().NotBeNull();
        savedCart!.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task AddItem_ExistingProduct_AccumulatesQuantity()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, product) = await SeedPrerequisitesAsync(context, stockQuantity: 20);
        var authMock = CreateCurrentUserMock(user.Id);

        var cart = new Cart(user.Id);
        cart.AddItem(product.Id, 2);
        context.Carts.Add(cart);
        await context.SaveChangesAsync();

        var handler = new AddCartItemCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new AddCartItemCommand(product.Id, 3), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(1);
        result.Value.Items.First().Quantity.Should().Be(5);
    }

    [Fact]
    public async Task AddItem_WhenProductIsInactive_ReturnsConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, product) = await SeedPrerequisitesAsync(context, isProductActive: false);
        var authMock = CreateCurrentUserMock(user.Id);

        var handler = new AddCartItemCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new AddCartItemCommand(product.Id, 1), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("Cart.ProductInactive");
        (await context.Carts.CountAsync()).Should().Be(0, "Cart must not be created on validation failure");
    }

    [Fact]
    public async Task AddItem_WhenProductIsOutOfStock_ReturnsConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, product) = await SeedPrerequisitesAsync(context, stockQuantity: 0);
        var authMock = CreateCurrentUserMock(user.Id);

        var handler = new AddCartItemCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new AddCartItemCommand(product.Id, 1), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("Cart.InsufficientStock");
    }

    [Fact]
    public async Task AddItem_WhenRequestedQuantityExceedsAvailableStock_ReturnsConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, product) = await SeedPrerequisitesAsync(context, stockQuantity: 5);
        var authMock = CreateCurrentUserMock(user.Id);

        var handler = new AddCartItemCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new AddCartItemCommand(product.Id, 6), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("Cart.InsufficientStock");
    }

    [Fact]
    public async Task AddItem_WhenAccumulatedQuantityExceeds99_ReturnsValidationFailure()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, product) = await SeedPrerequisitesAsync(context, stockQuantity: 100);
        var authMock = CreateCurrentUserMock(user.Id);

        var cart = new Cart(user.Id);
        cart.AddItem(product.Id, 95);
        context.Carts.Add(cart);
        await context.SaveChangesAsync();

        var handler = new AddCartItemCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new AddCartItemCommand(product.Id, 5), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be("Cart.MaxQuantityExceeded");
    }

    // 3. SetCartItemQuantityCommand Tests
    [Fact]
    public async Task SetQuantity_ValidIncrease_UpdatesQuantity()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, product) = await SeedPrerequisitesAsync(context, stockQuantity: 20);
        var authMock = CreateCurrentUserMock(user.Id);

        var cart = new Cart(user.Id);
        cart.AddItem(product.Id, 2);
        context.Carts.Add(cart);
        await context.SaveChangesAsync();

        var handler = new SetCartItemQuantityCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new SetCartItemQuantityCommand(product.Id, 7), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.First().Quantity.Should().Be(7);
    }

    [Fact]
    public async Task SetQuantity_SameQuantity_IsIdempotentAndDoesNotSave()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, product) = await SeedPrerequisitesAsync(context, stockQuantity: 20);
        var authMock = CreateCurrentUserMock(user.Id);

        var cart = new Cart(user.Id);
        cart.AddItem(product.Id, 3);
        context.Carts.Add(cart);
        await context.SaveChangesAsync();
        var initialVersion = cart.Version;

        var handler = new SetCartItemQuantityCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new SetCartItemQuantityCommand(product.Id, 3), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Version.Should().Be(initialVersion, "Idempotent set must not bump Version");
    }

    [Fact]
    public async Task SetQuantity_IncreaseWhenProductInactive_ReturnsConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, product) = await SeedPrerequisitesAsync(context, stockQuantity: 20);
        var authMock = CreateCurrentUserMock(user.Id);

        var cart = new Cart(user.Id);
        cart.AddItem(product.Id, 2);
        context.Carts.Add(cart);
        await context.SaveChangesAsync();

        product.IsActive = false;
        await context.SaveChangesAsync();

        var handler = new SetCartItemQuantityCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new SetCartItemQuantityCommand(product.Id, 5), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("Cart.ProductInactive");
    }

    [Fact]
    public async Task SetQuantity_DecreaseWhenProductInactive_IsAllowed()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, product) = await SeedPrerequisitesAsync(context, stockQuantity: 20);
        var authMock = CreateCurrentUserMock(user.Id);

        var cart = new Cart(user.Id);
        cart.AddItem(product.Id, 5);
        context.Carts.Add(cart);
        await context.SaveChangesAsync();

        product.IsActive = false;
        await context.SaveChangesAsync();

        var handler = new SetCartItemQuantityCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new SetCartItemQuantityCommand(product.Id, 2), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.First().Quantity.Should().Be(2);
    }

    [Fact]
    public async Task SetQuantity_WhenItemNotInCart_ReturnsNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, _) = await SeedPrerequisitesAsync(context);
        var authMock = CreateCurrentUserMock(user.Id);

        var cart = new Cart(user.Id);
        context.Carts.Add(cart);
        await context.SaveChangesAsync();

        var handler = new SetCartItemQuantityCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new SetCartItemQuantityCommand(Guid.NewGuid(), 2), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("Cart.ItemNotFound");
    }

    // 4. RemoveCartItemCommand Tests
    [Fact]
    public async Task RemoveItem_WhenItemExists_RemovesItemSuccessfully()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, product) = await SeedPrerequisitesAsync(context);
        var authMock = CreateCurrentUserMock(user.Id);

        var cart = new Cart(user.Id);
        cart.AddItem(product.Id, 2);
        context.Carts.Add(cart);
        await context.SaveChangesAsync();

        var handler = new RemoveCartItemCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new RemoveCartItemCommand(product.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var reloaded = await context.Carts.Include(c => c.Items).FirstAsync(c => c.Id == cart.Id);
        reloaded.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveItem_WhenItemNotInCart_ReturnsNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, _) = await SeedPrerequisitesAsync(context);
        var authMock = CreateCurrentUserMock(user.Id);

        var cart = new Cart(user.Id);
        context.Carts.Add(cart);
        await context.SaveChangesAsync();

        var handler = new RemoveCartItemCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new RemoveCartItemCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("Cart.ItemNotFound");
    }

    // 5. ClearCartCommand Tests
    [Fact]
    public async Task ClearCart_WhenCartHasItems_ClearsAllItems()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, product) = await SeedPrerequisitesAsync(context);
        var authMock = CreateCurrentUserMock(user.Id);

        var cart = new Cart(user.Id);
        cart.AddItem(product.Id, 2);
        context.Carts.Add(cart);
        await context.SaveChangesAsync();

        var handler = new ClearCartCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new ClearCartCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var reloaded = await context.Carts.Include(c => c.Items).FirstAsync(c => c.Id == cart.Id);
        reloaded.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task ClearCart_WhenNoCartExists_IsIdempotentSuccess()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, _) = await SeedPrerequisitesAsync(context);
        var authMock = CreateCurrentUserMock(user.Id);

        var handler = new ClearCartCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new ClearCartCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    // 6. Validation Tests
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100)]
    public void AddCartItemValidator_InvalidQuantity_FailsValidation(int quantity)
    {
        var validator = new AddCartItemCommandValidator();
        var result = validator.Validate(new AddCartItemCommand(Guid.NewGuid(), quantity));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void AddCartItemValidator_EmptyProductId_FailsValidation()
    {
        var validator = new AddCartItemCommandValidator();
        var result = validator.Validate(new AddCartItemCommand(Guid.Empty, 1));
        result.IsValid.Should().BeFalse();
    }
}
