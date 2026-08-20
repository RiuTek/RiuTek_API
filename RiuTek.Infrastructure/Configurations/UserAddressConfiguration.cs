using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RiuTek.Core.Entities;

namespace RiuTek.Infrastructure.Configurations;

public class UserAddressConfiguration : IEntityTypeConfiguration<UserAddress>
{
    public void Configure(EntityTypeBuilder<UserAddress> builder)
    {
        builder.ToTable("UserAddresses");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.ReceiverName)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(a => a.PhoneNumber)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(a => a.AddressLine)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(a => a.Ward)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.District)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.City)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasOne(a => a.User)
            .WithMany(u => u.Addresses)
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
