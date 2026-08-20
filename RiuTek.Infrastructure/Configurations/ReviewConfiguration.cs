using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RiuTek.Core.Entities;

namespace RiuTek.Infrastructure.Configurations;

public class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("Reviews");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Rating)
            .IsRequired();

        builder.Property(r => r.Title)
            .HasMaxLength(200);

        builder.Property(r => r.Content)
            .HasMaxLength(2000);

        builder.Property(r => r.Images)
            .HasColumnType("text[]");

        builder.Property(r => r.StaffReply)
            .HasMaxLength(2000);

        builder.HasOne(r => r.Product)
            .WithMany(p => p.Reviews)
            .HasForeignKey(r => r.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.User)
            .WithMany(u => u.Reviews)
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.Order)
            .WithMany()
            .HasForeignKey(r => r.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // A user can review a product only once per order
        builder.HasIndex(r => new { r.UserId, r.ProductId, r.OrderId })
            .IsUnique();
    }
}
