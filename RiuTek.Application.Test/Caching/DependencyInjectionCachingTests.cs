using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Posts.Queries;
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
            { "RedisSettings:Enabled", "false" }
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
}
