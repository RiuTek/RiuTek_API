using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Core.Interfaces;
using RiuTek.Infrastructure;

namespace RiuTek.Application.Test.Security;

public class DatabaseConfigurationTests
{
    private const string ValidTestJwtKey = "Valid_Secret_Key_At_Least_32_Bytes_Long_123456";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddInfrastructureDI_WhenConnectionStringMissingOrEmpty_ThrowsWithClearMessage(string? connectionString)
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "JwtSettings:SecretKey", ValidTestJwtKey },
            { "RedisSettings:Enabled", "false" }
        };

        if (connectionString != null)
        {
            inMemorySettings["ConnectionStrings:DefaultConnection"] = connectionString;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var services = new ServiceCollection();

        var act = () => services.AddInfrastructureDI(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings__DefaultConnection*")
            .Where(ex => !ex.Message.Contains("postgres") && !ex.Message.Contains("Password"));
    }

    [Fact]
    public void AddInfrastructureDI_WhenValidConfigurationProvided_RegistersServicesWithoutConnectingToDatabase()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "ConnectionStrings:DefaultConnection", "Host=localhost;Database=testdb;Username=testuser;Password=testpass" },
            { "JwtSettings:SecretKey", ValidTestJwtKey },
            { "RedisSettings:Enabled", "false" }
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var services = new ServiceCollection();

        var act = () => services.AddInfrastructureDI(configuration);

        act.Should().NotThrow();

        // Verify key abstractions are registered
        services.Should().Contain(d => d.ServiceType == typeof(IApplicationDbContext));
        services.Should().Contain(d => d.ServiceType == typeof(IUnitOfWork));
        services.Should().Contain(d => d.ServiceType == typeof(IRepository<>));
    }

    [Fact]
    public void ConfigurationPrecedence_EnvironmentVariablesOverrideJsonOrInMemorySettings()
    {
        var randomSuffix = Guid.NewGuid().ToString("N")[..8];
        var prefix = $"TEST_RIUTEK_{randomSuffix}_";
        var envVarName = $"{prefix}ConnectionStrings__DefaultConnection";
        var expectedEnvValue = "Host=env_host;Database=env_db;Username=env_user;Password=env_pass";

        try
        {
            Environment.SetEnvironmentVariable(envVarName, expectedEnvValue);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "ConnectionStrings:DefaultConnection", "Host=json_host;Database=json_db" }
                })
                .AddEnvironmentVariables(prefix: prefix)
                .Build();

            var resolvedConnectionString = configuration.GetConnectionString("DefaultConnection");

            resolvedConnectionString.Should().Be(expectedEnvValue);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, null);
        }
    }
}
