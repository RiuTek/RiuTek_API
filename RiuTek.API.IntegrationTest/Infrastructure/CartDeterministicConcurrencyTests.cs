using System.Net;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RiuTek.API.Contracts;
using RiuTek.API.Controllers;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Carts.Commands;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Data;
using Xunit;

namespace RiuTek.API.IntegrationTest.Infrastructure;

[Collection(IntegrationTestCollection.Name)]
public class CartDeterministicConcurrencyTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture;

    public CartDeterministicConcurrencyTests(PostgreSqlContainerFixture fixture)
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
            email: $"concurrency_user_{suffix}@riutek.test",
            passwordHash: "dummy_hashed_password",
            fullName: $"Concurrency User {suffix}",
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
    public async Task FirstCartCreationRace_BothReadNull_OneSucceeds_OneReturnsDuplicateCartCreationConflict_DatabaseStateClean()
    {
        // 1. Arrange: seed user and product (PostgreSQL has 0 Cart rows for this user)
        using var setupScope = _fixture.Factory.Services.CreateScope();
        var setupDb = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (user, _, product) = await SeedPrerequisitesAsync(setupDb);

        // Create two independent scopes and DbContext instances
        using var scope1 = _fixture.Factory.Services.CreateScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        using var scope2 = _fixture.Factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Deterministic synchronization barriers
        var tcs1ReachedHook = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2ReachedHook = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsAllowDb1ToSave = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsAllowDb2ToSave = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var hookDb1 = new TestHookApplicationDbContext(db1, async ct =>
        {
            tcs1ReachedHook.TrySetResult(true);
            await tcsAllowDb1ToSave.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
        });

        var hookDb2 = new TestHookApplicationDbContext(db2, async ct =>
        {
            tcs2ReachedHook.TrySetResult(true);
            await tcsAllowDb2ToSave.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
        });

        var userSvc = new TestCurrentUserService(user.Id);
        var handler1 = new AddCartItemCommandHandler(hookDb1, userSvc);
        var handler2 = new AddCartItemCommandHandler(hookDb2, userSvc);

        // 2. Act:
        // Start handler 1 (requests quantity = 1)
        var task1 = handler1.Handle(new AddCartItemCommand(product.Id, 1), CancellationToken.None);

        // Wait until handler 1 finishes reading null cart and pauses before SaveChangesAsync
        await tcs1ReachedHook.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // Start handler 2 (requests quantity = 2) while handler 1 is held before save
        var task2 = handler2.Handle(new AddCartItemCommand(product.Id, 2), CancellationToken.None);

        // Wait until handler 2 also finishes reading null cart and pauses before SaveChangesAsync
        await tcs2ReachedHook.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // Both handlers have deterministically read "no cart exists" from PostgreSQL!

        // Release handler 1 to save first
        tcsAllowDb1ToSave.TrySetResult(true);
        var result1 = await task1;

        // Release handler 2 to save second (hits PostgreSQL unique constraint IX_Carts_UserId)
        tcsAllowDb2ToSave.TrySetResult(true);
        var result2 = await task2;

        // 3. Assert:
        // Handler 1 must succeed
        result1.IsSuccess.Should().BeTrue("Winner's request must succeed");
        result1.Value.Should().NotBeNull();
        result1.Value.Items.Should().ContainSingle(i => i.ProductId == product.Id && i.Quantity == 1);

        // Handler 2 must catch PostgreSQL unique constraint violation on IX_Carts_UserId and return Conflict
        result2.IsFailure.Should().BeTrue("Concurrent creator must receive Failure result");
        result2.Error.Type.Should().Be(ErrorType.Conflict);
        result2.Error.Code.Should().Be("Cart.DuplicateCartCreation");

        // 4. Database audit using a completely fresh 3rd DbContext:
        using var verifyScope = _fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var userCarts = await verifyDb.Carts
            .Include(c => c.Items)
            .Where(c => c.UserId == user.Id)
            .ToListAsync();

        userCarts.Should().ContainSingle("Exactly one cart row must exist in PostgreSQL for this user");
        var persistedCart = userCarts.Single();
        persistedCart.Items.Should().ContainSingle("Only winner's payload must exist; loser's data was not partially written or merged");
        persistedCart.Items.Single().ProductId.Should().Be(product.Id);
        persistedCart.Items.Single().Quantity.Should().Be(1);
    }

    [Fact]
    public async Task StaleVersionConcurrency_SetQuantity_OneSucceeds_OneReturnsConcurrencyConflict_DatabaseStateClean()
    {
        // 1. Arrange: seed user, product, and existing cart with quantity = 2 (Version = 1)
        using var setupScope = _fixture.Factory.Services.CreateScope();
        var setupDb = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (user, _, product) = await SeedPrerequisitesAsync(setupDb);

        var cart = new Cart(user.Id);
        cart.AddItem(product.Id, 2);
        setupDb.Carts.Add(cart);
        var initialVersion = cart.Version;
        initialVersion.Should().Be(2, "Cart initialized at 1 and incremented to 2 upon adding initial item");
        await setupDb.SaveChangesAsync();

        using var scope1 = _fixture.Factory.Services.CreateScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        using var scope2 = _fixture.Factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tcs1ReachedHook = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2ReachedHook = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsAllowDb1ToSave = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsAllowDb2ToSave = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var hookDb1 = new TestHookApplicationDbContext(db1, async ct =>
        {
            tcs1ReachedHook.TrySetResult(true);
            await tcsAllowDb1ToSave.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
        });

        var hookDb2 = new TestHookApplicationDbContext(db2, async ct =>
        {
            tcs2ReachedHook.TrySetResult(true);
            await tcsAllowDb2ToSave.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
        });

        var userSvc = new TestCurrentUserService(user.Id);
        var handler1 = new SetCartItemQuantityCommandHandler(hookDb1, userSvc);
        var handler2 = new SetCartItemQuantityCommandHandler(hookDb2, userSvc);

        // 2. Act:
        // Start handler 1 (sets quantity to 5)
        var task1 = handler1.Handle(new SetCartItemQuantityCommand(product.Id, 5), CancellationToken.None);
        await tcs1ReachedHook.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // Start handler 2 (sets quantity to 10) while handler 1 is held before save
        var task2 = handler2.Handle(new SetCartItemQuantityCommand(product.Id, 10), CancellationToken.None);
        await tcs2ReachedHook.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // Both handlers loaded the cart with initial Version = 1 before either saved!

        // Release writer 1 to save first
        tcsAllowDb1ToSave.TrySetResult(true);
        var result1 = await task1;

        // Release writer 2 to save with stale version
        tcsAllowDb2ToSave.TrySetResult(true);
        var result2 = await task2;

        // 3. Assert:
        result1.IsSuccess.Should().BeTrue("First writer must succeed");
        result1.Value.Items.Single().Quantity.Should().Be(5);

        result2.IsFailure.Should().BeTrue("Stale writer must receive Failure result");
        result2.Error.Type.Should().Be(ErrorType.Conflict);
        result2.Error.Code.Should().Be("Cart.ConcurrencyConflict");

        // 4. Database audit using fresh DbContext:
        using var verifyScope = _fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var reloadedCart = await verifyDb.Carts
            .Include(c => c.Items)
            .SingleAsync(c => c.UserId == user.Id);

        reloadedCart.Version.Should().Be(3, "Version must be incremented from 2 to 3 by the winning writer");
        reloadedCart.Items.Single().Quantity.Should().Be(5, "Writer 2's stale quantity of 10 must not be persisted");
    }

    [Fact]
    public async Task StaleVersionConcurrency_AddItem_OneSucceeds_OneReturnsConcurrencyConflict_DatabaseStateClean()
    {
        // 1. Arrange: seed user, category, two products, and existing cart with Product 1 (qty 1)
        using var setupScope = _fixture.Factory.Services.CreateScope();
        var setupDb = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (user, category, product1) = await SeedPrerequisitesAsync(setupDb);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var product2 = new Product(
            categoryId: category.Id,
            name: $"Product 2 {suffix}",
            slug: $"product-2-{suffix}",
            sku: $"SKU2-{suffix}",
            brand: "RiuTek Brand",
            price: 350_000m,
            stockQuantity: 50,
            imageUrl: "https://riutek.test/p2.png",
            componentType: ComponentType.Accessory,
            specifications: new AccessorySpecification { Details = "P2 Specs" }
        );
        setupDb.Products.Add(product2);

        var cart = new Cart(user.Id);
        cart.AddItem(product1.Id, 1);
        setupDb.Carts.Add(cart);
        await setupDb.SaveChangesAsync();

        using var scope1 = _fixture.Factory.Services.CreateScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        using var scope2 = _fixture.Factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tcs1ReachedHook = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2ReachedHook = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsAllowDb1ToSave = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsAllowDb2ToSave = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var hookDb1 = new TestHookApplicationDbContext(db1, async ct =>
        {
            tcs1ReachedHook.TrySetResult(true);
            await tcsAllowDb1ToSave.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
        });

        var hookDb2 = new TestHookApplicationDbContext(db2, async ct =>
        {
            tcs2ReachedHook.TrySetResult(true);
            await tcsAllowDb2ToSave.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
        });

        var userSvc = new TestCurrentUserService(user.Id);
        var handler1 = new AddCartItemCommandHandler(hookDb1, userSvc);
        var handler2 = new AddCartItemCommandHandler(hookDb2, userSvc);

        // 2. Act:
        // Handler 1 adds Product 2 (qty 1)
        var task1 = handler1.Handle(new AddCartItemCommand(product2.Id, 1), CancellationToken.None);
        await tcs1ReachedHook.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // Handler 2 adds more of Product 1 (qty 2) while handler 1 is paused
        var task2 = handler2.Handle(new AddCartItemCommand(product1.Id, 2), CancellationToken.None);
        await tcs2ReachedHook.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // Release writer 1 first
        tcsAllowDb1ToSave.TrySetResult(true);
        var result1 = await task1;

        // Release writer 2 second with stale version
        tcsAllowDb2ToSave.TrySetResult(true);
        var result2 = await task2;

        // 3. Assert:
        result1.IsSuccess.Should().BeTrue("Writer 1 must succeed");

        result2.IsFailure.Should().BeTrue("Writer 2 must receive ConcurrencyConflict");
        result2.Error.Type.Should().Be(ErrorType.Conflict);
        result2.Error.Code.Should().Be("Cart.ConcurrencyConflict");

        // 4. Database audit using fresh DbContext:
        using var verifyScope = _fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var reloadedCart = await verifyDb.Carts
            .Include(c => c.Items)
            .SingleAsync(c => c.UserId == user.Id);

        reloadedCart.Version.Should().Be(3, "Version must be incremented from 2 to 3 by the winning writer");
        reloadedCart.Items.Should().HaveCount(2, "Product 1 (qty 1) and Product 2 (qty 1)");
        reloadedCart.Items.Single(i => i.ProductId == product1.Id).Quantity.Should().Be(1, "Writer 2's addition was not persisted");
    }

    [Theory]
    [InlineData("Cart.DuplicateCartCreation", "Cart for this user is currently being initialized by a concurrent request.")]
    [InlineData("Cart.ConcurrencyConflict", "The cart was modified by another request. Please refresh and try again.")]
    public async Task ConflictErrors_AreProperlyMappedToHttp409Conflict(string errorCode, string description)
    {
        // Prove that CartsController / ApiControllerBase maps ErrorType.Conflict to StatusCodes.Status409Conflict
        Result<CartDto> failureResult = Error.Conflict(errorCode, description);
        var fakeSender = new FakeSender(failureResult);

        var controller = new CartsController();
        var httpContext = new DefaultHttpContext();
        var services = new ServiceCollection();
        services.AddSingleton<ISender>(fakeSender);
        httpContext.RequestServices = services.BuildServiceProvider();

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        var actionResult = await controller.AddItem(new AddCartItemRequest(Guid.NewGuid(), 1));

        actionResult.Should().BeOfType<ConflictObjectResult>();
        var conflictResult = (ConflictObjectResult)actionResult;
        conflictResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }
}

