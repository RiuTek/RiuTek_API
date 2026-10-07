using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RiuTek.API.Contracts;
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
public class UserIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public UserIntegrationTests(PostgreSqlContainerFixture fixture)
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
            imageUrl: "https://riutek.test/img.png",
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

    #region 1. Anonymous Access Tests (401 Unauthorized)

    [Fact]
    public async Task AnonymousRequests_ToAllUserEndpoints_Return401Unauthorized()
    {
        var anonymousClient = _fixture.Factory.CreateClient();
        var dummyGuid = Guid.NewGuid();

        // PUT /api/v1/users/me
        var putMe = await anonymousClient.PutAsJsonAsync("/api/v1/users/me", new UpdateProfileRequest("Name", "0901234567"));
        putMe.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // GET /api/v1/users/me/addresses
        var getAddresses = await anonymousClient.GetAsync("/api/v1/users/me/addresses");
        getAddresses.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // POST /api/v1/users/me/addresses
        var postAddress = await anonymousClient.PostAsJsonAsync("/api/v1/users/me/addresses", new AddUserAddressRequest("R", "0901234567", "L", "W", "D", "C"));
        postAddress.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // PUT /api/v1/users/me/addresses/{guid}
        var putAddress = await anonymousClient.PutAsJsonAsync($"/api/v1/users/me/addresses/{dummyGuid}", new UpdateUserAddressRequest("R", "0901234567", "L", "W", "D", "C"));
        putAddress.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // PATCH /api/v1/users/me/addresses/{guid}/default
        var patchDefault = await anonymousClient.PatchAsync($"/api/v1/users/me/addresses/{dummyGuid}/default", null);
        patchDefault.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // DELETE /api/v1/users/me/addresses/{guid}
        var deleteAddress = await anonymousClient.DeleteAsync($"/api/v1/users/me/addresses/{dummyGuid}");
        deleteAddress.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region 2. Inactive User Tests (403 Forbidden)

    [Fact]
    public async Task InactiveUserRequests_Return403Forbidden()
    {
        var (client, _) = await CreateAuthenticatedClientAsync(isActive: false);
        var dummyGuid = Guid.NewGuid();

        var putMe = await client.PutAsJsonAsync("/api/v1/users/me", new UpdateProfileRequest("Name", "0901234567"));
        putMe.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var getAddresses = await client.GetAsync("/api/v1/users/me/addresses");
        getAddresses.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var postAddress = await client.PostAsJsonAsync("/api/v1/users/me/addresses", new AddUserAddressRequest("R", "0901234567", "L", "W", "D", "C"));
        postAddress.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var putAddress = await client.PutAsJsonAsync($"/api/v1/users/me/addresses/{dummyGuid}", new UpdateUserAddressRequest("R", "0901234567", "L", "W", "D", "C"));
        putAddress.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var patchDefault = await client.PatchAsync($"/api/v1/users/me/addresses/{dummyGuid}/default", null);
        patchDefault.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var deleteAddress = await client.DeleteAsync($"/api/v1/users/me/addresses/{dummyGuid}");
        deleteAddress.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region 3. Cross-User Isolation (404 NotFound)

    [Fact]
    public async Task CrossUserIsolation_CustomerBCannotMutateOrSeeCustomerAAddress()
    {
        var (clientA, userA) = await CreateAuthenticatedClientAsync();
        var (clientB, userB) = await CreateAuthenticatedClientAsync();

        // Customer A creates an address
        var addRes = await clientA.PostAsJsonAsync("/api/v1/users/me/addresses", new AddUserAddressRequest(
            ReceiverName: "User A Receiver",
            PhoneNumber: "0901111111",
            AddressLine: "100 User A Street",
            Ward: "Ward A",
            District: "District A",
            City: "City A"
        ));
        addRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var addressA = await addRes.Content.ReadFromJsonAsync<UserAddressDto>(JsonOptions);
        addressA.Should().NotBeNull();

        // Customer B tries to update Customer A's address -> 404
        var updateRes = await clientB.PutAsJsonAsync($"/api/v1/users/me/addresses/{addressA!.Id}", new UpdateUserAddressRequest(
            ReceiverName: "Hacked",
            PhoneNumber: "0909999999",
            AddressLine: "Hacked St",
            Ward: "W",
            District: "D",
            City: "C"
        ));
        updateRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Customer B tries to set Customer A's address as default -> 404
        var patchRes = await clientB.PatchAsync($"/api/v1/users/me/addresses/{addressA.Id}/default", null);
        patchRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Customer B tries to delete Customer A's address -> 404
        var deleteRes = await clientB.DeleteAsync($"/api/v1/users/me/addresses/{addressA.Id}");
        deleteRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Customer B gets addresses -> does not see Customer A's address
        var getRes = await clientB.GetAsync("/api/v1/users/me/addresses");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var bAddresses = await getRes.Content.ReadFromJsonAsync<List<UserAddressDto>>(JsonOptions);
        bAddresses.Should().NotBeNull();
        bAddresses!.Should().BeEmpty();
    }

    #endregion

    #region 4. Full Profile & Address Lifecycle

    [Fact]
    public async Task FullLifecycle_ProfileUpdate_AndAddressCrudWithDefaultPromotion()
    {
        var (client, user) = await CreateAuthenticatedClientAsync();

        // 1. Update Profile
        var updateProfileRes = await client.PutAsJsonAsync("/api/v1/users/me", new UpdateProfileRequest("Updated Name", "0908765432"));
        updateProfileRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var profileDto = await updateProfileRes.Content.ReadFromJsonAsync<UserDto>(JsonOptions);
        profileDto.Should().NotBeNull();
        profileDto!.FullName.Should().Be("Updated Name");
        profileDto.PhoneNumber.Should().Be("0908765432");

        // 2. Initial addresses list is empty
        var initialListRes = await client.GetAsync("/api/v1/users/me/addresses");
        initialListRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var initialList = await initialListRes.Content.ReadFromJsonAsync<List<UserAddressDto>>(JsonOptions);
        initialList!.Should().BeEmpty();

        // 3. POST first address (requested IsDefault = false, but auto-defaulted to true)
        var post1Res = await client.PostAsJsonAsync("/api/v1/users/me/addresses", new AddUserAddressRequest(
            ReceiverName: "First Receiver",
            PhoneNumber: "0901111111",
            AddressLine: "111 First Ave",
            Ward: "Ward 1",
            District: "District 1",
            City: "HCM",
            IsDefault: false
        ));
        post1Res.StatusCode.Should().Be(HttpStatusCode.Created);
        var addr1 = await post1Res.Content.ReadFromJsonAsync<UserAddressDto>(JsonOptions);
        addr1.Should().NotBeNull();
        addr1!.IsDefault.Should().BeTrue();

        // 4. POST second address (requested IsDefault = false, stays false)
        var post2Res = await client.PostAsJsonAsync("/api/v1/users/me/addresses", new AddUserAddressRequest(
            ReceiverName: "Second Receiver",
            PhoneNumber: "0902222222",
            AddressLine: "222 Second Ave",
            Ward: "Ward 2",
            District: "District 2",
            City: "HN",
            IsDefault: false
        ));
        post2Res.StatusCode.Should().Be(HttpStatusCode.Created);
        var addr2 = await post2Res.Content.ReadFromJsonAsync<UserAddressDto>(JsonOptions);
        addr2.Should().NotBeNull();
        addr2!.IsDefault.Should().BeFalse();

        // 5. GET list: contains 2 addresses, addr1 first (default)
        var listRes = await client.GetAsync("/api/v1/users/me/addresses");
        listRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await listRes.Content.ReadFromJsonAsync<List<UserAddressDto>>(JsonOptions);
        list!.Should().HaveCount(2);
        list![0].Id.Should().Be(addr1.Id);
        list[0].IsDefault.Should().BeTrue();
        list[1].Id.Should().Be(addr2.Id);
        list[1].IsDefault.Should().BeFalse();

        // 6. PUT update second address
        var putRes = await client.PutAsJsonAsync($"/api/v1/users/me/addresses/{addr2.Id}", new UpdateUserAddressRequest(
            ReceiverName: "Second Receiver Updated",
            PhoneNumber: "0903333333",
            AddressLine: "222 Second Ave Renovated",
            Ward: "Ward 2 New",
            District: "District 2 New",
            City: "Da Nang"
        ));
        putRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedAddr2 = await putRes.Content.ReadFromJsonAsync<UserAddressDto>(JsonOptions);
        updatedAddr2!.ReceiverName.Should().Be("Second Receiver Updated");
        updatedAddr2.City.Should().Be("Da Nang");
        updatedAddr2.IsDefault.Should().BeFalse();

        // 7. PATCH set second address as default
        var patchRes = await client.PatchAsync($"/api/v1/users/me/addresses/{addr2.Id}/default", null);
        patchRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify idempotent call
        var patchAgainRes = await client.PatchAsync($"/api/v1/users/me/addresses/{addr2.Id}/default", null);
        patchAgainRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify order in GET list: addr2 is now default and first
        var listAfterDefault = await (await client.GetAsync("/api/v1/users/me/addresses")).Content.ReadFromJsonAsync<List<UserAddressDto>>(JsonOptions);
        listAfterDefault![0].Id.Should().Be(addr2.Id);
        listAfterDefault[0].IsDefault.Should().BeTrue();
        listAfterDefault[1].Id.Should().Be(addr1.Id);
        listAfterDefault[1].IsDefault.Should().BeFalse();

        // 8. DELETE the default address (addr2) -> addr1 should be automatically promoted to default!
        var deleteRes = await client.DeleteAsync($"/api/v1/users/me/addresses/{addr2.Id}");
        deleteRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listAfterDelete = await (await client.GetAsync("/api/v1/users/me/addresses")).Content.ReadFromJsonAsync<List<UserAddressDto>>(JsonOptions);
        listAfterDelete!.Should().HaveCount(1);
        listAfterDelete![0].Id.Should().Be(addr1.Id);
        listAfterDelete[0].IsDefault.Should().BeTrue();

        // 9. DELETE the remaining address -> list becomes empty
        var deleteLastRes = await client.DeleteAsync($"/api/v1/users/me/addresses/{addr1.Id}");
        deleteLastRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var finalList = await (await client.GetAsync("/api/v1/users/me/addresses")).Content.ReadFromJsonAsync<List<UserAddressDto>>(JsonOptions);
        finalList!.Should().BeEmpty();
    }

    #endregion

    #region 5. E2E Checkout Quote with Created Address

    [Fact]
    public async Task CheckoutQuote_UsingAddressCreatedViaApi_ReturnsValidQuote()
    {
        var unitPrice = 300_000m;
        var cartQty = 2;
        var (_, product) = await SeedProductAsync(price: unitPrice, stockQuantity: 20);
        var (client, user) = await CreateAuthenticatedClientAsync();

        // 1. Add item to cart
        var addToCartRes = await client.PostAsJsonAsync("/api/v1/carts/items", new AddCartItemRequest(product.Id, cartQty));
        addToCartRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Create address via API
        var createAddrRes = await client.PostAsJsonAsync("/api/v1/users/me/addresses", new AddUserAddressRequest(
            ReceiverName: "E2E Receiver",
            PhoneNumber: "0909123456",
            AddressLine: "123 Nguyen Hue",
            Ward: "Ben Nghe",
            District: "District 1",
            City: "TP Ho Chi Minh",
            IsDefault: true
        ));
        createAddrRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdAddress = await createAddrRes.Content.ReadFromJsonAsync<UserAddressDto>(JsonOptions);
        createdAddress.Should().NotBeNull();

        // 3. Request Checkout Quote with the newly created address
        var quoteRes = await client.PostAsJsonAsync("/api/v1/orders/checkout/quote", new CheckoutQuoteRequest(createdAddress!.Id));
        quoteRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var quoteDto = await quoteRes.Content.ReadFromJsonAsync<CheckoutQuoteDto>(JsonOptions);
        quoteDto.Should().NotBeNull();
        quoteDto!.Items.Should().ContainSingle(i => i.ProductId == product.Id && i.Quantity == cartQty);
        quoteDto.Subtotal.Should().Be(unitPrice * cartQty);
        quoteDto.FinalAmount.Should().Be(unitPrice * cartQty);
        quoteDto.DiscountAmount.Should().Be(0m);
        quoteDto.CanCheckout.Should().BeTrue();
        quoteDto.ShippingAddress.Should().Be("123 Nguyen Hue, Ben Nghe, District 1, TP Ho Chi Minh");
    }

    #endregion
}
