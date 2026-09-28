using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Data;

namespace RiuTek.API.IntegrationTest.Infrastructure;

[Collection(IntegrationTestCollection.Name)]
public class OrderPaymentPersistenceIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture;

    public OrderPaymentPersistenceIntegrationTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => CleanupAsync();

    public Task DisposeAsync() => CleanupAsync();

    [Fact]
    public async Task OrderAggregate_WithItemAndPaymentAttempt_RoundTripsThroughPostgreSql()
    {
        var now = new DateTime(2026, 9, 28, 8, 0, 0, DateTimeKind.Utc);
        Guid orderId;

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var (user, product) = await SeedPrerequisitesAsync(db);
            var order = CreateOrder(user.Id, "checkout-roundtrip", PaymentMethod.Stripe);
            order.AddItem(product.Id, product.Name, product.Sku, product.Price, 2);
            var paymentResult = order.CreatePaymentAttempt("payment-roundtrip", now.AddMinutes(15), now);

            paymentResult.IsSuccess.Should().BeTrue();
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
        }

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var persisted = await db.Orders
                .AsNoTracking()
                .Include(order => order.Items)
                .Include(order => order.PaymentAttempts)
                .SingleAsync(order => order.Id == orderId);

            persisted.Items.Should().ContainSingle();
            persisted.Items.Single().TotalPrice.Should().Be(500_000m);
            persisted.PaymentAttempts.Should().ContainSingle();
            persisted.PaymentAttempts.Single().Should().Match<PaymentAttempt>(attempt =>
                attempt.Method == PaymentMethod.Stripe &&
                attempt.Amount == 500_000m &&
                attempt.Status == PaymentAttemptStatus.Pending &&
                attempt.ExpiresAt == now.AddMinutes(15));
        }
    }

    [Fact]
    public async Task CheckoutIdempotencyKey_IsUniquePerUserButReusableByAnotherUser()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (firstUser, product) = await SeedPrerequisitesAsync(db);
        var secondUser = new User(
            $"order_second_{Guid.NewGuid():N}@riutek.test",
            "dummy_hashed_password",
            "Second Order User",
            UserRole.Customer);
        db.Users.Add(secondUser);
        await db.SaveChangesAsync();

        var first = CreatePayableOrder(firstUser.Id, product, "same-checkout-key");
        var otherUser = CreatePayableOrder(secondUser.Id, product, "same-checkout-key");
        db.Orders.AddRange(first, otherUser);
        await db.SaveChangesAsync();

        var duplicate = CreatePayableOrder(firstUser.Id, product, "same-checkout-key");
        db.Orders.Add(duplicate);

        var act = async () => await db.SaveChangesAsync();

        var exception = await act.Should().ThrowAsync<DbUpdateException>();
        db.IsUniqueViolation(
                exception.Which,
                "UX_Orders_UserId_CheckoutIdempotencyKey")
            .Should().BeTrue();
    }

    private async Task CleanupAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await db.PaymentAttempts.ExecuteDeleteAsync();
        await db.OrderItems.ExecuteDeleteAsync();
        await db.Orders.ExecuteDeleteAsync();
        await db.CartItems.ExecuteDeleteAsync();
        await db.Carts.ExecuteDeleteAsync();
        await db.Products.ExecuteDeleteAsync();
        await db.Categories.ExecuteDeleteAsync();
        await db.Users.ExecuteDeleteAsync();
    }

    private static async Task<(User User, Product Product)> SeedPrerequisitesAsync(ApplicationDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(
            $"order_{suffix}@riutek.test",
            "dummy_hashed_password",
            $"Order User {suffix}",
            UserRole.Customer);
        var category = new Category(
            $"Order Category {suffix}",
            $"order-category-{suffix}",
            ComponentType.Accessory,
            "Order integration test category");
        var product = new Product(
            category.Id,
            $"Order Product {suffix}",
            $"order-product-{suffix}",
            $"ORDER-SKU-{suffix}",
            "RiuTek",
            250_000m,
            10,
            "https://riutek.test/order-product.png",
            ComponentType.Accessory,
            new AccessorySpecification { Details = "Order persistence test" });

        db.AddRange(user, category, product);
        await db.SaveChangesAsync();
        return (user, product);
    }

    private static Order CreatePayableOrder(Guid userId, Product product, string idempotencyKey)
    {
        var order = CreateOrder(userId, idempotencyKey, PaymentMethod.COD);
        order.AddItem(product.Id, product.Name, product.Sku, product.Price);
        return order;
    }

    private static Order CreateOrder(Guid userId, string idempotencyKey, PaymentMethod method) => new(
        $"ORD-{Guid.NewGuid():N}",
        userId,
        idempotencyKey,
        "Nguyen Van A",
        "customer@riutek.test",
        "0900000000",
        "Ho Chi Minh City",
        method);
}
