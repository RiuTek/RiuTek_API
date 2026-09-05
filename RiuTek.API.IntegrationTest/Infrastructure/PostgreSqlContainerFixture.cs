using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RiuTek.Infrastructure.Data;
using Testcontainers.PostgreSql;
using Xunit;

namespace RiuTek.API.IntegrationTest.Infrastructure;

public class PostgreSqlContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .WithDatabase("riutek_integration_test")
        .WithUsername("riutek_test_user")
        .WithPassword("Riutek_Test_P@ssw0rd_2026!")
        .Build();

    public RiuTekWebApplicationFactory Factory { get; private set; } = null!;

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await _container.StartAsync(cts.Token);

        Factory = new RiuTekWebApplicationFactory(ConnectionString);

        // Run migrations on fresh database using production EF Core Npgsql/pgvector pipeline
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.MigrateAsync(cts.Token);
    }

    public async Task DisposeAsync()
    {
        if (Factory != null)
        {
            await Factory.DisposeAsync();
        }

        await _container.DisposeAsync();
    }
}
