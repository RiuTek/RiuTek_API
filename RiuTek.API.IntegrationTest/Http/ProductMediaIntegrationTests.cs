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
using RiuTek.Application.Features.Products.Common;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Data;
using Xunit;

namespace RiuTek.API.IntegrationTest.Http;

public class FakeProductMediaStorage : IProductMediaStorage
{
    public bool IsEnabled { get; set; } = true;
    public long MaxFileSizeBytes { get; set; } = 5242880L;
    public string PublicBaseUrl { get; set; } = "https://media.riutek.com";
    public HashSet<string> ExistingKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<Result<ProductMediaUploadDto>> CreatePresignedUploadUrlAsync(
        string objectKey,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return Task.FromResult(Result.Failure<ProductMediaUploadDto>(
                Error.Unavailable("ProductMedia.StorageDisabled", "Storage is disabled.")));
        }

        var uploadUrl = $"https://r2.cloudflarestorage.com/riutek-bucket/{objectKey}?fake-sig=1";
        var publicUrl = GetPublicUrl(objectKey);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
        var headers = new Dictionary<string, string> { { "Content-Type", contentType } };

        return Task.FromResult(Result.Success(new ProductMediaUploadDto(
            objectKey,
            uploadUrl,
            publicUrl,
            expiresAt,
            "PUT",
            headers)));
    }

    public Task<Result<ProductMediaMetadata>> GetObjectMetadataAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return Task.FromResult(Result.Failure<ProductMediaMetadata>(
                Error.Unavailable("ProductMedia.StorageDisabled", "Storage is disabled.")));
        }

        if (!ExistingKeys.Contains(objectKey))
        {
            return Task.FromResult(Result.Failure<ProductMediaMetadata>(
                Error.NotFound("ProductMedia.NotFound", "Object not found.")));
        }

        var ext = Path.GetExtension(objectKey);
        var mime = ProductMediaHelper.GetExpectedMimeTypeForExtension(ext) ?? "image/jpeg";

        return Task.FromResult(Result.Success(new ProductMediaMetadata(
            objectKey,
            mime,
            1024,
            "\"fake-etag\"")));
    }

    public Task<Result> DeleteObjectAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return Task.FromResult(Result.Failure(
                Error.Unavailable("ProductMedia.StorageDisabled", "Storage is disabled.")));
        }

        ExistingKeys.Remove(objectKey);
        return Task.FromResult(Result.Success());
    }

    public string GetPublicUrl(string objectKey)
    {
        return $"{PublicBaseUrl.TrimEnd('/')}/{objectKey.TrimStart('/')}";
    }
}

