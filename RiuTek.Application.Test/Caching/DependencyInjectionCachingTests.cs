using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Infrastructure;
using RiuTek.Infrastructure.Caching;

namespace RiuTek.Application.Test.Caching;

public class DependencyInjectionCachingTests
{
    [Fact]
    public void AddInfrastructureDI_WhenRedisDisabled_RegistersNoOpCacheService()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "ConnectionStrings:DefaultConnection", "Host=localhost;Database=test;Username=postgres;Password=postgres" },
            { "JwtSettings:SecretKey", "Valid_Secret_Key_At_Least_32_Bytes_Long_123456" },
            { "RedisSettings:Enabled", "false" },
            { "RedisSettings:ConnectTimeoutMs", "5000" },
            { "RedisSettings:SyncTimeoutMs", "3000" },
            { "RedisSettings:DefaultExpirationMinutes", "10" }
        };

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructureDI(configuration);

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var cacheService = serviceProvider.GetService<ICacheService>();

        // Assert
        cacheService.Should().NotBeNull();
        cacheService.Should().BeOfType<NoOpCacheService>();
    }

    [Fact]
    public async Task AddInfrastructureDI_WhenRedisEnabledWithUnreachableServer_ResolvesGracefullyAndReturnsNullOnGet()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "ConnectionStrings:DefaultConnection", "Host=localhost;Database=test;Username=postgres;Password=postgres" },
            { "JwtSettings:SecretKey", "Valid_Secret_Key_At_Least_32_Bytes_Long_123456" },
            { "RedisSettings:Enabled", "true" },
            { "RedisSettings:ConnectionString", "127.0.0.1:65534,abortConnect=false,connectTimeout=100,syncTimeout=100" },
            { "RedisSettings:InstanceName", "riutek_test:" },
            { "RedisSettings:ConnectTimeoutMs", "100" },
            { "RedisSettings:SyncTimeoutMs", "100" },
            { "RedisSettings:DefaultExpirationMinutes", "10" }
        };

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructureDI(configuration);

        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert DI Resolution
        var cacheService = serviceProvider.GetService<ICacheService>();
        cacheService.Should().NotBeNull();
        cacheService.Should().BeOfType<RedisCacheService>();

        // Act & Assert Graceful Cache Miss without throwing
        var result = await cacheService!.GetAsync<string>("any_key");
        result.Should().BeNull();
    }
}
