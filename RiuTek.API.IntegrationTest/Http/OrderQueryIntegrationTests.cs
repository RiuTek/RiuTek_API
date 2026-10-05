using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RiuTek.API.IntegrationTest.Infrastructure;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Models;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Data;
using Xunit;

namespace RiuTek.API.IntegrationTest.Http;

[Collection(IntegrationTestCollection.Name)]
public class OrderQueryIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public OrderQueryIntegrationTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync() => await CleanupDataAsync();

    public async Task DisposeAsync() => await CleanupDataAsync();

    private async Task CleanupDataAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            await db.OrderItems.ExecuteDeleteAsync();
            await db.Orders.ExecuteDeleteAsync();
            await db.CartItems.ExecuteDeleteAsync();
            await db.Carts.ExecuteDeleteAsync();
            await db.UserAddresses.ExecuteDeleteAsync();
            await db.Products.ExecuteDeleteAsync();
            await db.Categories.ExecuteDeleteAsync();
            await db.Users.ExecuteDeleteAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private async Task<(HttpClient Client, User User)> CreateAuthenticatedUserAsync(bool isUserActive = true)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var jwtGenerator = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(
            email: $"query_user_{suffix}@riutek.test",
            passwordHash: "dummy_hashed_password",
            fullName: $"Query User {suffix}",
            role: UserRole.Customer,
            phoneNumber: "0901234567"
        )
        {
            IsActive = isUserActive
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var token = jwtGenerator.GenerateAccessToken(user);
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return (client, user);
    }

    private async Task<Product> SeedProductAsync(decimal price = 200_000m, int stockQuantity = 50)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = new Category(
            name: $"Category {suffix}",
            slug: $"category-{suffix}",
            componentType: ComponentType.Accessory
        );
        db.Categories.Add(category);

        var product = new Product(
            categoryId: category.Id,
            name: $"Product {suffix}",
            slug: $"product-{suffix}",
            sku: $"SKU-{suffix}",
            brand: "RiuTek",
            price: price,
            stockQuantity: stockQuantity,
            imageUrl: "https://riutek.test/product.png",
            componentType: ComponentType.Accessory,
            specifications: new AccessorySpecification { Details = "Test Specs" }
        );
        db.Products.Add(product);
        await db.SaveChangesAsync();

        return product;
    }

    private async Task<Order> SeedOrderAsync(
        Guid userId,
        OrderStatus status = OrderStatus.Confirmed,
        PaymentMethod paymentMethod = PaymentMethod.COD,
        string? notes = null,
        DateTime? createdAt = null,
        List<(Guid ProductId, string Name, string Sku, decimal Price, int Qty)>? items = null)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var order = new Order(
            orderNumber: $"ORD-PG-{suffix}",
            userId: userId,
            checkoutIdempotencyKey: $"key-{Guid.NewGuid():N}",
            customerName: $"Customer {suffix}",
            customerEmail: $"customer_{suffix}@test.com",
            customerPhone: "0901234567",
            shippingAddress: $"123 Street {suffix}, District 1, HCMC",
            paymentMethod: paymentMethod,
            notes: notes
        );

        if (status != order.Status)
        {
            typeof(Order).GetProperty(nameof(Order.Status))!.SetValue(order, status);
        }

        if (createdAt.HasValue)
        {
            typeof(BaseEntity).GetProperty(nameof(BaseEntity.CreatedAt))!.SetValue(order, createdAt.Value);
        }

        if (items != null)
        {
            foreach (var item in items)
            {
                order.AddItem(item.ProductId, item.Name, item.Sku, item.Price, item.Qty);
            }
        }
        else
        {
            var category = new Category($"Cat {suffix}", $"cat-{suffix}", ComponentType.Accessory);
            db.Categories.Add(category);
            var prod = new Product(
                category.Id,
                $"Default Item {suffix}",
                $"def-{suffix}",
                $"SKU-{suffix}",
                "Brand",
                100_000m,
                50,
                "https://riutek.test/p.png",
                ComponentType.Accessory,
                new AccessorySpecification { Details = "Specs" }
            );
            db.Products.Add(prod);
            order.AddItem(prod.Id, prod.Name, prod.Sku, prod.Price, 1);
        }

        db.Orders.Add(order);
        await db.SaveChangesAsync();

        return order;
    }

    [Fact]
    public async Task GetMyOrders_And_GetMyOrderById_WhenAnonymous_Return401Unauthorized()
    {
        var client = _fixture.Factory.CreateClient();

        // 1. List
        var listResponse = await client.GetAsync("/api/v1/orders");
        listResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 2. Detail
        var detailResponse = await client.GetAsync($"/api/v1/orders/{Guid.NewGuid()}");
        detailResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMyOrders_UserAOnlySeesOwnOrders_CannotSeeUserBOrders()
    {
        var (clientA, userA) = await CreateAuthenticatedUserAsync();
        var (_, userB) = await CreateAuthenticatedUserAsync();

        var orderA1 = await SeedOrderAsync(userA.Id);
        var orderA2 = await SeedOrderAsync(userA.Id);
        var orderB = await SeedOrderAsync(userB.Id);

        var response = await clientA.GetAsync("/api/v1/orders");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var pagedResult = await response.Content.ReadFromJsonAsync<PagedResult<OrderSummaryDto>>(JsonOptions);
        pagedResult.Should().NotBeNull();
        pagedResult!.TotalCount.Should().Be(2);
        pagedResult.Items.Should().HaveCount(2);

        var returnedIds = pagedResult.Items.Select(x => x.Id).ToList();
        returnedIds.Should().Contain([orderA1.Id, orderA2.Id]);
        returnedIds.Should().NotContain(orderB.Id);
    }

    [Fact]
    public async Task GetMyOrders_StatusFilter_And_StableOrdering_And_PagedMetadata()
    {
        var (client, user) = await CreateAuthenticatedUserAsync();

        var baseTime = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);

        // Seed 3 Confirmed orders and 2 PendingPayment orders with deterministic timestamps
        var orderC1 = await SeedOrderAsync(user.Id, status: OrderStatus.Confirmed, createdAt: baseTime.AddHours(1));
        var orderC2 = await SeedOrderAsync(user.Id, status: OrderStatus.Confirmed, createdAt: baseTime.AddHours(2));
        var orderC3 = await SeedOrderAsync(user.Id, status: OrderStatus.Confirmed, createdAt: baseTime.AddHours(3));
        var orderP1 = await SeedOrderAsync(user.Id, status: OrderStatus.PendingPayment, createdAt: baseTime.AddHours(4));
        var orderP2 = await SeedOrderAsync(user.Id, status: OrderStatus.PendingPayment, createdAt: baseTime.AddHours(5));

        // 1. Filter status=Confirmed, pageIndex=1, pageSize=2
        var responsePage1 = await client.GetAsync("/api/v1/orders?status=Confirmed&pageIndex=1&pageSize=2");
        responsePage1.StatusCode.Should().Be(HttpStatusCode.OK);

        var page1 = await responsePage1.Content.ReadFromJsonAsync<PagedResult<OrderSummaryDto>>(JsonOptions);
        page1.Should().NotBeNull();
        page1!.TotalCount.Should().Be(3);
        page1.TotalPages.Should().Be(2);
        page1.PageIndex.Should().Be(1);
        page1.PageSize.Should().Be(2);
        page1.Items.Should().HaveCount(2);
        page1.Items.Should().OnlyContain(x => x.Status == OrderStatus.Confirmed);

        // Stable ordering: CreatedAt DESC -> orderC3 (13:00) then orderC2 (12:00)
        page1.Items[0].Id.Should().Be(orderC3.Id);
        page1.Items[1].Id.Should().Be(orderC2.Id);

        // 2. Filter status=Confirmed, pageIndex=2, pageSize=2
        var responsePage2 = await client.GetAsync("/api/v1/orders?status=Confirmed&pageIndex=2&pageSize=2");
        responsePage2.StatusCode.Should().Be(HttpStatusCode.OK);

        var page2 = await responsePage2.Content.ReadFromJsonAsync<PagedResult<OrderSummaryDto>>(JsonOptions);
        page2.Should().NotBeNull();
        page2!.Items.Should().HaveCount(1);
        page2.Items[0].Id.Should().Be(orderC1.Id);

        // 3. Beyond last page -> empty items, metadata intact
        var responsePage10 = await client.GetAsync("/api/v1/orders?status=Confirmed&pageIndex=10&pageSize=2");
        responsePage10.StatusCode.Should().Be(HttpStatusCode.OK);

        var page10 = await responsePage10.Content.ReadFromJsonAsync<PagedResult<OrderSummaryDto>>(JsonOptions);
        page10.Should().NotBeNull();
        page10!.Items.Should().BeEmpty();
        page10.TotalCount.Should().Be(3);
        page10.TotalPages.Should().Be(2);
        page10.PageIndex.Should().Be(10);
    }

    [Fact]
    public async Task GetMyOrderById_WhenOrderBelongsToOtherUser_Returns404NotFound()
    {
        var (clientA, _) = await CreateAuthenticatedUserAsync();
        var (_, userB) = await CreateAuthenticatedUserAsync();

        var orderB = await SeedOrderAsync(userB.Id);

        // User A tries to access User B's order
        var response = await clientA.GetAsync($"/api/v1/orders/{orderB.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Order.NotFound");

        // Non-existent order id also returns 404 with exact same error code
        var responseNonExistent = await clientA.GetAsync($"/api/v1/orders/{Guid.NewGuid()}");
        responseNonExistent.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var bodyNonExistent = await responseNonExistent.Content.ReadAsStringAsync();
        bodyNonExistent.Should().Contain("Order.NotFound");
    }

    [Fact]
    public async Task GetMyOrderById_ReturnsAccurateSnapshot_WithoutInternalOrSensitiveFields_EvenIfProductChanged()
    {
        var (client, user) = await CreateAuthenticatedUserAsync();
        var product = await SeedProductAsync(price: 300_000m, stockQuantity: 20);
        var product2 = await SeedProductAsync(price: 50_000m, stockQuantity: 10);

        var items = new List<(Guid ProductId, string Name, string Sku, decimal Price, int Qty)>
        {
            (product.Id, "Snapshot Gaming Mouse", "MOUSE-01", 300_000m, 2),
            (product2.Id, "Snapshot Mousepad", "PAD-01", 50_000m, 1)
        };

        var order = await SeedOrderAsync(
            user.Id,
            status: OrderStatus.Confirmed,
            paymentMethod: PaymentMethod.COD,
            notes: "Special delivery instructions",
            items: items
        );

        // Modify the product in PostgreSQL catalog after order creation
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var p = await db.Products.FirstAsync(x => x.Id == product.Id);
            p.Name = "Completely Altered Name";
            p.Price = 9_999_999m;
            p.Sku = "ALTERED-SKU";
            p.IsActive = false;
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/v1/orders/{order.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var rawJson = await response.Content.ReadAsStringAsync();

        // 1. Verify JSON does NOT contain sensitive / internal fields
        rawJson.Should().NotContain("checkoutIdempotencyKey", "checkoutIdempotencyKey must not be exposed");
        rawJson.Should().NotContain("paymentAttempts", "paymentAttempts must not be exposed");
        rawJson.Should().NotContain("user", "user navigation entity must not be exposed");

        // 2. Parse DTO and verify accurate snapshot
        var detail = JsonSerializer.Deserialize<OrderDetailDto>(rawJson, JsonOptions);
        detail.Should().NotBeNull();
        detail!.Id.Should().Be(order.Id);
        detail.OrderNumber.Should().Be(order.OrderNumber);
        detail.Status.Should().Be(OrderStatus.Confirmed);
        detail.PaymentMethod.Should().Be(PaymentMethod.COD);
        detail.PaymentStatus.Should().Be(PaymentStatus.Pending);
        detail.Currency.Should().Be("VND");
        detail.CustomerName.Should().Be(order.CustomerName);
        detail.CustomerEmail.Should().Be(order.CustomerEmail);
        detail.CustomerPhone.Should().Be(order.CustomerPhone);
        detail.ShippingAddress.Should().Be(order.ShippingAddress);
        detail.Notes.Should().Be("Special delivery instructions");
        detail.TotalAmount.Should().Be(650_000m);
        detail.DiscountAmount.Should().Be(0m);
        detail.FinalAmount.Should().Be(650_000m);

        // 3. Items snapshot intact despite product mutation
        detail.Items.Should().HaveCount(2);
        var mouseItem = detail.Items.First(i => i.ProductId == product.Id);
        mouseItem.ProductName.Should().Be("Snapshot Gaming Mouse", "Historical snapshot productName must be preserved");
        mouseItem.ProductSku.Should().Be("MOUSE-01", "Historical snapshot productSku must be preserved");
        mouseItem.UnitPrice.Should().Be(300_000m, "Historical snapshot unitPrice must be preserved");
        mouseItem.Quantity.Should().Be(2);
        mouseItem.TotalPrice.Should().Be(600_000m);
    }

    [Fact]
    public async Task GetMyOrders_ItemCountMatchesOrderItems_WithoutExposingItemDetailsInList()
    {
        var (client, user) = await CreateAuthenticatedUserAsync();

        var prodA = await SeedProductAsync(price: 100_000m);
        var prodB = await SeedProductAsync(price: 200_000m);
        var prodC = await SeedProductAsync(price: 300_000m);

        var items = new List<(Guid ProductId, string Name, string Sku, decimal Price, int Qty)>
        {
            (prodA.Id, "Item A", "SKU-A", 100_000m, 1),
            (prodB.Id, "Item B", "SKU-B", 200_000m, 2),
            (prodC.Id, "Item C", "SKU-C", 300_000m, 1)
        };

        var order = await SeedOrderAsync(user.Id, items: items);

        var response = await client.GetAsync("/api/v1/orders");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var rawJson = await response.Content.ReadAsStringAsync();

        // Verify list JSON does NOT expose item details or address
        using var doc = JsonDocument.Parse(rawJson);
        var itemsElement = doc.RootElement.GetProperty("items");
        itemsElement.GetArrayLength().Should().Be(1);

        var firstSummary = itemsElement[0];
        firstSummary.GetProperty("itemCount").GetInt32().Should().Be(3);
        firstSummary.TryGetProperty("items", out _).Should().BeFalse("OrderSummary must not include items list");
        firstSummary.TryGetProperty("shippingAddress", out _).Should().BeFalse("OrderSummary must not include shippingAddress");
        firstSummary.TryGetProperty("customerPhone", out _).Should().BeFalse("OrderSummary must not include customerPhone");
        firstSummary.TryGetProperty("notes", out _).Should().BeFalse("OrderSummary must not include notes");
        firstSummary.TryGetProperty("checkoutIdempotencyKey", out _).Should().BeFalse("OrderSummary must not include idempotency key");
    }

    [Theory]
    [InlineData("/api/v1/orders?pageIndex=0", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/orders?pageSize=0", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/orders?pageSize=100", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/orders?status=InvalidEnumValue", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/orders/00000000-0000-0000-0000-000000000000", HttpStatusCode.BadRequest)]
    public async Task OrdersEndpoints_WhenInputInvalid_Returns400BadRequest(string uri, HttpStatusCode expectedStatus)
    {
        var (client, _) = await CreateAuthenticatedUserAsync();

        var response = await client.GetAsync(uri);
        response.StatusCode.Should().Be(expectedStatus);
    }
}
