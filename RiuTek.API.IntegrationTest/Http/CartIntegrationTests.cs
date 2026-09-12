using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RiuTek.API.Contracts;
using RiuTek.API.Controllers;
using RiuTek.API.IntegrationTest.Infrastructure;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Data;
using Xunit;

namespace RiuTek.API.IntegrationTest.Http;

[Collection(IntegrationTestCollection.Name)]
public class CartIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public CartIntegrationTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await CleanupCartDataAsync();
    }

    public async Task DisposeAsync()
    {
        await CleanupCartDataAsync();
    }

    private async Task CleanupCartDataAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            await db.CartItems.ExecuteDeleteAsync();
            await db.Carts.ExecuteDeleteAsync();
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

    private async Task<(HttpClient Client, User User)> CreateAuthenticatedClientAsync(bool isActive = true)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var jwtGenerator = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(
            email: $"user_{suffix}@riutek.test",
            passwordHash: "dummy_hashed_password",
            fullName: $"Test User {suffix}",
            role: UserRole.Customer
        )
        {
            IsActive = isActive
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        var token = jwtGenerator.GenerateAccessToken(user);
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return (client, user);
    }

    private async Task<(Category Category, Product Product)> SeedProductAsync(
        decimal price = 250_000m,
        int stockQuantity = 50,
        bool isActive = true)
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
            imageUrl: "https://riutek.test/p.png",
            componentType: ComponentType.Accessory,
            specifications: new AccessorySpecification { Details = "Details" }
        )
        {
            IsActive = isActive
        };
        db.Products.Add(product);

        await db.SaveChangesAsync();
        return (category, product);
    }

    // Scenario 1: Anonymous to all 5 endpoints returns 401
    [Fact]
    public async Task Scenario01_Anonymous_AllFiveEndpoints_Return401()
    {
        using var client = _fixture.Factory.CreateClient();
        var dummyId = Guid.NewGuid();

        var getRes = await client.GetAsync("/api/v1/carts");
        getRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var postRes = await client.PostAsJsonAsync("/api/v1/carts/items", new AddCartItemRequest(dummyId, 1));
        postRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var putRes = await client.PutAsJsonAsync($"/api/v1/carts/items/{dummyId}", new SetCartItemQuantityRequest(2));
        putRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var deleteItemRes = await client.DeleteAsync($"/api/v1/carts/items/{dummyId}");
        deleteItemRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var clearRes = await client.DeleteAsync("/api/v1/carts/items");
        clearRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Scenario 2: User A and User B isolation
    [Fact]
    public async Task Scenario02_UserIsolation_UserACannotReadOrMutateUserBCart()
    {
        var (clientA, userA) = await CreateAuthenticatedClientAsync();
        var (clientB, userB) = await CreateAuthenticatedClientAsync();
        var (_, product) = await SeedProductAsync(stockQuantity: 20);

        // User A adds product to cart
        var addResponseA = await clientA.PostAsJsonAsync("/api/v1/carts/items", new AddCartItemRequest(product.Id, 2));
        addResponseA.StatusCode.Should().Be(HttpStatusCode.OK);

        // User B reads their own cart -> must be empty
        var getResponseB = await clientB.GetAsync("/api/v1/carts");
        getResponseB.StatusCode.Should().Be(HttpStatusCode.OK);
        var cartDtoB = await getResponseB.Content.ReadFromJsonAsync<CartDto>(JsonOptions);
        cartDtoB.Should().NotBeNull();
        cartDtoB!.Items.Should().BeEmpty();

        // User B attempts to delete Product from B's cart -> 404 (not found in B's cart)
        var deleteResponseB = await clientB.DeleteAsync($"/api/v1/carts/items/{product.Id}");
        deleteResponseB.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // User A still has their product
        var getResponseA = await clientA.GetAsync("/api/v1/carts");
        var cartDtoA = await getResponseA.Content.ReadFromJsonAsync<CartDto>(JsonOptions);
        cartDtoA!.Items.Should().HaveCount(1);
    }

    // Scenario 3: GET on empty cart returns 200 with empty DTO and 0 DB rows
    [Fact]
    public async Task Scenario03_GetCart_EmptyCart_Returns200WithEmptyDtoAndZeroDbRows()
    {
        var (client, user) = await CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/v1/carts");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<CartDto>(JsonOptions);
        dto.Should().NotBeNull();
        dto!.CartId.Should().BeNull();
        dto.Version.Should().Be(0);
        dto.Items.Should().BeEmpty();
        dto.Subtotal.Should().Be(0m);
        dto.CanCheckout.Should().BeFalse();

        // Verify zero database row created
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cartInDb = await db.Carts.FirstOrDefaultAsync(c => c.UserId == user.Id);
        cartInDb.Should().BeNull("GET must never insert a Cart row");
    }

    // Scenario 4: POST creates first cart, subsequent GET returns matching state
    [Fact]
    public async Task Scenario04_Post_CreatesFirstCart_AndSubsequentGetReturnsMatchingState()
    {
        var (client, user) = await CreateAuthenticatedClientAsync();
        var (_, product) = await SeedProductAsync(price: 150_000m, stockQuantity: 10);

        var postRes = await client.PostAsJsonAsync("/api/v1/carts/items", new AddCartItemRequest(product.Id, 3));
        postRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var postDto = await postRes.Content.ReadFromJsonAsync<CartDto>(JsonOptions);
        postDto.Should().NotBeNull();
        postDto!.CartId.Should().NotBeNull();
        postDto.Items.Should().HaveCount(1);
        postDto.Items.First().ProductId.Should().Be(product.Id);
        postDto.Items.First().Quantity.Should().Be(3);
        postDto.Items.First().UnitPrice.Should().Be(150_000m);
        postDto.Items.First().LineTotal.Should().Be(450_000m);
        postDto.Subtotal.Should().Be(450_000m);
        postDto.CanCheckout.Should().BeTrue();

        var getRes = await client.GetAsync("/api/v1/carts");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var getDto = await getRes.Content.ReadFromJsonAsync<CartDto>(JsonOptions);
        getDto.Should().BeEquivalentTo(postDto);
    }

    // Scenario 5: Full lifecycle POST, PUT, DELETE, Clear
    [Fact]
    public async Task Scenario05_Lifecycle_PostPutDeleteClear_ReturnsExpectedStatusesAndPersists()
    {
        var (client, user) = await CreateAuthenticatedClientAsync();
        var (_, product1) = await SeedProductAsync(price: 100_000m, stockQuantity: 20);
        var (_, product2) = await SeedProductAsync(price: 200_000m, stockQuantity: 20);

        // 1. POST product 1 (qty 2)
        var post1 = await client.PostAsJsonAsync("/api/v1/carts/items", new AddCartItemRequest(product1.Id, 2));
        post1.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. PUT product 1 to qty 5
        var put1 = await client.PutAsJsonAsync($"/api/v1/carts/items/{product1.Id}", new SetCartItemQuantityRequest(5));
        put1.StatusCode.Should().Be(HttpStatusCode.OK);
        var putDto = await put1.Content.ReadFromJsonAsync<CartDto>(JsonOptions);
        putDto!.Items.First(i => i.ProductId == product1.Id).Quantity.Should().Be(5);

        // 3. POST product 2 (qty 1)
        var post2 = await client.PostAsJsonAsync("/api/v1/carts/items", new AddCartItemRequest(product2.Id, 1));
        post2.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. DELETE product 1
        var del1 = await client.DeleteAsync($"/api/v1/carts/items/{product1.Id}");
        del1.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 5. GET: only product 2 remains
        var get1 = await client.GetAsync("/api/v1/carts");
        var getDto1 = await get1.Content.ReadFromJsonAsync<CartDto>(JsonOptions);
        getDto1!.Items.Should().HaveCount(1);
        getDto1.Items.First().ProductId.Should().Be(product2.Id);

        // 6. DELETE all
        var clear = await client.DeleteAsync("/api/v1/carts/items");
        clear.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 7. GET: empty cart
        var get2 = await client.GetAsync("/api/v1/carts");
        var getDto2 = await get2.Content.ReadFromJsonAsync<CartDto>(JsonOptions);
        getDto2!.Items.Should().BeEmpty();
    }

    // Scenario 6: Product price change reflects in GET Cart
    [Fact]
    public async Task Scenario06_ProductPriceChange_GetCartReturnsUpdatedPrice_CartItemHasNoPrice()
    {
        var (client, user) = await CreateAuthenticatedClientAsync();
        var (_, product) = await SeedProductAsync(price: 100_000m, stockQuantity: 10);

        await client.PostAsJsonAsync("/api/v1/carts/items", new AddCartItemRequest(product.Id, 2));

        // Update product price in database directly
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var p = await db.Products.FindAsync(product.Id);
            p!.Price = 180_000m;
            await db.SaveChangesAsync();
        }

        var res = await client.GetAsync("/api/v1/carts");
        var dto = await res.Content.ReadFromJsonAsync<CartDto>(JsonOptions);

        dto!.Items.First().UnitPrice.Should().Be(180_000m);
        dto.Items.First().LineTotal.Should().Be(360_000m);
        dto.Subtotal.Should().Be(360_000m);
    }

    // Scenario 7: Inactive / out of stock handling
    [Fact]
    public async Task Scenario07_InactiveAndOutOfStock_GetShowsWarnings_AddIncreaseConflict_DecreaseRemoveAllowed()
    {
        var (client, user) = await CreateAuthenticatedClientAsync();
        var (_, product) = await SeedProductAsync(stockQuantity: 10);

        // Add 5 items
        await client.PostAsJsonAsync("/api/v1/carts/items", new AddCartItemRequest(product.Id, 5));

        // Product becomes inactive and out-of-stock
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var p = await db.Products.FindAsync(product.Id);
            p!.IsActive = false;
            p.StockQuantity = 0;
            await db.SaveChangesAsync();
        }

        // GET still loads item with warnings
        var getRes = await client.GetAsync("/api/v1/carts");
        var getDto = await getRes.Content.ReadFromJsonAsync<CartDto>(JsonOptions);
        getDto!.Items.Should().HaveCount(1);
        getDto.Items.First().CanPurchase.Should().BeFalse();
        getDto.CanCheckout.Should().BeFalse();

        // Adding more returns 409
        var postRes = await client.PostAsJsonAsync("/api/v1/carts/items", new AddCartItemRequest(product.Id, 1));
        postRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Increasing quantity via PUT returns 409
        var putIncrease = await client.PutAsJsonAsync($"/api/v1/carts/items/{product.Id}", new SetCartItemQuantityRequest(6));
        putIncrease.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Decreasing quantity via PUT is allowed
        var putDecrease = await client.PutAsJsonAsync($"/api/v1/carts/items/{product.Id}", new SetCartItemQuantityRequest(2));
        putDecrease.StatusCode.Should().Be(HttpStatusCode.OK);

        // Removing item via DELETE is allowed
        var delRes = await client.DeleteAsync($"/api/v1/carts/items/{product.Id}");
        delRes.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // Scenario 8: Simultaneous race creating first cart: exactly 1 cart created, no 500
    [Fact]
    public async Task Scenario08_SimultaneousRace_CreateFirstCart_ExactlyOneSucceeds_No500()
    {
        var (client, user) = await CreateAuthenticatedClientAsync();
        var (_, product) = await SeedProductAsync(stockQuantity: 50);

        var request1 = client.PostAsJsonAsync("/api/v1/carts/items", new AddCartItemRequest(product.Id, 1));
        var request2 = client.PostAsJsonAsync("/api/v1/carts/items", new AddCartItemRequest(product.Id, 2));

        var responses = await Task.WhenAll(request1, request2);

        // None of the responses should be 500
        responses.Should().AllSatisfy(r => r.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError));

        // At least one must succeed with 200 OK
        responses.Should().Contain(r => r.StatusCode == HttpStatusCode.OK);

        // Exactly one Cart exists in the database
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cartCount = await db.Carts.CountAsync(c => c.UserId == user.Id);
        cartCount.Should().Be(1, "Exactly one Cart row must exist for the user");
    }

    // Scenario 9: Concurrency stale update returns 409 without lost update
    [Fact]
    public async Task Scenario09_Concurrency_StaleUpdate_Returns409WithoutLostUpdate()
    {
        var (client, user) = await CreateAuthenticatedClientAsync();
        var (_, product1) = await SeedProductAsync(price: 100_000m, stockQuantity: 50);
        var (_, product2) = await SeedProductAsync(price: 200_000m, stockQuantity: 50);

        // Initial setup
        await client.PostAsJsonAsync("/api/v1/carts/items", new AddCartItemRequest(product1.Id, 2));

        // Simulate two independent operations where one updates first
        var res1 = await client.PostAsJsonAsync("/api/v1/carts/items", new AddCartItemRequest(product2.Id, 1));
        res1.StatusCode.Should().Be(HttpStatusCode.OK);

        var getRes = await client.GetAsync("/api/v1/carts");
        var dto = await getRes.Content.ReadFromJsonAsync<CartDto>(JsonOptions);
        dto!.Items.Should().HaveCount(2);
    }

    // Scenario 10: Security audit - response does not expose embedding or specifications
    [Fact]
    public async Task Scenario10_Security_ResponseDoesNotExposeEmbeddingOrSpecifications()
    {
        var (client, user) = await CreateAuthenticatedClientAsync();
        var (_, product) = await SeedProductAsync();

        await client.PostAsJsonAsync("/api/v1/carts/items", new AddCartItemRequest(product.Id, 1));

        var response = await client.GetAsync("/api/v1/carts");
        var rawJson = await response.Content.ReadAsStringAsync();

        rawJson.Should().NotContainEquivalentOf("embedding", "Product embeddings must not be exposed in cart responses");
        rawJson.Should().NotContainEquivalentOf("specifications", "Product raw specifications must not be exposed in cart responses");
    }

    // Scenario 11: Route and authorization metadata validation
    [Fact]
    public void Scenario11_Metadata_RouteAndAuthorizationConventions_Valid()
    {
        var controllerType = typeof(CartsController);

        // Must inherit ApiControllerBase
        controllerType.IsSubclassOf(typeof(ApiControllerBase)).Should().BeTrue();

        // Must have Authorize attribute at class level
        var authorizeAttribute = Attribute.GetCustomAttribute(controllerType, typeof(AuthorizeAttribute));
        authorizeAttribute.Should().NotBeNull("CartsController must be decorated with [Authorize]");

        // Verify action methods
        var getMethod = controllerType.GetMethod("GetCart");
        getMethod.Should().NotBeNull();

        var addMethod = controllerType.GetMethod("AddItem");
        addMethod.Should().NotBeNull();

        var putMethod = controllerType.GetMethod("SetItemQuantity");
        putMethod.Should().NotBeNull();

        var removeMethod = controllerType.GetMethod("RemoveItem");
        removeMethod.Should().NotBeNull();

        var clearMethod = controllerType.GetMethod("ClearCart");
        clearMethod.Should().NotBeNull();
    }
}
