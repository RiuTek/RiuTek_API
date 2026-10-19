using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RiuTek.Core.Entities;

namespace RiuTek.Infrastructure.Configurations;

public class PCBuildConfiguration : IEntityTypeConfiguration<PCBuild>
{
    public void Configure(EntityTypeBuilder<PCBuild> builder)
    {
        builder.ToTable("PCBuilds");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(b => b.Description)
            .HasMaxLength(1000);

        builder.Property(b => b.TotalPrice)
            .HasPrecision(18, 2);

        builder.Property(b => b.AiRationale)
            .HasMaxLength(2000);

        builder.Property(b => b.UpdatedAt)
            .IsConcurrencyToken();

        builder.HasOne(b => b.User)
            .WithMany(u => u.PCBuilds)
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(b => b.Items)
            .WithOne(i => i.PCBuild)
            .HasForeignKey(i => i.PCBuildId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(b => b.Items)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // Stable listing index for Customer mine queries
        builder.HasIndex(b => new { b.UserId, b.CreatedAt, b.Id });
    }
}

public class PCBuildItemConfiguration : IEntityTypeConfiguration<PCBuildItem>
{
    public void Configure(EntityTypeBuilder<PCBuildItem> builder)
    {
        builder.ToTable("PCBuildItems", t =>
        {
            t.HasCheckConstraint("CK_PCBuildItems_Quantity_Positive", "\"Quantity\" > 0");
            t.HasCheckConstraint("CK_PCBuildItems_UnitPrice_NonNegative", "\"UnitPrice\" >= 0");
        });

        builder.HasKey(i => i.Id);

        builder.Property(i => i.UnitPrice)
            .HasPrecision(18, 2);

        builder.HasOne(i => i.Product)
            .WithMany(p => p.PCBuildItems)
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique product per build
        builder.HasIndex(i => new { i.PCBuildId, i.ProductId })
            .IsUnique();
    }
}
