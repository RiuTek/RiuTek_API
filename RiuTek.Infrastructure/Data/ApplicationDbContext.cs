using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Core.Entities;
using RiuTek.Core.Interfaces;

namespace RiuTek.Infrastructure.Data;

public class ApplicationDbContext : DbContext, IUnitOfWork, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
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
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Register pgvector extension
        modelBuilder.HasPostgresExtension("vector");

        // Apply all entity configurations in this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }

    public bool IsUniqueViolation(DbUpdateException ex, string? constraintName = null)
    {
        if (ex.InnerException is Npgsql.PostgresException pex && pex.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation)
        {
            return constraintName == null || string.Equals(pex.ConstraintName, constraintName, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
