using Microsoft.EntityFrameworkCore;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Product> Products { get; }
    DbSet<Category> Categories { get; }
    DbSet<PCBuild> PCBuilds { get; }
    DbSet<PCBuildItem> PCBuildItems { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<User> Users { get; }
    DbSet<UserAddress> UserAddresses { get; }
    DbSet<Review> Reviews { get; }
    DbSet<Comment> Comments { get; }
    DbSet<Wishlist> Wishlists { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
