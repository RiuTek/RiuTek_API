using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using RiuTek.API.IntegrationTest.Infrastructure;
using RiuTek.Application.Common.Models;
using RiuTek.Application.DTOs;
using Xunit;

namespace RiuTek.API.IntegrationTest.Http;

[Collection(IntegrationTestCollection.Name)]
public class HealthAndCategorySmokeTests
{
    private readonly HttpClient _client;

    public HealthAndCategorySmokeTests(PostgreSqlContainerFixture fixture)
    {
        _client = fixture.Factory.CreateClient();
    }

    [Fact]
    public async Task GetHealthLive_ReturnsOk_WithHealthyContent()
    {
        // Act: Call liveness endpoint
        var response = await _client.GetAsync("/health/live");

        // Assert: 200 OK and "Healthy" built-in content
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("Healthy");
    }

    [Fact]
    public async Task GetCategories_OnEmptyDatabase_ReturnsOk_WithEmptyArray()
    {
        // Act: Call public category tree endpoint through full HTTP pipeline
        var response = await _client.GetAsync("/api/v1/categories");

        // Assert: 200 OK
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var categories = await response.Content.ReadFromJsonAsync<List<CategoryDto>>();
        categories.Should().NotBeNull();
        categories.Should().BeEmpty("Database is empty after migration so category tree must be empty array");
    }

    [Fact]
    public async Task GetProducts_OnEmptyDatabase_ReturnsOk_WithEmptyPagedResult()
    {
        // Act: Call public product list endpoint through full HTTP pipeline
        var response = await _client.GetAsync("/api/v1/products");

        // Assert: 200 OK
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var pagedResult = await response.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>();
        pagedResult.Should().NotBeNull();
        pagedResult!.Items.Should().BeEmpty("Database is empty after migration so product list items must be empty");
        pagedResult.TotalCount.Should().Be(0);
        pagedResult.PageIndex.Should().Be(1);
        pagedResult.PageSize.Should().Be(20);
    }
}
