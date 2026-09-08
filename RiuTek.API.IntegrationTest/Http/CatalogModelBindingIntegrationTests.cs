using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using RiuTek.API.Contracts;
using RiuTek.API.IntegrationTest.Infrastructure;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using Xunit;

namespace RiuTek.API.IntegrationTest.Http;

[Collection(IntegrationTestCollection.Name)]
public class CatalogModelBindingIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;

    public CatalogModelBindingIntegrationTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public record ErrorResponse(string? Code, string? Description);

    private static async Task AssertFrameworkValidationProblemDetails(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "framework validation error must return HTTP 400");
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json", "framework validation response must have exact application/problem+json content-type");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrWhiteSpace("response body must not be empty");

        // Must not leak internal types, paths, or stack traces
        body.Should().NotContain("at System.", "must not leak CLR stack trace");
        body.Should().NotContain("RiuTek-API", "must not leak local repository path");
        body.Should().NotContain("ComponentSpecification", "must not leak internal type name");
        body.Should().NotContain("System.NotSupportedException", "must not leak internal exception name");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("title", out var titleProp).Should().BeTrue("ValidationProblemDetails must contain 'title'");
        titleProp.GetString().Should().NotBeNullOrWhiteSpace("title must be a non-empty string");

        root.TryGetProperty("status", out var statusProp).Should().BeTrue("ValidationProblemDetails must contain 'status'");
        statusProp.GetInt32().Should().Be(400, "status must be 400");

        root.TryGetProperty("errors", out var errorsProp).Should().BeTrue("ValidationProblemDetails must contain 'errors'");
        errorsProp.ValueKind.Should().Be(JsonValueKind.Object, "'errors' must be a JSON object");
        errorsProp.EnumerateObject().Any().Should().BeTrue("'errors' object must contain at least one property");
    }

    [Fact]
    public async Task HappyBinding_PolymorphicSpecification_SerializesDiscriminator_AndReachesHandler()
    {
        using var client = IntegrationTestAuth.CreateClientForRole(_fixture.Factory, UserRole.Admin);

        var requestPayload = new CreateProductRequest(
            CategoryId: Guid.NewGuid(),
            Name: "Intel Core i9-14900K",
            Sku: $"CPU-INTEL-14900K-{Guid.NewGuid():N}",
            Brand: "Intel",
            Price: 15000000m,
            OriginalPrice: 16000000m,
            StockQuantity: 10,
            ImageUrl: "https://riutek.com/images/cpu-14900k.jpg",
            AdditionalImages: new List<string> { "https://riutek.com/images/cpu-14900k-box.jpg" },
            ComponentType: ComponentType.Cpu,
            Specifications: new CpuSpecification
            {
                Socket = CpuSocket.LGA1700,
                CoreCount = 24,
                ThreadCount = 32,
                BaseClockGhz = 3.2,
                BoostClockGhz = 6.0,
                TdpWattage = 125,
                HasIntegratedGpu = true,
                SupportedMemoryType = RamType.DDR5,
                MaxMemorySpeedMhz = 5600
            }
        );

        // 1. Assert serialized payload contains the polymorphic $type discriminator
        var serializedJson = JsonSerializer.Serialize(requestPayload);
        serializedJson.Should().Contain("\"$type\":\"cpu\"");

        // 2. Post to API and assert handler reached (returning 404 CategoryNotFound, not 400 or 500)
        using var response = await client.PostAsJsonAsync("/api/v1/products", requestPayload);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().NotBeNull();
        error!.Code.Should().Be("Product.CategoryNotFound");
        error.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task NegativeBinding_SpecificationsMissingDiscriminator_Returns400BadRequest()
    {
        using var client = IntegrationTestAuth.CreateClientForRole(_fixture.Factory, UserRole.Admin);

        var rawJson = $$"""
        {
            "categoryId": "{{Guid.NewGuid()}}",
            "name": "Intel Core i9",
            "sku": "SKU-{{Guid.NewGuid():N}}",
            "brand": "Intel",
            "price": 1000000,
            "originalPrice": 1200000,
            "stockQuantity": 5,
            "imageUrl": "https://example.com/img.jpg",
            "componentType": 1,
            "specifications": {
                "socket": 1,
                "coreCount": 8
            }
        }
        """;

        using var content = new StringContent(rawJson, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/v1/products", content);

        await AssertFrameworkValidationProblemDetails(response);
    }

    [Fact]
    public async Task NegativeBinding_SpecificationsUnsupportedDiscriminator_Returns400BadRequest()
    {
        using var client = IntegrationTestAuth.CreateClientForRole(_fixture.Factory, UserRole.Admin);

        var rawJson = $$"""
        {
            "categoryId": "{{Guid.NewGuid()}}",
            "name": "Quantum CPU",
            "sku": "SKU-{{Guid.NewGuid():N}}",
            "brand": "Intel",
            "price": 1000000,
            "originalPrice": 1200000,
            "stockQuantity": 5,
            "imageUrl": "https://example.com/img.jpg",
            "componentType": 1,
            "specifications": {
                "$type": "quantum_unsupported_type",
                "socket": 1
            }
        }
        """;

        using var content = new StringContent(rawJson, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/v1/products", content);

        await AssertFrameworkValidationProblemDetails(response);
    }

    [Fact]
    public async Task NegativeBinding_SpecificationsNull_Returns400BadRequest()
    {
        using var client = IntegrationTestAuth.CreateClientForRole(_fixture.Factory, UserRole.Admin);

        var rawJson = $$"""
        {
            "categoryId": "{{Guid.NewGuid()}}",
            "name": "Null Spec CPU",
            "sku": "SKU-{{Guid.NewGuid():N}}",
            "brand": "Intel",
            "price": 1000000,
            "originalPrice": 1200000,
            "stockQuantity": 5,
            "imageUrl": "https://example.com/img.jpg",
            "componentType": 1,
            "specifications": null
        }
        """;

        using var content = new StringContent(rawJson, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/v1/products", content);

        await AssertFrameworkValidationProblemDetails(response);
    }

    [Fact]
    public async Task NegativeBinding_UpdateProductMissingJsonRequiredIsActive_Returns400BadRequest()
    {
        using var client = IntegrationTestAuth.CreateClientForRole(_fixture.Factory, UserRole.Admin);

        var rawJson = $$"""
        {
            "categoryId": "{{Guid.NewGuid()}}",
            "name": "Updated CPU",
            "sku": "SKU-{{Guid.NewGuid():N}}",
            "brand": "Intel",
            "price": 1000000,
            "originalPrice": 1200000,
            "stockQuantity": 5,
            "imageUrl": "https://example.com/img.jpg",
            "componentType": 1,
            "specifications": {
                "$type": "cpu",
                "socket": 1,
                "coreCount": 8,
                "threadCount": 16,
                "baseClockGhz": 3.0,
                "boostClockGhz": 5.0,
                "tdpWattage": 105,
                "hasIntegratedGpu": true,
                "supportedMemoryType": 2,
                "maxMemorySpeedMhz": 5200
            }
        }
        """;

        using var content = new StringContent(rawJson, Encoding.UTF8, "application/json");
        using var response = await client.PutAsync($"/api/v1/products/{Guid.NewGuid()}", content);

        await AssertFrameworkValidationProblemDetails(response);
    }

    [Fact]
    public async Task NegativeBinding_MalformedJson_Returns400BadRequest_WithoutInternalStackTraceLeak()
    {
        using var client = IntegrationTestAuth.CreateClientForRole(_fixture.Factory, UserRole.Admin);

        const string malformedJson = "{ \"categoryId\": broken-json-syntax }";
        using var content = new StringContent(malformedJson, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/v1/products", content);

        await AssertFrameworkValidationProblemDetails(response);
    }

    [Fact]
    public async Task NegativeBinding_RouteNotAGuid_Returns404NotFound_DueToRouteConstraint()
    {
        using var client = IntegrationTestAuth.CreateClientForRole(_fixture.Factory, UserRole.Admin);

        using var response = await client.GetAsync("/api/v1/products/not-a-valid-guid");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task NegativeBinding_GetProducts_InvalidPagination_Returns400ValidationFailed()
    {
        using var client = _fixture.Factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/products?pageIndex=0&pageSize=101");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().NotBeNull();
        error!.Code.Should().Be("Validation.Failed");
        error.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task NegativeBinding_GetProducts_InvalidSortByEnum_Returns400BadRequest()
    {
        using var client = _fixture.Factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/products?sortBy=999");

        await AssertFrameworkValidationProblemDetails(response);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var errors = doc.RootElement.GetProperty("errors");

        var hasSortByError = false;
        foreach (var prop in errors.EnumerateObject())
        {
            if (prop.Name.Equals("sortBy", StringComparison.OrdinalIgnoreCase) ||
                prop.Name.EndsWith("sortBy", StringComparison.OrdinalIgnoreCase))
            {
                hasSortByError = true;
                break;
            }
        }
        hasSortByError.Should().BeTrue("Expected a validation error for sortBy");
    }

    [Fact]
    public async Task EmptyResponseContract_ReturnsExpectedStructure_WithCamelCaseNaming()
    {
        using var client = _fixture.Factory.CreateClient();

        // 1. Products empty list contract
        var productsResponse = await client.GetAsync("/api/v1/products");
        productsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var productsRawJson = await productsResponse.Content.ReadAsStringAsync();
        using var productsDoc = JsonDocument.Parse(productsRawJson);
        var root = productsDoc.RootElement;

        root.TryGetProperty("items", out var itemsElement).Should().BeTrue();
        root.TryGetProperty("totalCount", out var totalCountElement).Should().BeTrue();
        root.TryGetProperty("pageIndex", out var pageIndexElement).Should().BeTrue();
        root.TryGetProperty("pageSize", out var pageSizeElement).Should().BeTrue();

        // Boundary check: PascalCase properties must not be present
        root.TryGetProperty("Items", out _).Should().BeFalse();
        root.TryGetProperty("TotalCount", out _).Should().BeFalse();

        itemsElement.GetArrayLength().Should().Be(0);
        totalCountElement.GetInt32().Should().Be(0);
        pageIndexElement.GetInt32().Should().Be(1);
        pageSizeElement.GetInt32().Should().Be(20);

        // 2. Categories empty tree contract
        var categoriesResponse = await client.GetAsync("/api/v1/categories");
        categoriesResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var categoriesRawJson = await categoriesResponse.Content.ReadAsStringAsync();
        using var categoriesDoc = JsonDocument.Parse(categoriesRawJson);
        categoriesDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        categoriesDoc.RootElement.GetArrayLength().Should().Be(0);
    }
}