[Collection(IntegrationTestCollection.Name)]
public class ProductMediaIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ProductMediaIntegrationTests(PostgreSqlContainerFixture fixture)
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
            email: $"media_{role.ToString().ToLowerInvariant()}_{Guid.NewGuid():N}@riutek.test",
            passwordHash: "dummy_hash",
            fullName: $"{role} Media User",
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

    private static HttpRequestMessage CreateDeleteMediaRequest(string objectKey) =>
        new(HttpMethod.Delete, "/api/v1/product-media")
        {
            Content = JsonContent.Create(new DeleteProductMediaRequest(objectKey))
        };

    [Fact]
    public async Task ProductMediaEndpoints_WhenGuest_Return401Unauthorized()
    {
        using var client = _fixture.Factory.CreateClient();

        var uploadReq = new CreateProductMediaUploadRequest("test.jpg", "image/jpeg", 1024);
        var uploadRes = await client.PostAsJsonAsync("/api/v1/product-media/upload-requests", uploadReq);
        uploadRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var confirmReq = new ConfirmProductMediaUploadRequest("products/2026/10/01925b30-c9a7-7e61-a8d9-6012e1a3bc4d.jpg");
        var confirmRes = await client.PostAsJsonAsync("/api/v1/product-media/confirmations", confirmReq);
        confirmRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var deleteReq = CreateDeleteMediaRequest("products/2026/10/01925b30-c9a7-7e61-a8d9-6012e1a3bc4d.jpg");
        var deleteRes = await client.SendAsync(deleteReq);
        deleteRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProductMediaEndpoints_WhenCustomer_Return403Forbidden()
    {
        using var client = _fixture.Factory.CreateClient();
        var token = await SeedUserAndGenerateTokenAsync(UserRole.Customer);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var uploadReq = new CreateProductMediaUploadRequest("test.jpg", "image/jpeg", 1024);
        var uploadRes = await client.PostAsJsonAsync("/api/v1/product-media/upload-requests", uploadReq);
        uploadRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var confirmReq = new ConfirmProductMediaUploadRequest("products/2026/10/01925b30-c9a7-7e61-a8d9-6012e1a3bc4d.jpg");
        var confirmRes = await client.PostAsJsonAsync("/api/v1/product-media/confirmations", confirmReq);
        confirmRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var deleteReq = CreateDeleteMediaRequest("products/2026/10/01925b30-c9a7-7e61-a8d9-6012e1a3bc4d.jpg");
        var deleteRes = await client.SendAsync(deleteReq);
        deleteRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ProductMediaEndpoints_WhenDefaultConfigStorageDisabled_Return503ServiceUnavailable()
    {
        using var client = _fixture.Factory.CreateClient();
        var token = await SeedUserAndGenerateTokenAsync(UserRole.Admin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var uploadReq = new CreateProductMediaUploadRequest("test.jpg", "image/jpeg", 1024);
        var uploadRes = await client.PostAsJsonAsync("/api/v1/product-media/upload-requests", uploadReq);
        uploadRes.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        var confirmReq = new ConfirmProductMediaUploadRequest("products/2026/10/01925b30-c9a7-7e61-a8d9-6012e1a3bc4d.jpg");
        var confirmRes = await client.PostAsJsonAsync("/api/v1/product-media/confirmations", confirmReq);
        confirmRes.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        using var deleteReq = CreateDeleteMediaRequest("products/2026/10/01925b30-c9a7-7e61-a8d9-6012e1a3bc4d.jpg");
        var deleteRes = await client.SendAsync(deleteReq);
        deleteRes.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task ProductMediaLifecycle_WithFakeStorage_UploadConfirmAndDelete_Succeeds()
    {
        var (client, fakeStorage) = await CreateClientWithFakeStorageAsync(UserRole.Staff);
        using (client)
        {
            // 1. Request presigned upload URL
            var uploadReq = new CreateProductMediaUploadRequest("cool-cpu.webp", "image/webp", 2048);
            var uploadRes = await client.PostAsJsonAsync("/api/v1/product-media/upload-requests", uploadReq);
            uploadRes.StatusCode.Should().Be(HttpStatusCode.OK);

            var uploadDto = await uploadRes.Content.ReadFromJsonAsync<ProductMediaUploadDto>(JsonOptions);
            uploadDto.Should().NotBeNull();
            uploadDto!.ObjectKey.Should().StartWith("products/");
            uploadDto.ObjectKey.Should().EndWith(".webp");
            uploadDto.UploadUrl.Should().Contain("r2.cloudflarestorage.com");

            // Simulate object upload to storage
            fakeStorage.ExistingKeys.Add(uploadDto.ObjectKey);

            // 2. Confirm upload
            var confirmReq = new ConfirmProductMediaUploadRequest(uploadDto.ObjectKey);
            var confirmRes = await client.PostAsJsonAsync("/api/v1/product-media/confirmations", confirmReq);
            confirmRes.StatusCode.Should().Be(HttpStatusCode.OK);

            var mediaDto = await confirmRes.Content.ReadFromJsonAsync<ProductMediaDto>(JsonOptions);
            mediaDto.Should().NotBeNull();
            mediaDto!.ObjectKey.Should().Be(uploadDto.ObjectKey);
            mediaDto.ContentType.Should().Be("image/webp");

            // 3. Delete unreferenced media
            using var deleteReq = CreateDeleteMediaRequest(uploadDto.ObjectKey);
            var deleteRes = await client.SendAsync(deleteReq);
            deleteRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

            fakeStorage.ExistingKeys.Should().NotContain(uploadDto.ObjectKey);
        }
    }

    [Fact]
    public async Task DeleteProductMedia_WhenMediaIsInUseByProduct_Returns409Conflict()
    {
        const string key = "products/2026/10/01925b30-c9a7-7e61-a8d9-6012e1a3bc4d.jpg";
        const string publicUrl = $"https://media.riutek.com/{key}";

        // Seed product using this publicUrl
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var category = new Category("CPU", "cpu", ComponentType.Cpu);
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        var product = new Product(
            category.Id,
            "Intel i9",
            "intel-i9",
            "SKU-I9",
            "Intel",
            500m,
            10,
            publicUrl,
            ComponentType.Cpu,
            new CpuSpecification
            {
                Socket = CpuSocket.LGA1700,
                CoreCount = 16,
                ThreadCount = 24,
                BaseClockGhz = 3.2,
                BoostClockGhz = 5.6,
                TdpWattage = 125,
                HasIntegratedGpu = true,
                SupportedMemoryType = RamType.DDR5,
                MaxMemorySpeedMhz = 5600
            });
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var (client, fakeStorage) = await CreateClientWithFakeStorageAsync(UserRole.Admin, s => s.ExistingKeys.Add(key));
        using (client)
        {
            using var deleteReq = CreateDeleteMediaRequest(key);
            var deleteRes = await client.SendAsync(deleteReq);

            deleteRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
            fakeStorage.ExistingKeys.Should().Contain(key); // Not deleted
        }
    }

    [Fact]
    public async Task DeleteProductMedia_WhenMediaIsInUseInAdditionalImages_Returns409Conflict()
    {
        const string key = "products/2026/10/01925b30-c9a7-7e61-a8d9-6012e1a3bc4d.jpg";
        const string publicUrl = $"https://media.riutek.com/{key}";
        const string primaryUrl = "https://media.riutek.com/products/2026/10/primary.jpg";

        // Seed product using target publicUrl in AdditionalImages, with a distinct primary ImageUrl
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var category = new Category("CPU", $"cpu-{Guid.NewGuid():N}", ComponentType.Cpu);
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        var product = new Product(
            category.Id,
            "Intel i9 Additional",
            $"intel-i9-{Guid.NewGuid():N}",
            $"SKU-ADD-{Guid.NewGuid():N}",
            "Intel",
            500m,
            10,
            primaryUrl,
            ComponentType.Cpu,
            new CpuSpecification
            {
                Socket = CpuSocket.LGA1700,
                CoreCount = 16,
                ThreadCount = 24,
                BaseClockGhz = 3.2,
                BoostClockGhz = 5.6,
                TdpWattage = 125,
                HasIntegratedGpu = true,
                SupportedMemoryType = RamType.DDR5,
                MaxMemorySpeedMhz = 5600
            })
        {
            AdditionalImages = [publicUrl]
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var (client, fakeStorage) = await CreateClientWithFakeStorageAsync(UserRole.Admin, s => s.ExistingKeys.Add(key));
        using (client)
        {
            using var deleteReq = CreateDeleteMediaRequest(key);
            var deleteRes = await client.SendAsync(deleteReq);

            deleteRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
            fakeStorage.ExistingKeys.Should().Contain(key); // Not deleted
        }
    }
}
