using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RiuTek.Core.Entities;

namespace RiuTek.Infrastructure.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.OrderNumber)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(o => o.OrderNumber)
            .IsUnique();

        builder.Property(o => o.CheckoutIdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(o => new { o.UserId, o.CheckoutIdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UX_Orders_UserId_CheckoutIdempotencyKey");

        builder.Property(o => o.CustomerName)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(o => o.CustomerEmail)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(o => o.CustomerPhone)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(o => o.ShippingAddress)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(o => o.TotalAmount)
            .HasPrecision(18, 2);

        builder.Property(o => o.DiscountAmount)
            .HasPrecision(18, 2);

        builder.Property(o => o.FinalAmount)
            .HasPrecision(18, 2);

        builder.Property(o => o.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(o => o.Notes)
            .HasMaxLength(1000);

        builder.HasOne(o => o.User)
            .WithMany(u => u.Orders)
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(o => o.Items)
            .WithOne(i => i.Order)
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(o => o.Items)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(o => o.PaymentAttempts)
            .WithOne(p => p.Order)
            .HasForeignKey(p => p.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(o => o.PaymentAttempts)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class PaymentAttemptConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentAttempt> builder)
    {
        builder.ToTable("PaymentAttempts");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Amount)
            .HasPrecision(18, 2);

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(p => p.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(p => p.ProviderReference)
            .HasMaxLength(200);

        builder.Property(p => p.FailureCode)
            .HasMaxLength(100);

        builder.HasIndex(p => p.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("UX_PaymentAttempts_IdempotencyKey");

        builder.HasIndex(p => new { p.Method, p.ProviderReference })
            .IsUnique()
            .HasFilter("\"ProviderReference\" IS NOT NULL")
            .HasDatabaseName("UX_PaymentAttempts_Method_ProviderReference");

        builder.HasIndex(p => new { p.OrderId, p.CreatedAt });
    }
}

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.ProductName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(i => i.ProductSku)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(i => i.UnitPrice)
            .HasPrecision(18, 2);

        builder.Property(i => i.TotalPrice)
            .HasPrecision(18, 2);

        builder.HasOne(i => i.Product)
            .WithMany(p => p.OrderItems)
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.PCBuild)
            .WithMany()
            .HasForeignKey(i => i.PCBuildId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
