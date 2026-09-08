using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using RiuTek.API.Contracts;
using RiuTek.API.IntegrationTest.Infrastructure;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using Xunit;

namespace RiuTek.API.IntegrationTest.Http;

[Collection(IntegrationTestCollection.Name)]
public class CatalogAuthorizationIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;

    public CatalogAuthorizationIntegrationTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public record ErrorResponse(string? Code, string? Description);

    public enum ActorType
    {
        Guest,
        Customer,
        Staff,
        Admin
    }

    public enum EndpointKey
    {
        GetProductById,
        CreateProduct,
        UpdateProduct,
        CreateCategory,
        UpdateCategory,
        DeleteCategory
    }

    public static TheoryData<EndpointKey, ActorType, HttpStatusCode, string?> AuthorizationMatrixData()
    {
        var data = new TheoryData<EndpointKey, ActorType, HttpStatusCode, string?>();

        var endpoints = new[]
        {
            (EndpointKey.GetProductById, "Product.NotFound"),
            (EndpointKey.CreateProduct, "Product.CategoryNotFound"),
            (EndpointKey.UpdateProduct, "Product.NotFound"),
            (EndpointKey.CreateCategory, "Category.ParentNotFound"),
            (EndpointKey.UpdateCategory, "Category.NotFound"),
            (EndpointKey.DeleteCategory, "Category.NotFound")
        };

        foreach (var (endpoint, expectedBusinessCode) in endpoints)
        {
            data.Add(endpoint, ActorType.Guest, HttpStatusCode.Unauthorized, null);
            data.Add(endpoint, ActorType.Customer, HttpStatusCode.Forbidden, null);
            data.Add(endpoint, ActorType.Staff, HttpStatusCode.NotFound, expectedBusinessCode);
            data.Add(endpoint, ActorType.Admin, HttpStatusCode.NotFound, expectedBusinessCode);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AuthorizationMatrixData))]
    public async Task ProtectedEndpoints_AcrossAllActors_EnforceCorrectAuthorizationAndBusinessResponse(
        EndpointKey endpoint,
        ActorType actor,
        HttpStatusCode expectedStatus,
        string? expectedBusinessCode)
    {
        // 1. Create client corresponding to actor
        using var client = actor switch
        {
            ActorType.Guest => _fixture.Factory.CreateClient(),
            ActorType.Customer => IntegrationTestAuth.CreateClientForRole(_fixture.Factory, UserRole.Customer),
            ActorType.Staff => IntegrationTestAuth.CreateClientForRole(_fixture.Factory, UserRole.Staff),
            ActorType.Admin => IntegrationTestAuth.CreateClientForRole(_fixture.Factory, UserRole.Admin),
            _ => throw new ArgumentOutOfRangeException(nameof(actor))
        };

        // 2. Create fresh request for each executed test case
        using var request = CreateFreshRequest(endpoint);

        // 3. Send HTTP request
        using var response = await client.SendAsync(request);

        // 4. Assert status code
        response.StatusCode.Should().Be(expectedStatus);

        // 5. If Staff or Admin, assert exact business error code and non-empty description
        if (expectedBusinessCode != null)
        {
            var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            error.Should().NotBeNull();
            error!.Code.Should().Be(expectedBusinessCode);
            error.Description.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task ProtectedEndpoint_WithCorruptedBearerToken_Returns401Unauthorized_Not500()
    {
        using var client = IntegrationTestAuth.CreateClientWithInvalidBearer(_fixture.Factory);
        using var request = CreateFreshRequest(EndpointKey.CreateProduct);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PublicCatalogEndpoints_WithoutToken_Return200Ok()
    {
        using var client = _fixture.Factory.CreateClient();

        var categoriesResponse = await client.GetAsync("/api/v1/categories");
        categoriesResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var productsResponse = await client.GetAsync("/api/v1/products");
        productsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static HttpRequestMessage CreateFreshRequest(EndpointKey endpoint)
    {
        return endpoint switch
        {
            EndpointKey.GetProductById => new HttpRequestMessage(HttpMethod.Get, $"/api/v1/products/{Guid.NewGuid()}"),

            EndpointKey.CreateProduct => new HttpRequestMessage(HttpMethod.Post, "/api/v1/products")
            {
                Content = JsonContent.Create(new CreateProductRequest(
                    CategoryId: Guid.NewGuid(),
                    Name: "Test Product",
                    Sku: $"SKU-{Guid.NewGuid():N}",
                    Brand: "Test Brand",
                    Price: 1000000m,
                    OriginalPrice: 1200000m,
                    StockQuantity: 10,
                    ImageUrl: "https://example.com/product.jpg",
                    AdditionalImages: null,
                    ComponentType: ComponentType.Cpu,
                    Specifications: new CpuSpecification
                    {
                        Socket = CpuSocket.AM5,
                        CoreCount = 8,
                        ThreadCount = 16,
                        BaseClockGhz = 3.8,
                        BoostClockGhz = 5.3,
                        TdpWattage = 105,
                        HasIntegratedGpu = true,
                        SupportedMemoryType = RamType.DDR5,
                        MaxMemorySpeedMhz = 5200
                    }
                ))
            },

            EndpointKey.UpdateProduct => new HttpRequestMessage(HttpMethod.Put, $"/api/v1/products/{Guid.NewGuid()}")
            {
                Content = JsonContent.Create(new UpdateProductRequest(
                    CategoryId: Guid.NewGuid(),
                    Name: "Updated Product",
                    Sku: $"SKU-{Guid.NewGuid():N}",
                    Brand: "Test Brand",
                    Price: 1000000m,
                    OriginalPrice: 1200000m,
                    StockQuantity: 10,
                    IsActive: true,
                    ImageUrl: "https://example.com/product.jpg",
                    AdditionalImages: null,
                    ComponentType: ComponentType.Cpu,
                    Specifications: new CpuSpecification
                    {
                        Socket = CpuSocket.AM5,
                        CoreCount = 8,
                        ThreadCount = 16,
                        BaseClockGhz = 3.8,
                        BoostClockGhz = 5.3,
                        TdpWattage = 105,
                        HasIntegratedGpu = true,
                        SupportedMemoryType = RamType.DDR5,
                        MaxMemorySpeedMhz = 5200
                    }
                ))
            },

            EndpointKey.CreateCategory => new HttpRequestMessage(HttpMethod.Post, "/api/v1/categories")
            {
                Content = JsonContent.Create(new CreateCategoryRequest(
                    Name: $"Category-{Guid.NewGuid():N}",
                    ComponentType: ComponentType.Cpu,
                    Description: "Test Description",
                    ParentId: Guid.NewGuid()
                ))
            },

            EndpointKey.UpdateCategory => new HttpRequestMessage(HttpMethod.Put, $"/api/v1/categories/{Guid.NewGuid()}")
            {
                Content = JsonContent.Create(new UpdateCategoryRequest(
                    Name: $"Category-{Guid.NewGuid():N}",
                    ComponentType: ComponentType.Cpu,
                    Description: "Test Description",
                    ParentId: null
                ))
            },

            EndpointKey.DeleteCategory => new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/categories/{Guid.NewGuid()}"),

            _ => throw new ArgumentOutOfRangeException(nameof(endpoint))
        };
    }
}
