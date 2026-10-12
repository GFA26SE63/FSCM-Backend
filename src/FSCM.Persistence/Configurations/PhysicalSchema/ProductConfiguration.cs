using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("PRODUCT");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ProductCategoryId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.Sku).HasMaxLength(50).IsRequired();
        builder.Property(entity => entity.Name).HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.Unit).HasMaxLength(50).IsRequired();
        builder.Property(entity => entity.ShelfLifeHours).IsRequired();
        builder.Property(entity => entity.NearDateThresholdHours).IsRequired();
        builder.Property(entity => entity.Status).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.PublishedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.InactivatedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => entity.Sku).IsUnique();
        builder.HasOne<ProductCategory>()
            .WithMany()
            .HasForeignKey(entity => entity.ProductCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
