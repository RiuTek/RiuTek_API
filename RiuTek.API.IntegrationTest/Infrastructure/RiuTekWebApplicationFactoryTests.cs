using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using RiuTek.Application.DTOs;
using Xunit;

namespace RiuTek.API.IntegrationTest.Infrastructure;

[Collection(IntegrationTestCollection.Name)]
public class RiuTekWebApplicationFactoryTests
{
    private readonly PostgreSqlContainerFixture _fixture;

    public RiuTekWebApplicationFactoryTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Factory_DoesNotReadOrMutateProcessEnvironment_AndOverridesProcessSentinels()
    {
        // 1. Snapshot initial process environment values
        var initialConnection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection", EnvironmentVariableTarget.Process);
        var initialJwt = Environment.GetEnvironmentVariable("JwtSettings__SecretKey", EnvironmentVariableTarget.Process);
        var initialRedis = Environment.GetEnvironmentVariable("RedisSettings__Enabled", EnvironmentVariableTarget.Process);
        var initialAspNetCoreEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", EnvironmentVariableTarget.Process);
        var initialDotnetEnv = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT", EnvironmentVariableTarget.Process);

        const string sentinelConnection = "Host=sentinel-unreachable-host;Port=5432;Database=sentinel_db;Username=none;Password=none";
        const string sentinelJwt = "SENTINEL_INVALID_KEY_SHORT";
        const string sentinelRedis = "true";
        const string sentinelAspNetCoreEnv = "SentinelAspNetCoreEnv";
        const string sentinelDotnetEnv = "SentinelDotnetEnv";

        try
        {
            // 2. Set sentinel values in process environment (deliberately non-functional sentinels)
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", sentinelConnection, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("JwtSettings__SecretKey", sentinelJwt, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("RedisSettings__Enabled", sentinelRedis, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", sentinelAspNetCoreEnv, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", sentinelDotnetEnv, EnvironmentVariableTarget.Process);

            // 3. Create secondary factory sharing the fixture's connection string
            var secondaryFactory = new RiuTekWebApplicationFactory(_fixture.ConnectionString);
            var client = secondaryFactory.CreateClient();

            // Verify sentinels remain untouched after factory and client instantiation
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection", EnvironmentVariableTarget.Process).Should().Be(sentinelConnection);
            Environment.GetEnvironmentVariable("JwtSettings__SecretKey", EnvironmentVariableTarget.Process).Should().Be(sentinelJwt);
            Environment.GetEnvironmentVariable("RedisSettings__Enabled", EnvironmentVariableTarget.Process).Should().Be(sentinelRedis);
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", EnvironmentVariableTarget.Process).Should().Be(sentinelAspNetCoreEnv);
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT", EnvironmentVariableTarget.Process).Should().Be(sentinelDotnetEnv);

            // 4. Execute endpoints:
            // GET /health/live must return 200 OK
            var healthResponse = await client.GetAsync("/health/live");
            healthResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var healthContent = await healthResponse.Content.ReadAsStringAsync();
            healthContent.Should().Be("Healthy");

            // GET /api/v1/categories must return 200 OK from fixture database (proves in-memory configuration defeated sentinel)
            var categoryResponse = await client.GetAsync("/api/v1/categories");
            categoryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var categories = await categoryResponse.Content.ReadFromJsonAsync<List<CategoryDto>>();
            categories.Should().NotBeNull();
            categories.Should().BeEmpty();

            // Verify sentinels remain untouched during test execution
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection", EnvironmentVariableTarget.Process).Should().Be(sentinelConnection);
            Environment.GetEnvironmentVariable("JwtSettings__SecretKey", EnvironmentVariableTarget.Process).Should().Be(sentinelJwt);
            Environment.GetEnvironmentVariable("RedisSettings__Enabled", EnvironmentVariableTarget.Process).Should().Be(sentinelRedis);
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", EnvironmentVariableTarget.Process).Should().Be(sentinelAspNetCoreEnv);
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT", EnvironmentVariableTarget.Process).Should().Be(sentinelDotnetEnv);

            // 5. Dispose secondary client and factory
            client.Dispose();
            await secondaryFactory.DisposeAsync();

            // Verify sentinels remain untouched after factory disposal
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection", EnvironmentVariableTarget.Process).Should().Be(sentinelConnection);
            Environment.GetEnvironmentVariable("JwtSettings__SecretKey", EnvironmentVariableTarget.Process).Should().Be(sentinelJwt);
            Environment.GetEnvironmentVariable("RedisSettings__Enabled", EnvironmentVariableTarget.Process).Should().Be(sentinelRedis);
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", EnvironmentVariableTarget.Process).Should().Be(sentinelAspNetCoreEnv);
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT", EnvironmentVariableTarget.Process).Should().Be(sentinelDotnetEnv);
        }
        finally
        {
            // 6. Guarantee exact restoration of initial process environment
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", initialConnection, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("JwtSettings__SecretKey", initialJwt, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("RedisSettings__Enabled", initialRedis, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", initialAspNetCoreEnv, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", initialDotnetEnv, EnvironmentVariableTarget.Process);
        }
    }
}
