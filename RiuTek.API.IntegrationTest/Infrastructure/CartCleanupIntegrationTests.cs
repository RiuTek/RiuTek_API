using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Data;
using RiuTek.Infrastructure.Services;
using Xunit;

namespace RiuTek.API.IntegrationTest.Infrastructure;

[Collection(IntegrationTestCollection.Name)]
public class CartCleanupIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture;

    public CartCleanupIntegrationTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await CleanupDataAsync();
    }

    public async Task DisposeAsync()
    {
        await CleanupDataAsync();
    }

    private async Task CleanupDataAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            await db.CartItems.ExecuteDeleteAsync();
            await db.Carts.ExecuteDeleteAsync();
            await db.PCBuildItems.ExecuteDeleteAsync();
            await db.PCBuilds.ExecuteDeleteAsync();
            await db.OrderItems.ExecuteDeleteAsync();
            await db.Orders.ExecuteDeleteAsync();
            await db.Products.ExecuteDeleteAsync();
            await db.Categories.ExecuteDeleteAsync();
            await db.Users.ExecuteDeleteAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private static async Task<(User User, Category Category, Product Product)> SeedPrerequisitesAsync(ApplicationDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(
            email: $"cleanup_user_{suffix}@riutek.test",
            passwordHash: "dummy_hashed_password",
            fullName: $"Cleanup User {suffix}",
            role: UserRole.Customer
        );
        db.Users.Add(user);

        var category = new Category(
            name: $"Category {suffix}",
            slug: $"category-{suffix}",
            componentType: ComponentType.Accessory,
            description: "Test Category Description"
        );
        db.Categories.Add(category);

        var product = new Product(
            categoryId: category.Id,
            name: $"Product {suffix}",
            slug: $"product-{suffix}",
            sku: $"SKU-{suffix}",
            brand: "RiuTek Brand",
            price: 250_000m,
            stockQuantity: 100,
            imageUrl: "https://riutek.test/product.png",
            componentType: ComponentType.Accessory,
            specifications: new AccessorySpecification { Details = "Specification test" }
        );
        db.Products.Add(product);

        await db.SaveChangesAsync();
        return (user, category, product);
    }

    [Fact]
    public async Task RetentionCutoff_BoundaryTests_ChecksCreatedAtAndUpdatedAtCorrectly()
    {
        // 1. Arrange: Reference fixed nowUtc
        var nowUtc = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);
        var cutoff = nowUtc.AddDays(-30); // 2026-08-15 12:00:00 UTC

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (user1, category, product) = await SeedPrerequisitesAsync(db);

        // User 1: Created 31 days ago, UpdatedAt == null -> DELETED (< cutoff)
        var cart1 = new Cart(user1.Id, nowUtc.AddDays(-31));
        cart1.AddItem(product.Id, 1, nowUtc.AddDays(-31));
        // Reset UpdatedAt to null to simulate no mutations after creation
        cart1.UpdatedAt = null;
        cart1.CreatedAt = nowUtc.AddDays(-31);
        db.Carts.Add(cart1);

        // User 2: Created exactly 30 days ago, UpdatedAt == null -> KEPT (== cutoff)
        var user2 = new User($"u2_{Guid.NewGuid():N}@t.com", "h", "U2", UserRole.Customer);
        db.Users.Add(user2);
        var cart2 = new Cart(user2.Id, cutoff);
        cart2.AddItem(product.Id, 1, cutoff);
        cart2.UpdatedAt = null;
        cart2.CreatedAt = cutoff;
        db.Carts.Add(cart2);

        // User 3: Created 29 days ago, UpdatedAt == null -> KEPT (> cutoff)
        var user3 = new User($"u3_{Guid.NewGuid():N}@t.com", "h", "U3", UserRole.Customer);
        db.Users.Add(user3);
        var cart3 = new Cart(user3.Id, nowUtc.AddDays(-29));
        cart3.AddItem(product.Id, 1, nowUtc.AddDays(-29));
        cart3.UpdatedAt = null;
        cart3.CreatedAt = nowUtc.AddDays(-29);
        db.Carts.Add(cart3);

        // User 4: Created 40 days ago, UpdatedAt = 10 days ago -> KEPT (renewed by mutation)
        var user4 = new User($"u4_{Guid.NewGuid():N}@t.com", "h", "U4", UserRole.Customer);
        db.Users.Add(user4);
        var cart4 = new Cart(user4.Id, nowUtc.AddDays(-40));
        cart4.AddItem(product.Id, 1, nowUtc.AddDays(-40));
        cart4.CreatedAt = nowUtc.AddDays(-40);
        cart4.UpdatedAt = nowUtc.AddDays(-10);
        db.Carts.Add(cart4);

        // User 5: Created 40 days ago, UpdatedAt = 31 days ago -> DELETED (< cutoff)
        var user5 = new User($"u5_{Guid.NewGuid():N}@t.com", "h", "U5", UserRole.Customer);
        db.Users.Add(user5);
        var cart5 = new Cart(user5.Id, nowUtc.AddDays(-40));
        cart5.AddItem(product.Id, 1, nowUtc.AddDays(-40));
        cart5.CreatedAt = nowUtc.AddDays(-40);
        cart5.UpdatedAt = nowUtc.AddDays(-31);
        db.Carts.Add(cart5);

        // User 6: Created 40 days ago, UpdatedAt = exactly 30 days ago (cutoff) -> KEPT (== cutoff)
        var user6 = new User($"u6_{Guid.NewGuid():N}@t.com", "h", "U6", UserRole.Customer);
        db.Users.Add(user6);
        var cart6 = new Cart(user6.Id, nowUtc.AddDays(-40));
        cart6.AddItem(product.Id, 1, nowUtc.AddDays(-40));
        cart6.CreatedAt = nowUtc.AddDays(-40);
        cart6.UpdatedAt = cutoff;
        db.Carts.Add(cart6);

        await db.SaveChangesAsync();

        // 2. Act: Execute cleanup service
        var cleanupService = new CartCleanupService(db, new CartCleanupSettings(), NullLogger<CartCleanupService>.Instance);
        var deletedCount = await cleanupService.CleanInactiveCartsAsync(nowUtc);

        // 3. Assert:
        // cart1 and cart5 should be deleted (total 2 deleted)
        deletedCount.Should().Be(2, "Only cart1 (31d old, null updated) and cart5 (31d updated) must be deleted");

        using var verifyScope = _fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var remainingCartIds = await verifyDb.Carts.Select(c => c.Id).ToListAsync();

        remainingCartIds.Should().NotContain(cart1.Id, "Cart 1 was inactive for 31 days");
        remainingCartIds.Should().Contain(cart2.Id, "Cart 2 was created exactly 30 days ago and must be kept");
        remainingCartIds.Should().Contain(cart3.Id, "Cart 3 was created 29 days ago and must be kept");
        remainingCartIds.Should().Contain(cart4.Id, "Cart 4 was updated 10 days ago and must be kept");
        remainingCartIds.Should().NotContain(cart5.Id, "Cart 5 was updated 31 days ago and must be deleted");
        remainingCartIds.Should().Contain(cart6.Id, "Cart 6 was updated exactly 30 days ago and must be kept");
    }

    [Fact]
    public async Task CascadeIntegrity_CartItemsCascaded_ProductsAndUsersPreserved()
    {
        // 1. Arrange: expired cart with items, plus user with PCBuild
        var nowUtc = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (user, category, product) = await SeedPrerequisitesAsync(db);

        var expiredCart = new Cart(user.Id, nowUtc.AddDays(-35));
        expiredCart.AddItem(product.Id, 3, nowUtc.AddDays(-35));
        expiredCart.CreatedAt = nowUtc.AddDays(-35);
        expiredCart.UpdatedAt = nowUtc.AddDays(-35);
        db.Carts.Add(expiredCart);

        // Seed a PCBuild for the user to verify unrelated customer data is never touched
        var pcBuild = new PCBuild("My Saved PC Build", user.Id);
        db.PCBuilds.Add(pcBuild);

        await db.SaveChangesAsync();

        var cartId = expiredCart.Id;
        var cartItemId = expiredCart.Items.Single().Id;

        // 2. Act: Clean inactive carts
        var cleanupService = new CartCleanupService(db, new CartCleanupSettings(), NullLogger<CartCleanupService>.Instance);
        var deleted = await cleanupService.CleanInactiveCartsAsync(nowUtc);

        // 3. Assert:
        deleted.Should().Be(1);

        using var verifyScope = _fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Cart and its CartItem must be gone
        var cartExists = await verifyDb.Carts.AnyAsync(c => c.Id == cartId);
        cartExists.Should().BeFalse("Cart must be deleted");

        var cartItemExists = await verifyDb.CartItems.AnyAsync(i => i.Id == cartItemId);
        cartItemExists.Should().BeFalse("CartItem must be cascade deleted by PostgreSQL foreign key");

        // User, Product, Category, and PCBuild must remain intact
        var userExists = await verifyDb.Users.AnyAsync(u => u.Id == user.Id);
        userExists.Should().BeTrue("User must NOT be deleted");

        var productExists = await verifyDb.Products.AnyAsync(p => p.Id == product.Id);
        productExists.Should().BeTrue("Product must NOT be deleted");

        var pcBuildExists = await verifyDb.PCBuilds.AnyAsync(b => b.Id == pcBuild.Id);
        pcBuildExists.Should().BeTrue("PCBuild must NOT be deleted");
    }

    [Fact]
    public async Task MultiBatch_HandlesBatchLimitsAndSubsequentEmptySweeps()
    {
        // 1. Arrange: Seed 5 expired carts
        var nowUtc = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (_, category, product) = await SeedPrerequisitesAsync(db);

        for (var i = 0; i < 5; i++)
        {
            var u = new User($"batch_user_{i}_{Guid.NewGuid():N}@t.com", "h", $"User {i}", UserRole.Customer);
            db.Users.Add(u);
            var cart = new Cart(u.Id, nowUtc.AddDays(-35));
            cart.AddItem(product.Id, 1, nowUtc.AddDays(-35));
            cart.CreatedAt = nowUtc.AddDays(-35);
            cart.UpdatedAt = nowUtc.AddDays(-35);
            db.Carts.Add(cart);
        }
        await db.SaveChangesAsync();

        var cleanupService = new CartCleanupService(db, new CartCleanupSettings(), NullLogger<CartCleanupService>.Instance);

        // 2. Act 1: Sweep with batchSize = 2, maxBatches = 2 (deletes max 4 carts)
        var sweep1 = await cleanupService.CleanInactiveCartsAsync(nowUtc, batchSize: 2, maxBatches: 2);
        sweep1.Should().Be(4, "First sweep must delete exactly 4 carts (2 batches of 2)");

        // Verify 1 expired cart remains
        using (var vScope1 = _fixture.Factory.Services.CreateScope())
        {
            var vDb1 = vScope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var count1 = await vDb1.Carts.CountAsync();
            count1.Should().Be(1, "1 expired cart must remain after hitting max batches limit");
        }

        // 3. Act 2: Second sweep deletes the remaining 1 cart
        var sweep2 = await cleanupService.CleanInactiveCartsAsync(nowUtc, batchSize: 2, maxBatches: 2);
        sweep2.Should().Be(1, "Second sweep must delete the remaining 1 cart");

        // 4. Act 3: Third sweep returns 0 (idempotent, no orphans)
        var sweep3 = await cleanupService.CleanInactiveCartsAsync(nowUtc, batchSize: 2, maxBatches: 2);
        sweep3.Should().Be(0, "Third sweep must find 0 candidates");

        using (var vScope2 = _fixture.Factory.Services.CreateScope())
        {
            var vDb2 = vScope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var remainingCarts = await vDb2.Carts.CountAsync();
            remainingCarts.Should().Be(0);
            var remainingItems = await vDb2.CartItems.CountAsync();
            remainingItems.Should().Be(0);
        }
    }

    [Fact]
    public async Task ConcurrentUpdateSafety_CutoffReverification_PreservesRecentlyMutatedCart()
    {
        // 1. Arrange: expired cart (35 days old)
        var nowUtc = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (user, _, product) = await SeedPrerequisitesAsync(db);

        var cart = new Cart(user.Id, nowUtc.AddDays(-35));
        cart.AddItem(product.Id, 1, nowUtc.AddDays(-35));
        cart.CreatedAt = nowUtc.AddDays(-35);
        cart.UpdatedAt = nowUtc.AddDays(-35);
        db.Carts.Add(cart);
        await db.SaveChangesAsync();

        var deleteReachedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowDeleteToProceedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var interceptor = new TestCartDeleteCommandInterceptor(deleteReachedTcs, allowDeleteToProceedTcs);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.UseVector();
                npgsqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
            })
            .AddInterceptors(interceptor)
            .Options;

        await using var cleanupDb = new ApplicationDbContext(options);
        var cleanupService = new CartCleanupService(cleanupDb, new CartCleanupSettings(), NullLogger<CartCleanupService>.Instance);

        // 2. Act:
        // Launch real production CleanInactiveCartsAsync in a background task
        var cleanupTask = cleanupService.CleanInactiveCartsAsync(nowUtc);

        // Wait until interceptor catches the SQL DELETE command (proves candidate SELECT has completed)
        await deleteReachedTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // In an independent DbContext, simulate user mutation renewing the cart right before DELETE executes
        using (var mutateScope = _fixture.Factory.Services.CreateScope())
        {
            var mutateDb = mutateScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cartToMutate = await mutateDb.Carts.Include(c => c.Items).SingleAsync(c => c.Id == cart.Id);
            cartToMutate.SetItemQuantity(product.Id, 5, nowUtc); // UpdatedAt is now renewed to nowUtc
            await mutateDb.SaveChangesAsync();
        }

        // Release the interceptor to let the SQL DELETE proceed
        allowDeleteToProceedTcs.TrySetResult(true);
        var deletedCount = await cleanupTask;

        // 3. Assert:
        // Because CartCleanupService.cs includes `(c.UpdatedAt ?? c.CreatedAt) < cutoff` in its DELETE statement,
        // PostgreSQL deletes 0 rows and the production service returns 0!
        deletedCount.Should().Be(0, "Real production service must preserve the cart because UpdatedAt was renewed before DELETE executed");

        using var verifyScope = _fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reloadedCart = await verifyDb.Carts.Include(c => c.Items).SingleOrDefaultAsync(c => c.Id == cart.Id);
        reloadedCart.Should().NotBeNull("Cart must still exist in PostgreSQL");
        reloadedCart!.Items.Single().Quantity.Should().Be(5);
    }

    [Fact]
    public async Task BackgroundService_WhenEnabled_InitialSweepRunsAfterApplicationStartedWithoutBlockingHost()
    {
        // 1. Arrange: Seed expired cart on PostgreSQL Testcontainer
        var expiredTime = DateTime.UtcNow.AddDays(-35);
        Guid expiredCartId;
        Guid expiredCartItemId;

        using (var setupScope = _fixture.Factory.Services.CreateScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var (user, _, product) = await SeedPrerequisitesAsync(db);

            var expiredCart = new Cart(user.Id, expiredTime);
            expiredCart.AddItem(product.Id, 2, expiredTime);
            expiredCart.CreatedAt = expiredTime;
            expiredCart.UpdatedAt = expiredTime;
            db.Carts.Add(expiredCart);
            await db.SaveChangesAsync();

            expiredCartId = expiredCart.Id;
            expiredCartItemId = expiredCart.Items.Single().Id;
        }

        var sweepStartedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowSweepToCompleteTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sweepFinishedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        TestBarrierCartCleanupService? barrierService = null;

        var factory = _fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("CartCleanup:Enabled", "true");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["CartCleanup:Enabled"] = "true"
                });
            });

            builder.ConfigureServices(services =>
            {
                var descriptor = services.First(d => d.ServiceType == typeof(ICartCleanupService));
                services.Remove(descriptor);

                services.AddScoped<ICartCleanupService>(sp =>
                {
                    var inner = new CartCleanupService(
                        sp.GetRequiredService<ApplicationDbContext>(),
                        sp.GetRequiredService<CartCleanupSettings>(),
                        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CartCleanupService>>());

                    var lifetime = sp.GetRequiredService<IHostApplicationLifetime>();

                    barrierService = new TestBarrierCartCleanupService(
                        inner,
                        lifetime,
                        onBeforeSweep: async () =>
                        {
                            sweepStartedTcs.TrySetResult(true);
                            await allowSweepToCompleteTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
                        },
                        onAfterSweep: () =>
                        {
                            sweepFinishedTcs.TrySetResult(true);
                        });

                    return barrierService;
                });
            });
        });

        try
        {
            using var client = factory.CreateClient();

            // 2. Wait until background service starts its initial sweep
            await sweepStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));

            // Assert 1: At the time sweep ran, ApplicationStarted MUST be signaled!
            barrierService.Should().NotBeNull();
            barrierService!.WasApplicationStartedWhenSweepRan.Should().BeTrue(
                "Initial cleanup sweep must execute strictly after IHostApplicationLifetime.ApplicationStarted has signaled");

            // Assert 2: While the sweep is held by the barrier, query /health/live to prove API is NOT blocked
            var healthResponse = await client.GetAsync("/health/live");
            healthResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
            var content = await healthResponse.Content.ReadAsStringAsync();
            content.Should().Be("Healthy");

            // 3. Release the sweep to complete
            allowSweepToCompleteTcs.TrySetResult(true);
            await sweepFinishedTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));

            // Assert 3: Confirm that the real background service actually deleted the expired cart from PostgreSQL
            using var verifyScope = _fixture.Factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var cartExists = await verifyDb.Carts.AnyAsync(c => c.Id == expiredCartId);
            cartExists.Should().BeFalse("The expired cart must be deleted by the background cleanup service");

            var itemExists = await verifyDb.CartItems.AnyAsync(i => i.Id == expiredCartItemId);
            itemExists.Should().BeFalse("CartItems must be cascade deleted by PostgreSQL");
        }
        finally
        {
            allowSweepToCompleteTcs.TrySetResult(true);
            await factory.DisposeAsync();
        }
    }
}

