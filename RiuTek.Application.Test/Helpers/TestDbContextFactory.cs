using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Test.Helpers;

public class TestApplicationDbContext : DbContext, IApplicationDbContext
{
    public TestApplicationDbContext(DbContextOptions<TestApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<PCBuild> PCBuilds => Set<PCBuild>();
    public DbSet<PCBuildItem> PCBuildItems => Set<PCBuildItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<PaymentAttempt> PaymentAttempts => Set<PaymentAttempt>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserAddress> UserAddresses => Set<UserAddress>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<Wishlist> Wishlists => Set<Wishlist>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<PostComment> PostComments => Set<PostComment>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Ignore pgvector embedding for InMemory testing
        modelBuilder.Entity<Product>().Ignore(p => p.Embedding);

        var jsonOptions = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var specConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<RiuTek.Core.Entities.Specifications.ComponentSpecification, string>(
            v => System.Text.Json.JsonSerializer.Serialize(v, jsonOptions),
            v => System.Text.Json.JsonSerializer.Deserialize<RiuTek.Core.Entities.Specifications.ComponentSpecification>(v, jsonOptions)!);

        modelBuilder.Entity<Product>()
            .Property(p => p.Specifications)
            .HasConversion(specConverter);
    }

    public bool IsUniqueViolation(DbUpdateException ex, string? constraintName = null)
    {
        var msg = ex.InnerException?.Message;
        if (msg is null) return false;
        if (!msg.Contains("Unique", StringComparison.OrdinalIgnoreCase)) return false;
        return constraintName == null || msg.Contains(constraintName, StringComparison.OrdinalIgnoreCase);
    }

    public Func<Task<int>>? SaveChangesInterceptor { get; set; }
    public bool ClearTrackedChangesCalled { get; set; }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (SaveChangesInterceptor != null)
        {
            return SaveChangesInterceptor();
        }
        return base.SaveChangesAsync(cancellationToken);
    }

    public void ClearTrackedChanges()
    {
        ClearTrackedChangesCalled = true;
        ChangeTracker.Clear();
    }
}

public static class TestDbContextFactory
{
    public static TestApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<TestApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestApplicationDbContext(options);
    }
}
