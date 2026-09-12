using System.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Data;
using Xunit;

namespace RiuTek.API.IntegrationTest.Infrastructure;

[Collection(IntegrationTestCollection.Name)]
public class CartPersistenceIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture;

    public CartPersistenceIntegrationTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await CleanupCartDataAsync();
    }

    public async Task DisposeAsync()
    {
        await CleanupCartDataAsync();
    }

    private async Task CleanupCartDataAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            await db.CartItems.ExecuteDeleteAsync();
            await db.Carts.ExecuteDeleteAsync();
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
            email: $"cart_user_{suffix}@riutek.test",
            passwordHash: "dummy_hashed_password",
            fullName: $"Cart User {suffix}",
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
    public async Task Scenario01_Migration_AppliesSuccessfully_CartAndCartItemTablesAndConstraintsExist()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        // 1. Verify tables exist
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT table_name
                FROM information_schema.tables
                WHERE table_schema = 'public' AND table_name IN ('Carts', 'CartItems')
                ORDER BY table_name;
                """;
            var tables = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }

            tables.Should().Contain(new[] { "CartItems", "Carts" });
        }

        // 2. Verify check constraint CK_CartItems_Quantity exists
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT conname, pg_get_constraintdef(oid) AS def
                FROM pg_constraint
                WHERE conname = 'CK_CartItems_Quantity';
                """;
            await using var reader = await cmd.ExecuteReaderAsync();
            var exists = await reader.ReadAsync();
            exists.Should().BeTrue("CK_CartItems_Quantity must exist in pg_constraint");
            var def = reader.GetString(reader.GetOrdinal("def"));
            def.Should().Contain("\"Quantity\" >= 1");
            def.Should().Contain("\"Quantity\" <= 99");
        }

        // 3. Verify unique index on Carts.UserId
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT indexname, indexdef
                FROM pg_indexes
                WHERE tablename = 'Carts' AND indexname = 'IX_Carts_UserId';
                """;
            await using var reader = await cmd.ExecuteReaderAsync();
            var exists = await reader.ReadAsync();
            exists.Should().BeTrue("IX_Carts_UserId must exist in pg_indexes");
            var indexDef = reader.GetString(reader.GetOrdinal("indexdef"));
            indexDef.Should().Contain("UNIQUE INDEX");
        }

        // 4. Verify unique index on CartItems (CartId, ProductId)
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT indexname, indexdef
                FROM pg_indexes
                WHERE tablename = 'CartItems' AND indexname = 'IX_CartItems_CartId_ProductId';
                """;
            await using var reader = await cmd.ExecuteReaderAsync();
            var exists = await reader.ReadAsync();
            exists.Should().BeTrue("IX_CartItems_CartId_ProductId must exist in pg_indexes");
            var indexDef = reader.GetString(reader.GetOrdinal("indexdef"));
            indexDef.Should().Contain("UNIQUE INDEX");
        }
    }

    [Fact]
    public async Task Scenario02_PersistAndReload_CartWithItemsAndNavigations_LoadsCorrectly()
    {
        Guid cartId;
        Guid userId;
        Guid productId;

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var (user, _, product) = await SeedPrerequisitesAsync(db);
            userId = user.Id;
            productId = product.Id;

            var cart = new Cart(user.Id);
            cart.AddItem(product.Id, 3);
            db.Carts.Add(cart);
            await db.SaveChangesAsync();
            cartId = cart.Id;
        }

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var loadedCart = await db.Carts
                .Include(c => c.User)
                .Include(c => c.Items)
                    .ThenInclude(i => i.Product)
                .SingleOrDefaultAsync(c => c.Id == cartId);

            loadedCart.Should().NotBeNull();
            loadedCart!.UserId.Should().Be(userId);
            loadedCart.User.Should().NotBeNull();
            loadedCart.User.Id.Should().Be(userId);
            loadedCart.Version.Should().Be(2);
            loadedCart.Items.Should().HaveCount(1);

            var item = loadedCart.Items.First();
            item.CartId.Should().Be(cartId);
            item.ProductId.Should().Be(productId);
            item.Quantity.Should().Be(3);
            item.Product.Should().NotBeNull();
            item.Product.Id.Should().Be(productId);
            item.Product.Price.Should().Be(250_000m);

            // Verify CartItem has no snapshot properties on entity type
            typeof(CartItem).GetProperty("Price").Should().BeNull();
            typeof(CartItem).GetProperty("ProductName").Should().BeNull();
            typeof(CartItem).GetProperty("Sku").Should().BeNull();
            typeof(CartItem).GetProperty("StockQuantity").Should().BeNull();
            typeof(CartItem).GetProperty("IsActive").Should().BeNull();
        }
    }

    [Fact]
    public async Task Scenario03_UniqueUserId_PreventsSecondCartForSameUser()
    {
        Guid userId;
        using (var scope1 = _fixture.Factory.Services.CreateScope())
        {
            var db1 = scope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var (user, _, _) = await SeedPrerequisitesAsync(db1);
            userId = user.Id;

            var cart1 = new Cart(user.Id);
            db1.Carts.Add(cart1);
            await db1.SaveChangesAsync();
        }

        using (var scope2 = _fixture.Factory.Services.CreateScope())
        {
            var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cart2 = new Cart(userId);
            db2.Carts.Add(cart2);

            var act = async () => await db2.SaveChangesAsync();
            var ex = await act.Should().ThrowAsync<DbUpdateException>();
            ex.Which.InnerException.Should().BeOfType<PostgresException>()
                .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        }
    }

    [Fact]
    public async Task Scenario04_UniqueCartIdProductId_PreventsDuplicateProductLine()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (user, _, product) = await SeedPrerequisitesAsync(db);

        var cart = new Cart(user.Id);
        db.Carts.Add(cart);
        await db.SaveChangesAsync();

        // Direct SQL insertion of first item
        var itemId1 = Guid.NewGuid();
        await db.Database.ExecuteSqlRawAsync(
            @"INSERT INTO ""CartItems"" (""Id"", ""CartId"", ""ProductId"", ""Quantity"", ""CreatedAt"")
              VALUES ({0}, {1}, {2}, {3}, {4})",
            itemId1, cart.Id, product.Id, 1, DateTime.UtcNow);

        // Attempt direct SQL insertion of duplicate (CartId, ProductId)
        var itemId2 = Guid.NewGuid();
        var act = async () => await db.Database.ExecuteSqlRawAsync(
            @"INSERT INTO ""CartItems"" (""Id"", ""CartId"", ""ProductId"", ""Quantity"", ""CreatedAt"")
              VALUES ({0}, {1}, {2}, {3}, {4})",
            itemId2, cart.Id, product.Id, 2, DateTime.UtcNow);

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        ex.Which.ConstraintName.Should().Be("IX_CartItems_CartId_ProductId");
    }

    [Fact]
    public async Task Scenario05_CheckConstraint_QuantityOutOfRange_RejectedAtDatabaseLevel()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (user, _, product) = await SeedPrerequisitesAsync(db);

        var cart = new Cart(user.Id);
        db.Carts.Add(cart);
        await db.SaveChangesAsync();

        // 5a. Quantity = 0 violates CK_CartItems_Quantity
        var actZero = async () => await db.Database.ExecuteSqlRawAsync(
            @"INSERT INTO ""CartItems"" (""Id"", ""CartId"", ""ProductId"", ""Quantity"", ""CreatedAt"")
              VALUES ({0}, {1}, {2}, {3}, {4})",
            Guid.NewGuid(), cart.Id, product.Id, 0, DateTime.UtcNow);

        var exZero = await actZero.Should().ThrowAsync<PostgresException>();
        exZero.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        exZero.Which.ConstraintName.Should().Be("CK_CartItems_Quantity");

        // 5b. Quantity = 100 violates CK_CartItems_Quantity
        var actHundred = async () => await db.Database.ExecuteSqlRawAsync(
            @"INSERT INTO ""CartItems"" (""Id"", ""CartId"", ""ProductId"", ""Quantity"", ""CreatedAt"")
              VALUES ({0}, {1}, {2}, {3}, {4})",
            Guid.NewGuid(), cart.Id, product.Id, 100, DateTime.UtcNow);

        var exHundred = await actHundred.Should().ThrowAsync<PostgresException>();
        exHundred.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        exHundred.Which.ConstraintName.Should().Be("CK_CartItems_Quantity");

        // 5c. Valid quantity (50) succeeds
        var actValid = async () => await db.Database.ExecuteSqlRawAsync(
            @"INSERT INTO ""CartItems"" (""Id"", ""CartId"", ""ProductId"", ""Quantity"", ""CreatedAt"")
              VALUES ({0}, {1}, {2}, {3}, {4})",
            Guid.NewGuid(), cart.Id, product.Id, 50, DateTime.UtcNow);

        await actValid.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Scenario06_CascadeDelete_UserDelete_CascadesToCartAndCartItems()
    {
        Guid cartId;
        Guid userId;

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var (user, _, product) = await SeedPrerequisitesAsync(db);
            userId = user.Id;

            var cart = new Cart(user.Id);
            cart.AddItem(product.Id, 2);
            db.Carts.Add(cart);
            await db.SaveChangesAsync();
            cartId = cart.Id;

            // Delete User
            var userToDelete = await db.Users.FindAsync(userId);
            db.Users.Remove(userToDelete!);
            await db.SaveChangesAsync();
        }

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var remainingCart = await db.Carts.FindAsync(cartId);
            remainingCart.Should().BeNull("Cart must be cascade-deleted when its owning User is deleted");

            var remainingItems = await db.CartItems.Where(ci => ci.CartId == cartId).ToListAsync();
            remainingItems.Should().BeEmpty("CartItems must be cascade-deleted when its owning User is deleted");
        }
    }

    [Fact]
    public async Task Scenario07_CascadeDelete_CartDelete_CascadesToCartItems()
    {
        Guid cartId;
        Guid userId;
        Guid productId;

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var (user, _, product) = await SeedPrerequisitesAsync(db);
            userId = user.Id;
            productId = product.Id;

            var cart = new Cart(user.Id);
            cart.AddItem(product.Id, 2);
            db.Carts.Add(cart);
            await db.SaveChangesAsync();
            cartId = cart.Id;

            // Delete Cart
            var cartToDelete = await db.Carts.FindAsync(cartId);
            db.Carts.Remove(cartToDelete!);
            await db.SaveChangesAsync();
        }

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var remainingItems = await db.CartItems.Where(ci => ci.CartId == cartId).ToListAsync();
            remainingItems.Should().BeEmpty("CartItems must be cascade-deleted when Cart is deleted");

            var userStillExists = await db.Users.FindAsync(userId);
            userStillExists.Should().NotBeNull("User must NOT be deleted when Cart is deleted");

            var productStillExists = await db.Products.FindAsync(productId);
            productStillExists.Should().NotBeNull("Product must NOT be deleted when Cart is deleted");
        }
    }

    [Fact]
    public async Task Scenario08_RestrictDelete_ProductDelete_RestrictedWhenInCart()
    {
        Guid productId;
        using (var scope1 = _fixture.Factory.Services.CreateScope())
        {
            var db1 = scope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var (user, _, product) = await SeedPrerequisitesAsync(db1);
            productId = product.Id;

            var cart = new Cart(user.Id);
            cart.AddItem(product.Id, 1);
            db1.Carts.Add(cart);
            await db1.SaveChangesAsync();
        }

        // In a new DbContext scope where only the Product is tracked (not CartItem),
        // deleting the Product sends DELETE FROM "Products" to the database,
        // which triggers PostgreSQL's foreign key restrict constraint FK_CartItems_Products_ProductId
        using (var scope2 = _fixture.Factory.Services.CreateScope())
        {
            var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var productToDelete = await db2.Products.FindAsync(productId);
            db2.Products.Remove(productToDelete!);

            var act = async () => await db2.SaveChangesAsync();
            var ex = await act.Should().ThrowAsync<DbUpdateException>();
            ex.Which.InnerException.Should().BeOfType<PostgresException>()
                .Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
        }
    }

    [Fact]
    public async Task Scenario09_ProductSoftUpdate_IsActiveFalse_DoesNotDeleteCartItem()
    {
        Guid cartId;
        Guid productId;

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var (user, _, product) = await SeedPrerequisitesAsync(db);
            productId = product.Id;

            var cart = new Cart(user.Id);
            cart.AddItem(product.Id, 1);
            db.Carts.Add(cart);
            await db.SaveChangesAsync();
            cartId = cart.Id;

            // Soft-update product: IsActive = false
            var productToUpdate = await db.Products.FindAsync(productId);
            productToUpdate!.IsActive = false;
            await db.SaveChangesAsync();
        }

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var reloadedCart = await db.Carts
                .Include(c => c.Items)
                    .ThenInclude(i => i.Product)
                .SingleOrDefaultAsync(c => c.Id == cartId);

            reloadedCart.Should().NotBeNull();
            reloadedCart!.Items.Should().HaveCount(1);
            var item = reloadedCart.Items.First();
            item.Quantity.Should().Be(1);
            item.Product.Should().NotBeNull();
            item.Product.IsActive.Should().BeFalse("Product IsActive changed to false");
        }
    }

    [Fact]
    public async Task Scenario10_Concurrency_ConcurrentUpdatesOnSameCart_ThrowsDbUpdateConcurrencyException()
    {
        Guid cartId;
        Guid product1Id;
        Guid product2Id;

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var (user, category, product1) = await SeedPrerequisitesAsync(db);
            product1Id = product1.Id;

            var product2 = new Product(
                categoryId: category.Id,
                name: "Product 2 Concurrency Test",
                slug: $"product-2-concurrency-{Guid.NewGuid():N}",
                sku: $"SKU-CONC-2-{Guid.NewGuid():N}",
                brand: "RiuTek",
                price: 500_000m,
                stockQuantity: 50,
                imageUrl: "https://riutek.test/p2.png",
                componentType: ComponentType.Accessory,
                specifications: new AccessorySpecification { Details = "Concurrency item" }
            );
            db.Products.Add(product2);
            await db.SaveChangesAsync();
            product2Id = product2.Id;

            var cart = new Cart(user.Id);
            cart.AddItem(product1.Id, 2);
            db.Carts.Add(cart);
            await db.SaveChangesAsync();
            cartId = cart.Id;
        }

        // Open two independent DbContext scopes simulating concurrent users/requests
        using var scope1 = _fixture.Factory.Services.CreateScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        using var scope2 = _fixture.Factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var cartContext1 = await db1.Carts.Include(c => c.Items).FirstAsync(c => c.Id == cartId);
        var cartContext2 = await db2.Carts.Include(c => c.Items).FirstAsync(c => c.Id == cartId);

        // First scope succeeds and updates Version
        var p2 = await db1.Products.FindAsync(product2Id);
        cartContext1.AddItem(p2!.Id, 1);
        await db1.SaveChangesAsync();

        // Second scope holds stale Version and attempts to update
        cartContext2.SetItemQuantity(product1Id, 5);
        var actStale = async () => await db2.SaveChangesAsync();

        await actStale.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "Stale Version concurrency token must trigger DbUpdateConcurrencyException");
    }

    [Fact]
    public async Task Scenario11_DatabaseSchema_CartItemsHasNoSnapshotColumns()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'CartItems'
            ORDER BY column_name;
            """;

        var columns = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        columns.Should().Contain(new[] { "Id", "CartId", "ProductId", "Quantity", "CreatedAt", "UpdatedAt" });
        columns.Should().NotContain(new[] { "Price", "OriginalPrice", "ProductName", "Sku", "ImageUrl", "IsActive", "StockQuantity" },
            "CartItems must never snapshot product catalog data into its schema");
    }

    [Fact]
    public async Task Scenario12_Concurrency_StaleFailureStateIsNotPersisted()
    {
        Guid cartId;
        Guid product1Id;
        Guid product2Id;

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var (user, category, product1) = await SeedPrerequisitesAsync(db);
            product1Id = product1.Id;

            var product2 = new Product(
                categoryId: category.Id,
                name: "Product 2 Stale Persistence Test",
                slug: $"product-2-stale-{Guid.NewGuid():N}",
                sku: $"SKU-STALE-2-{Guid.NewGuid():N}",
                brand: "RiuTek",
                price: 300_000m,
                stockQuantity: 50,
                imageUrl: "https://riutek.test/p2.png",
                componentType: ComponentType.Accessory,
                specifications: new AccessorySpecification { Details = "Stale failure test" }
            );
            db.Products.Add(product2);
            await db.SaveChangesAsync();
            product2Id = product2.Id;

            var cart = new Cart(user.Id);
            cart.AddItem(product1.Id, 2);
            db.Carts.Add(cart);
            await db.SaveChangesAsync();
            cartId = cart.Id;
        }

        // Two independent scopes simulate concurrent users
        using var scope1 = _fixture.Factory.Services.CreateScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        using var scope2 = _fixture.Factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var cartContext1 = await db1.Carts.Include(c => c.Items).FirstAsync(c => c.Id == cartId);
        var cartContext2 = await db2.Carts.Include(c => c.Items).FirstAsync(c => c.Id == cartId);

        // Writer 1 succeeds: adds product 2, version increments from 2 to 3
        cartContext1.AddItem(product2Id, 1);
        await db1.SaveChangesAsync();

        // Writer 2 attempts update with stale version: tries to change product 1 quantity to 5
        cartContext2.SetItemQuantity(product1Id, 5);
        var actStale = async () => await db2.SaveChangesAsync();
        await actStale.Should().ThrowAsync<DbUpdateConcurrencyException>();

        // Open 3rd fresh scope: verify writer 1 committed, while writer 2's mutations were completely uncommitted
        using var scope3 = _fixture.Factory.Services.CreateScope();
        var db3 = scope3.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var persistedCart = await db3.Carts.Include(c => c.Items).FirstAsync(c => c.Id == cartId);
        persistedCart.Version.Should().Be(3, "Only the successful writer's version should be in the database");
        persistedCart.Items.Should().HaveCount(2, "Product 1 and Product 2 should be in the cart");

        var item1 = persistedCart.Items.Single(i => i.ProductId == product1Id);
        item1.Quantity.Should().Be(2, "Stale writer's quantity (5) must NOT have been saved; original quantity (2) remains");

        var item2 = persistedCart.Items.Single(i => i.ProductId == product2Id);
        item2.Quantity.Should().Be(1, "Successful writer's added item must be persisted");
    }

    [Fact]
    public async Task Scenario13_MultiStepMutations_AddRemoveReAddAndClearAdd_PreserveTracking()
    {
        Guid cartId;
        Guid product1Id;
        Guid product2Id;

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var (user, category, product1) = await SeedPrerequisitesAsync(db);
            product1Id = product1.Id;

            var product2 = new Product(
                categoryId: category.Id,
                name: "Product 2 MultiStep Test",
                slug: $"product-2-multistep-{Guid.NewGuid():N}",
                sku: $"SKU-MULTI-2-{Guid.NewGuid():N}",
                brand: "RiuTek",
                price: 400_000m,
                stockQuantity: 50,
                imageUrl: "https://riutek.test/p2.png",
                componentType: ComponentType.Accessory,
                specifications: new AccessorySpecification { Details = "Multistep test" }
            );
            db.Products.Add(product2);
            await db.SaveChangesAsync();
            product2Id = product2.Id;

            var cart = new Cart(user.Id);
            db.Carts.Add(cart);
            await db.SaveChangesAsync();
            cartId = cart.Id;
        }

        // Part A: Add, Remove, Re-add in single unit of work
        using (var scopeA = _fixture.Factory.Services.CreateScope())
        {
            var dbA = scopeA.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cartA = await dbA.Carts.Include(c => c.Items).FirstAsync(c => c.Id == cartId);

            cartA.AddItem(product1Id, 2);
            cartA.RemoveItem(product1Id);
            cartA.AddItem(product1Id, 5);

            await dbA.SaveChangesAsync();
        }

        using (var scopeCheckA = _fixture.Factory.Services.CreateScope())
        {
            var dbCheckA = scopeCheckA.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cartCheckA = await dbCheckA.Carts.Include(c => c.Items).FirstAsync(c => c.Id == cartId);

            cartCheckA.Items.Should().HaveCount(1);
            var item = cartCheckA.Items.First();
            item.ProductId.Should().Be(product1Id);
            item.Quantity.Should().Be(5);
        }

        // Part B: Clear and Add in single unit of work
        using (var scopeB = _fixture.Factory.Services.CreateScope())
        {
            var dbB = scopeB.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cartB = await dbB.Carts.Include(c => c.Items).FirstAsync(c => c.Id == cartId);

            cartB.Clear();
            cartB.AddItem(product2Id, 3);

            await dbB.SaveChangesAsync();
        }

        using (var scopeCheckB = _fixture.Factory.Services.CreateScope())
        {
            var dbCheckB = scopeCheckB.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cartCheckB = await dbCheckB.Carts.Include(c => c.Items).FirstAsync(c => c.Id == cartId);

            cartCheckB.Items.Should().HaveCount(1);
            var item = cartCheckB.Items.First();
            item.ProductId.Should().Be(product2Id);
            item.Quantity.Should().Be(3);
        }
    }
}
