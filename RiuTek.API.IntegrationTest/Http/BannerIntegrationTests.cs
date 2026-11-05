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
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Data;
using Xunit;

namespace RiuTek.API.IntegrationTest.Http;

[Collection(IntegrationTestCollection.Name)]
public class BannerIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public BannerIntegrationTests(PostgreSqlContainerFixture fixture)
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
            await db.Banners.ExecuteDeleteAsync();
            await db.StockMovements.ExecuteDeleteAsync();
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

    private async Task<string> SeedUserAndGenerateTokenAsync(UserRole role)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var jwtGenerator = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();

        var user = new User(
            email: $"banner_{role.ToString().ToLowerInvariant()}_{Guid.NewGuid():N}@riutek.test",
            passwordHash: "dummy_hash",
            fullName: $"{role} Banner User",
            role: role
        ) { IsActive = true };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return jwtGenerator.GenerateAccessToken(user);
    }

    private async Task<(HttpClient Client, FakeProductMediaStorage FakeStorage)> CreateClientWithFakeStorageAsync(
        UserRole? role = UserRole.Admin,
        Action<FakeProductMediaStorage>? configure = null)
    {
        var fakeStorage = new FakeProductMediaStorage();
        configure?.Invoke(fakeStorage);

        var customFactory = _fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IProductMediaStorage));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }
                services.AddSingleton<IProductMediaStorage>(fakeStorage);
            });
        });

        var client = customFactory.CreateClient();
        if (role.HasValue)
        {
            var token = await SeedUserAndGenerateTokenAsync(role.Value);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return (client, fakeStorage);
    }

    [Fact]
    public async Task GetPublicBanners_ReturnsOnlyActive_SortedDeterministically_WhenAnonymous()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var idLow = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var idHigh = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var b1 = new Banner("Active 1 (Order 0, idHigh)", "products/2026/11/00000000-0000-0000-0000-000000000001.webp", "https://media.riutek.com/1", "/p1", 0, isActive: true);
        typeof(Banner).GetProperty(nameof(Banner.Id))!.SetValue(b1, idHigh);

        var b2 = new Banner("Active 2 (Order 0, idLow)", "products/2026/11/00000000-0000-0000-0000-000000000002.webp", "https://media.riutek.com/2", "/p2", 0, isActive: true);
        typeof(Banner).GetProperty(nameof(Banner.Id))!.SetValue(b2, idLow);

        var b3 = new Banner("Active 3 (Order 1)", "products/2026/11/00000000-0000-0000-0000-000000000003.webp", "https://media.riutek.com/3", "/p3", 1, isActive: true);

        var bInactive = new Banner("Inactive", "products/2026/11/00000000-0000-0000-0000-000000000004.webp", "https://media.riutek.com/4", "/p4", 0, isActive: false);

        db.Banners.AddRange(b1, b2, b3, bInactive);
        await db.SaveChangesAsync();

        var (client, _) = await CreateClientWithFakeStorageAsync(role: null);

        var response = await client.GetAsync("/api/v1/banners");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var banners = await response.Content.ReadFromJsonAsync<List<PublicBannerDto>>(JsonOptions);
        banners.Should().NotBeNull();
        banners!.Should().HaveCount(3);

        banners[0].Id.Should().Be(idLow);
        banners[0].Title.Should().Be("Active 2 (Order 0, idLow)");
        banners[1].Id.Should().Be(idHigh);
        banners[1].Title.Should().Be("Active 1 (Order 0, idHigh)");
        banners[2].Title.Should().Be("Active 3 (Order 1)");
    }

    [Fact]
    public async Task GetPublicBanners_WhenNoBanners_ReturnsEmptyArray()
    {
        var (client, _) = await CreateClientWithFakeStorageAsync(role: null);

        var response = await client.GetAsync("/api/v1/banners");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var banners = await response.Content.ReadFromJsonAsync<List<PublicBannerDto>>(JsonOptions);
        banners.Should().NotBeNull();
        banners!.Should().BeEmpty();
    }

    [Fact]
    public async Task ManagedEndpoints_AuthorizationMatrix_WorksAsExpected()
    {
        // 1. Anonymous -> 401
        var (anonClient, _) = await CreateClientWithFakeStorageAsync(role: null);
        var anonResp = await anonClient.GetAsync("/api/v1/banners/managed");
        anonResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 2. Customer -> 403
        var (custClient, _) = await CreateClientWithFakeStorageAsync(role: UserRole.Customer);
        var custResp = await custClient.GetAsync("/api/v1/banners/managed");
        custResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 3. Staff -> 200
        var (staffClient, _) = await CreateClientWithFakeStorageAsync(role: UserRole.Staff);
        var staffResp = await staffClient.GetAsync("/api/v1/banners/managed");
        staffResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Admin -> 200
        var (adminClient, _) = await CreateClientWithFakeStorageAsync(role: UserRole.Admin);
        var adminResp = await adminClient.GetAsync("/api/v1/banners/managed");
        adminResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateBanner_Returns201_LocationPointsToManagedGetById()
    {
        var key = "products/2026/11/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee.webp";
        var (client, fakeStorage) = await CreateClientWithFakeStorageAsync(role: UserRole.Admin, storage =>
        {
            storage.ExistingKeys.Add(key);
        });

        var request = new CreateBannerRequest(
            Title: "Black Friday Sale",
            ImageObjectKey: key,
            TargetUrl: "/products?sale=black-friday",
            DisplayOrder: 1,
            IsActive: true);

        var response = await client.PostAsJsonAsync("/api/v1/banners", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<ManagedBannerDto>(JsonOptions);
        created.Should().NotBeNull();
        created!.Title.Should().Be("Black Friday Sale");
        created.ImageObjectKey.Should().Be(key);
        created.ImageUrl.Should().Be($"https://media.riutek.com/{key}");
        created.TargetUrl.Should().Be("/products?sale=black-friday");
        created.DisplayOrder.Should().Be(1);
        created.IsActive.Should().BeTrue();

        // Verify Location header points to managed GetById
        response.Headers.Location.Should().NotBeNull();
        var location = response.Headers.Location!.ToString();
        location.Should().EndWith($"/api/v1/banners/{created.Id}");

        // Call GetById using the location
        var getByIdResp = await client.GetAsync(response.Headers.Location);
        getByIdResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getByIdResp.Content.ReadFromJsonAsync<ManagedBannerDto>(JsonOptions);
        fetched!.Id.Should().Be(created.Id);
        fetched.Title.Should().Be("Black Friday Sale");
    }

    [Fact]
    public async Task UpdateBanner_Succeeds_AndDoesNotCallHeadWhenImageUnchanged()
    {
        var key = "products/2026/11/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee.webp";
        var (client, fakeStorage) = await CreateClientWithFakeStorageAsync(role: UserRole.Admin, storage =>
        {
            storage.ExistingKeys.Add(key);
        });

        // 1. Create banner
        var createResp = await client.PostAsJsonAsync("/api/v1/banners", new CreateBannerRequest(
            "Initial Title", key, "/initial", 0, false));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await createResp.Content.ReadFromJsonAsync<ManagedBannerDto>(JsonOptions))!;

        var headCountBeforeUpdate = fakeStorage.GetMetadataCallCount;

        // 2. Update without changing key
        var updateRequest = new UpdateBannerRequest(
            Title: "Updated Title",
            ImageObjectKey: key,
            TargetUrl: "/updated-target",
            DisplayOrder: 10,
            IsActive: true);

        var updateResp = await client.PutAsJsonAsync($"/api/v1/banners/{created.Id}", updateRequest);
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await updateResp.Content.ReadFromJsonAsync<ManagedBannerDto>(JsonOptions);
        updated!.Title.Should().Be("Updated Title");
        updated.TargetUrl.Should().Be("/updated-target");
        updated.DisplayOrder.Should().Be(10);
        updated.IsActive.Should().BeTrue();

        // Verify HEAD was NOT called during update with same key
        fakeStorage.GetMetadataCallCount.Should().Be(headCountBeforeUpdate);

        // 3. Update non-existent banner -> 404
        var notFoundResp = await client.PutAsJsonAsync($"/api/v1/banners/{Guid.NewGuid()}", updateRequest);
        notFoundResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteBanner_HardDeletesRecord_Returns204_AndDoesNotCallStorageDelete()
    {
        var key = "products/2026/11/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee.webp";
        var (client, fakeStorage) = await CreateClientWithFakeStorageAsync(role: UserRole.Admin, storage =>
        {
            storage.ExistingKeys.Add(key);
        });

        // 1. Create banner
        var createResp = await client.PostAsJsonAsync("/api/v1/banners", new CreateBannerRequest(
            "To Delete", key, "/del", 0, true));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await createResp.Content.ReadFromJsonAsync<ManagedBannerDto>(JsonOptions))!;

        var deleteCallCountBefore = fakeStorage.DeleteCallCount;

        // 2. Delete banner
        var deleteResp = await client.DeleteAsync($"/api/v1/banners/{created.Id}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify storage was NOT called
        fakeStorage.DeleteCallCount.Should().Be(deleteCallCountBefore);

        // Verify banner is gone from DB (404)
        var getResp = await client.GetAsync($"/api/v1/banners/{created.Id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Delete non-existent -> 404
        var deleteAgainResp = await client.DeleteAsync($"/api/v1/banners/{created.Id}");
        deleteAgainResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task BannerMediaGuard_PreventsMediaDeletionWhileInUse_AllowsDeletionAfterBannerDeleted()
    {
        var key = "products/2026/11/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee.webp";
        var (client, fakeStorage) = await CreateClientWithFakeStorageAsync(role: UserRole.Admin, storage =>
        {
            storage.ExistingKeys.Add(key);
        });

        // 1. Create banner with key
        var createResp = await client.PostAsJsonAsync("/api/v1/banners", new CreateBannerRequest(
            "Guarded Banner", key, "/guard", 0, false));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var banner = (await createResp.Content.ReadFromJsonAsync<ManagedBannerDto>(JsonOptions))!;

        // 2. Try deleting the media via DELETE /api/v1/product-media -> MUST return 409 Conflict
        var deleteMediaReq = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/product-media")
        {
            Content = JsonContent.Create(new DeleteProductMediaRequest(key))
        };
        var conflictResp = await client.SendAsync(deleteMediaReq);
        conflictResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        fakeStorage.DeleteCallCount.Should().Be(0);

        // 3. Delete the banner record
        var deleteBannerResp = await client.DeleteAsync($"/api/v1/banners/{banner.Id}");
        deleteBannerResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 4. Now delete the media -> MUST return 204 NoContent and call storage delete
        var deleteMediaReq2 = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/product-media")
        {
            Content = JsonContent.Create(new DeleteProductMediaRequest(key))
        };
        var successResp = await client.SendAsync(deleteMediaReq2);
        successResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        fakeStorage.DeleteCallCount.Should().Be(1);
    }
}