internal sealed class TestHookApplicationDbContext : IApplicationDbContext
{
    private readonly ApplicationDbContext _inner;
    private readonly Func<CancellationToken, Task>? _beforeSaveChangesAsync;

    public TestHookApplicationDbContext(
        ApplicationDbContext inner,
        Func<CancellationToken, Task>? beforeSaveChangesAsync = null)
    {
        _inner = inner;
        _beforeSaveChangesAsync = beforeSaveChangesAsync;
    }

    public DbSet<Product> Products => _inner.Products;
    public DbSet<Category> Categories => _inner.Categories;
    public DbSet<PCBuild> PCBuilds => _inner.PCBuilds;
    public DbSet<PCBuildItem> PCBuildItems => _inner.PCBuildItems;
    public DbSet<Order> Orders => _inner.Orders;
    public DbSet<OrderItem> OrderItems => _inner.OrderItems;
    public DbSet<User> Users => _inner.Users;
    public DbSet<UserAddress> UserAddresses => _inner.UserAddresses;
    public DbSet<Review> Reviews => _inner.Reviews;
    public DbSet<Comment> Comments => _inner.Comments;
    public DbSet<Wishlist> Wishlists => _inner.Wishlists;
    public DbSet<Post> Posts => _inner.Posts;
    public DbSet<PostComment> PostComments => _inner.PostComments;
    public DbSet<Cart> Carts => _inner.Carts;
    public DbSet<CartItem> CartItems => _inner.CartItems;

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (_beforeSaveChangesAsync is not null)
        {
            await _beforeSaveChangesAsync(cancellationToken);
        }

        return await _inner.SaveChangesAsync(cancellationToken);
    }

    public bool IsUniqueViolation(DbUpdateException ex, string? constraintName = null)
        => _inner.IsUniqueViolation(ex, constraintName);

    public ApplicationDbContext InnerDbContext => _inner;
}

internal sealed class TestCurrentUserService : ICurrentUserService
{
    public TestCurrentUserService(Guid? userId)
    {
        UserId = userId;
    }

    public Guid? UserId { get; }
    public string? UserEmail => "concurrency_test@riutek.test";
    public string? UserRole => "Customer";
    public bool IsAuthenticated => UserId.HasValue;
}

internal sealed class FakeSender : ISender
{
    private readonly object _response;

    public FakeSender(object response)
    {
        _response = response;
    }

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult((TResponse)_response);
    }

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
    {
        return Task.CompletedTask;
    }

    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<object?>(_response);
    }

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
