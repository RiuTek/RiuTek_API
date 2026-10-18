using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RiuTek.Core.Entities;

namespace RiuTek.Infrastructure.Configurations;

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements", t =>
        {
            t.HasCheckConstraint("CK_StockMovements_QuantityDelta_NonZero", "\"QuantityDelta\" <> 0");
            t.HasCheckConstraint("CK_StockMovements_StockBefore_NonNegative", "\"StockBefore\" >= 0");
            t.HasCheckConstraint("CK_StockMovements_StockAfter_NonNegative", "\"StockAfter\" >= 0");
        });

        builder.HasKey(m => m.Id);

        builder.Property(m => m.ProductId)
            .IsRequired();

        builder.Property(m => m.QuantityDelta)
            .IsRequired();

        builder.Property(m => m.StockBefore)
            .IsRequired();

        builder.Property(m => m.StockAfter)
            .IsRequired();

        builder.Property(m => m.Reason)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(m => m.Note)
            .HasMaxLength(500);

        builder.Property(m => m.CreatedAt)
            .IsRequired();

        builder.HasOne(m => m.Product)
            .WithMany(p => p.StockMovements)
            .HasForeignKey(m => m.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.ActorUser)
            .WithMany()
            .HasForeignKey(m => m.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.Order)
            .WithMany()
            .HasForeignKey(m => m.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // Index for history pagination
        builder.HasIndex(m => new { m.ProductId, m.CreatedAt, m.Id });

        // Index for order audit
        builder.HasIndex(m => m.OrderId);
    }
}
