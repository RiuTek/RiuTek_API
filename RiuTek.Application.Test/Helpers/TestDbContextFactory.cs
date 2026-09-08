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
    public DbSet<User> Users => Set<User>();
    public DbSet<UserAddress> UserAddresses => Set<UserAddress>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<Wishlist> Wishlists => Set<Wishlist>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<PostComment> PostComments => Set<PostComment>();

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
