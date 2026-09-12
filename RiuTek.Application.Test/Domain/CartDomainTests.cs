using System.Reflection;
using FluentAssertions;
using RiuTek.Core.Entities;
using Xunit;

namespace RiuTek.Application.Test.Domain;

public class CartDomainTests
{
    [Fact]
    public void Cart_WhenUserIdIsEmpty_ThrowsArgumentException()
    {
        var act = () => new Cart(Guid.Empty);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*UserId*");
    }

    [Fact]
    public void Cart_WhenCreated_StartsEmptyWithValidVersionAndTimestamps()
    {
        var now = new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);
        var userId = Guid.NewGuid();

        var cart = new Cart(userId, now);

        cart.UserId.Should().Be(userId);
        cart.Version.Should().Be(1);
        cart.CreatedAt.Should().Be(now);
        cart.UpdatedAt.Should().BeNull();
        cart.Items.Should().BeEmpty();
    }

    [Fact]
    public void AddItem_WhenNewProduct_AddsItemSuccessfully()
    {
        var cart = new Cart(Guid.NewGuid());
        var productId = Guid.NewGuid();

        var result = cart.AddItem(productId, 3);

        result.IsSuccess.Should().BeTrue();
        cart.Items.Should().HaveCount(1);
        var item = cart.Items.Single();
        item.CartId.Should().Be(cart.Id);
        item.ProductId.Should().Be(productId);
        item.Quantity.Should().Be(3);
    }

    [Fact]
    public void AddItem_WhenSameProduct_IncreasesQuantityWithoutDuplicate()
    {
        var cart = new Cart(Guid.NewGuid());
        var productId = Guid.NewGuid();

        cart.AddItem(productId, 5);
        var result = cart.AddItem(productId, 10);

        result.IsSuccess.Should().BeTrue();
        cart.Items.Should().HaveCount(1);
        cart.Items.Single().Quantity.Should().Be(15);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(99)]
    public void AddItem_And_SetItemQuantity_AtBoundariesOneAndNinetyNine_Succeeds(int boundaryQty)
    {
        var cart = new Cart(Guid.NewGuid());
        var productId = Guid.NewGuid();

        var addResult = cart.AddItem(productId, boundaryQty);
        addResult.IsSuccess.Should().BeTrue();
        cart.Items.Single().Quantity.Should().Be(boundaryQty);

        var setResult = cart.SetItemQuantity(productId, boundaryQty == 1 ? 99 : 1);
        setResult.IsSuccess.Should().BeTrue();
        cart.Items.Single().Quantity.Should().Be(boundaryQty == 1 ? 99 : 1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-50)]
    [InlineData(100)]
    [InlineData(999)]
    public void AddItem_WhenQuantityIsZeroNegativeOrGreaterThanNinetyNine_FailsWithValidationError(int invalidQuantity)
    {
        var cart = new Cart(Guid.NewGuid());
        var productId = Guid.NewGuid();

        var result = cart.AddItem(productId, invalidQuantity);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cart.InvalidQuantity");
        cart.Items.Should().BeEmpty();
    }

    [Fact]
    public void AddItem_WhenTotalQuantityExceedsNinetyNine_FailsAndLeavesStateUntouched()
    {
        var initialTime = new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);
        var cart = new Cart(Guid.NewGuid(), initialTime);
        var productId = Guid.NewGuid();

        cart.AddItem(productId, 90, initialTime);
        var initialVersion = cart.Version;
        var initialUpdatedAt = cart.UpdatedAt;

        var mutateTime = initialTime.AddMinutes(5);
        var result = cart.AddItem(productId, 10, mutateTime); // 90 + 10 = 100 > 99

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cart.MaxQuantityExceeded");
        cart.Items.Single().Quantity.Should().Be(90);
        cart.Version.Should().Be(initialVersion);
        cart.UpdatedAt.Should().Be(initialUpdatedAt);
    }

    [Fact]
    public void AddItem_WhenProductIdIsEmpty_FailsWithValidationError()
    {
        var cart = new Cart(Guid.NewGuid());

        var result = cart.AddItem(Guid.Empty, 5);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cart.InvalidProductId");
        cart.Items.Should().BeEmpty();
    }

    [Fact]
    public void AddItem_WhenDistinctItemsReachFifty_FiftyFirstItemIsRejected()
    {
        var cart = new Cart(Guid.NewGuid());

        for (var i = 0; i < 50; i++)
        {
            var addResult = cart.AddItem(Guid.NewGuid(), 1);
            addResult.IsSuccess.Should().BeTrue();
        }

        cart.Items.Should().HaveCount(50);
        var versionAtFifty = cart.Version;
        var updatedAtAtFifty = cart.UpdatedAt;

        var overflowResult = cart.AddItem(Guid.NewGuid(), 1);

        overflowResult.IsFailure.Should().BeTrue();
        overflowResult.Error.Code.Should().Be("Cart.MaxDistinctItemsExceeded");
        cart.Items.Should().HaveCount(50);
        cart.Version.Should().Be(versionAtFifty);
        cart.UpdatedAt.Should().Be(updatedAtAtFifty);
    }

    [Fact]
    public void RemoveItem_And_Clear_UpdatesStateCorrectly_OrReturnsNotFound()
    {
        var cart = new Cart(Guid.NewGuid());
        var prod1 = Guid.NewGuid();
        var prod2 = Guid.NewGuid();
        var missingProd = Guid.NewGuid();

        cart.AddItem(prod1, 2);
        cart.AddItem(prod2, 3);

        // Removing non-existent item returns NotFound
        var missingResult = cart.RemoveItem(missingProd);
        missingResult.IsFailure.Should().BeTrue();
        missingResult.Error.Code.Should().Be("Cart.ItemNotFound");
        cart.Items.Should().HaveCount(2);

        // Removing existing item succeeds
        var removeResult = cart.RemoveItem(prod1);
        removeResult.IsSuccess.Should().BeTrue();
        cart.Items.Should().HaveCount(1);
        cart.Items.Single().ProductId.Should().Be(prod2);

        // Clear empties the cart
        var clearResult = cart.Clear();
        clearResult.IsSuccess.Should().BeTrue();
        cart.Items.Should().BeEmpty();

        // Clear on already empty cart is a no-op
        var versionBefore = cart.Version;
        var secondClear = cart.Clear();
        secondClear.IsSuccess.Should().BeTrue();
        cart.Version.Should().Be(versionBefore);
    }

    [Fact]
    public void SuccessfulMutation_UpdatesUpdatedAtAndIncrementsVersionExactlyOnce()
    {
        var t0 = new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);
        var cart = new Cart(Guid.NewGuid(), t0);

        cart.Version.Should().Be(1);
        cart.UpdatedAt.Should().BeNull();

        var t1 = t0.AddMinutes(1);
        var p1 = Guid.NewGuid();
        cart.AddItem(p1, 2, t1);
        cart.Version.Should().Be(2);
        cart.UpdatedAt.Should().Be(t1);

        var t2 = t1.AddMinutes(1);
        cart.SetItemQuantity(p1, 5, t2);
        cart.Version.Should().Be(3);
        cart.UpdatedAt.Should().Be(t2);

        var t3 = t2.AddMinutes(1);
        cart.RemoveItem(p1, t3);
        cart.Version.Should().Be(4);
        cart.UpdatedAt.Should().Be(t3);
    }

    [Fact]
    public void FailedOrNoOpMutation_DoesNotChangeTimestampOrVersion()
    {
        var t0 = new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);
        var cart = new Cart(Guid.NewGuid(), t0);
        var prod = Guid.NewGuid();

        cart.AddItem(prod, 5, t0);
        var version = cart.Version;
        var updatedAt = cart.UpdatedAt;

        var tLater = t0.AddHours(1);

        // 1. Invalid ProductId
        cart.AddItem(Guid.Empty, 1, tLater);
        cart.Version.Should().Be(version);
        cart.UpdatedAt.Should().Be(updatedAt);

        // 2. Invalid Quantity
        cart.AddItem(prod, -1, tLater);
        cart.Version.Should().Be(version);
        cart.UpdatedAt.Should().Be(updatedAt);

        // 3. SetItemQuantity with same quantity (no-op)
        cart.SetItemQuantity(prod, 5, tLater);
        cart.Version.Should().Be(version);
        cart.UpdatedAt.Should().Be(updatedAt);

        // 4. Remove missing item
        cart.RemoveItem(Guid.NewGuid(), tLater);
        cart.Version.Should().Be(version);
        cart.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void CartItem_DoesNotContainProductSnapshotFields()
    {
        var properties = typeof(CartItem).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        // Must not contain any snapshot properties from Product
        properties.Should().NotContain("Price");
        properties.Should().NotContain("OriginalPrice");
        properties.Should().NotContain("UnitPrice");
        properties.Should().NotContain("ProductName");
        properties.Should().NotContain("Name");
        properties.Should().NotContain("Slug");
        properties.Should().NotContain("Sku");
        properties.Should().NotContain("ImageUrl");
        properties.Should().NotContain("StockQuantity");
        properties.Should().NotContain("IsActive");
        properties.Should().NotContain("Subtotal");
        properties.Should().NotContain("Total");

        // Should strictly contain entity identity and relational navigations only
        properties.Should().BeEquivalentTo([
            "Id",
            "CreatedAt",
            "UpdatedAt",
            "CartId",
            "ProductId",
            "Quantity",
            "Cart",
            "Product"
        ]);
    }
}
