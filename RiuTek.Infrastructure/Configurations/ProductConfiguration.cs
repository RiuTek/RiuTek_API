using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;

namespace RiuTek.Infrastructure.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(p => p.Slug)
            .IsRequired()
            .HasMaxLength(255);

        builder.HasIndex(p => p.Slug)
            .IsUnique();

        builder.Property(p => p.Sku)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(p => p.Sku)
            .IsUnique();

        builder.Property(p => p.Brand)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.Price)
            .HasPrecision(18, 2);

        builder.Property(p => p.OriginalPrice)
            .HasPrecision(18, 2);

        builder.Property(p => p.ImageUrl)
            .HasMaxLength(1000);

        // Native PostgreSQL JSONB for polymorphic hardware specifications
        var specConverter = new ValueConverter<ComponentSpecification, string>(
            v => JsonSerializer.Serialize(v, JsonOptions),
            v => JsonSerializer.Deserialize<ComponentSpecification>(v, JsonOptions)!);

        builder.Property(p => p.Specifications)
            .HasConversion(specConverter)
            .HasColumnType("jsonb")
            .IsRequired();

        // Native pgvector column vector(1536)
        builder.Property(p => p.Embedding)
            .HasColumnType("vector(1536)");

        // HNSW Cosine distance index for AI vector search
        builder.HasIndex(p => p.Embedding)
            .HasMethod("hnsw")
            .HasOperators("vector_cosine_ops");

        builder.HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
