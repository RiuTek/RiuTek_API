using System.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RiuTek.Infrastructure.Data;
using Xunit;

namespace RiuTek.API.IntegrationTest.Infrastructure;

[Collection(IntegrationTestCollection.Name)]
public class MigrationAndDatabaseSmokeTests
{
    private readonly PostgreSqlContainerFixture _fixture;

    public MigrationAndDatabaseSmokeTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Database_CanConnect_Successfully()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var canConnect = await db.Database.CanConnectAsync();
        canConnect.Should().BeTrue("ApplicationDbContext must be able to connect to PostgreSQL container");
    }

    [Fact]
    public async Task Database_HasAppliedAllMigrations_AndNoPendingMigrations()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var appliedMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        appliedMigrations.Should().NotBeEmpty("At least the baseline migrations must be applied to the database");
        appliedMigrations.Should().Contain(m => m.Contains("InitialCreate"));
        appliedMigrations.Should().Contain(m => m.Contains("AddEcommerceEntities"));
        appliedMigrations.Should().Contain(m => m.Contains("AddPostAndPostCommentEntities"));
        appliedMigrations.Should().Contain(m => m.Contains("AddAuthenticatedCart"));

        var pendingMigrations = await db.Database.GetPendingMigrationsAsync();
        pendingMigrations.Should().BeEmpty("There should be no pending migrations after MigrateAsync");
    }

    [Fact]
    public async Task Database_PgVectorExtension_ExistsInSystemCatalog()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pg_extension WHERE extname = @extName;";
        var param = command.CreateParameter();
        param.ParameterName = "@extName";
        param.Value = "vector";
        command.Parameters.Add(param);

        var result = Convert.ToInt64(await command.ExecuteScalarAsync());
        result.Should().Be(1, "The 'vector' extension must be installed in PostgreSQL via EF migrations");
    }

    [Fact]
    public async Task Database_ProductsTable_HasEmbeddingColumn_WithVector1536Type()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT format_type(atttypid, atttypmod) AS full_type
            FROM pg_attribute
            WHERE attrelid = '"Products"'::regclass AND attname = 'Embedding';
            """;

        var fullType = (string?)await command.ExecuteScalarAsync();
        fullType.Should().Be("vector(1536)", "The Embedding column in Products table must be of type vector(1536)");
    }

    [Fact]
    public async Task Database_ProductsTable_HasHnswIndex_WithCosineMetric()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT indexname, indexdef
            FROM pg_indexes
            WHERE tablename = 'Products' AND indexname = 'IX_Products_Embedding';
            """;

        await using var reader = await command.ExecuteReaderAsync();
        var found = await reader.ReadAsync();
        found.Should().BeTrue("Index 'IX_Products_Embedding' must exist in pg_indexes");

        var indexDef = reader.GetString(reader.GetOrdinal("indexdef"));
        indexDef.Should().Contain("hnsw", "Index must use HNSW access method");
        indexDef.Should().Contain("vector_cosine_ops", "Index must use vector_cosine_ops operator class");
        indexDef.Should().Contain("Embedding", "Index must target the Embedding column");
    }
}