internal sealed class TestCartDeleteCommandInterceptor : DbCommandInterceptor
{
    private readonly TaskCompletionSource<bool> _deleteReachedTcs;
    private readonly TaskCompletionSource<bool> _allowDeleteToProceedTcs;
    private int _interceptedCount;

    public TestCartDeleteCommandInterceptor(
        TaskCompletionSource<bool> deleteReachedTcs,
        TaskCompletionSource<bool> allowDeleteToProceedTcs)
    {
        _deleteReachedTcs = deleteReachedTcs;
        _allowDeleteToProceedTcs = allowDeleteToProceedTcs;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("DELETE", StringComparison.OrdinalIgnoreCase) &&
            command.CommandText.Contains("\"Carts\"", StringComparison.OrdinalIgnoreCase))
        {
            if (Interlocked.Increment(ref _interceptedCount) == 1)
            {
                _deleteReachedTcs.TrySetResult(true);
                await _allowDeleteToProceedTcs.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
        }

        return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }
}

internal sealed class TestBarrierCartCleanupService : ICartCleanupService
{
    private readonly ICartCleanupService _inner;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly Func<Task>? _onBeforeSweep;
    private readonly Action? _onAfterSweep;

    public TestBarrierCartCleanupService(
        ICartCleanupService inner,
        IHostApplicationLifetime lifetime,
        Func<Task>? onBeforeSweep = null,
        Action? onAfterSweep = null)
    {
        _inner = inner;
        _lifetime = lifetime;
        _onBeforeSweep = onBeforeSweep;
        _onAfterSweep = onAfterSweep;
    }

    public bool WasApplicationStartedWhenSweepRan { get; private set; }

    public async Task<int> CleanInactiveCartsAsync(
        DateTime nowUtc,
        int? batchSize = null,
        int? maxBatches = null,
        CancellationToken cancellationToken = default)
    {
        WasApplicationStartedWhenSweepRan = _lifetime.ApplicationStarted.IsCancellationRequested;

        if (_onBeforeSweep is not null)
        {
            await _onBeforeSweep();
        }

        try
        {
            return await _inner.CleanInactiveCartsAsync(nowUtc, batchSize, maxBatches, cancellationToken);
        }
        finally
        {
            _onAfterSweep?.Invoke();
        }
    }
}
