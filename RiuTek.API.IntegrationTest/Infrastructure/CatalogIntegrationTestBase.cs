using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Data;
using Xunit;

namespace RiuTek.API.IntegrationTest.Infrastructure;

public abstract class CatalogIntegrationTestBase : IAsyncLifetime
{
    protected readonly PostgreSqlContainerFixture Fixture;

    protected CatalogIntegrationTestBase(PostgreSqlContainerFixture fixture)
    {
        Fixture = fixture;
    }

    public virtual async Task InitializeAsync()
    {
        await CleanupCatalogDataAsync();
    }

    public virtual async Task DisposeAsync()
    {
        await CleanupCatalogDataAsync();
    }

    protected HttpClient CreateAdminClient() =>
        IntegrationTestAuth.CreateClientForRole(Fixture.Factory, UserRole.Admin);

    protected HttpClient CreateStaffClient() =>
        IntegrationTestAuth.CreateClientForRole(Fixture.Factory, UserRole.Staff);

    protected HttpClient CreatePublicClient() =>
        Fixture.Factory.CreateClient();

    private async Task CleanupCatalogDataAsync()
    {
        using var scope = Fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await using var tx = await dbContext.Database.BeginTransactionAsync();
        try
        {
            // Delete dependent Products first, then Categories to respect foreign keys
            await dbContext.Products.ExecuteDeleteAsync();
            await dbContext.Categories.ExecuteDeleteAsync();

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        // Strict verification: ensure database table counts are exactly zero
        var remainingProducts = await dbContext.Products.CountAsync();
        var remainingCategories = await dbContext.Categories.CountAsync();

        if (remainingProducts != 0 || remainingCategories != 0)
        {
            throw new InvalidOperationException(
                $"Catalog data cleanup verification failed. Products: {remainingProducts}, Categories: {remainingCategories}");
        }
    }
}
