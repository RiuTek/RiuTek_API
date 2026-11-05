using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RiuTek.Core.Entities;

namespace RiuTek.Infrastructure.Configurations;

public class BannerConfiguration : IEntityTypeConfiguration<Banner>
{
    public void Configure(EntityTypeBuilder<Banner> builder)
    {
        builder.ToTable("Banners", t =>
        {
            t.HasCheckConstraint("CK_Banners_DisplayOrder_NonNegative", "\"DisplayOrder\" >= 0");
        });

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Title)
            .IsRequired()
            .HasMaxLength(160);

        builder.Property(b => b.ImageObjectKey)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(b => b.ImageUrl)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(b => b.TargetUrl)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(b => b.DisplayOrder)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(b => b.IsActive)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasIndex(b => new { b.IsActive, b.DisplayOrder });
    }
}
