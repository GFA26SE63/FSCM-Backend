using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("PRODUCT", table =>
        {
            table.HasCheckConstraint(
                "CK_PRODUCT_Status",
                "\"Status\" IN ('Draft', 'Published', 'Inactive')");
            table.HasCheckConstraint("CK_PRODUCT_ShelfLifeHours", "\"ShelfLifeHours\" > 0");
            table.HasCheckConstraint(
                "CK_PRODUCT_NearDateThresholdHours",
                "\"NearDateThresholdHours\" >= 0 AND \"NearDateThresholdHours\" < \"ShelfLifeHours\"");
            table.HasCheckConstraint(
                "CK_PRODUCT_Replacement",
                "\"ReplacesProductId\" IS NULL OR \"ReplacesProductId\" <> \"Id\"");
        });
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ProductCategoryId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ReplacesProductId).HasColumnType("uuid").IsRequired(false);
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
        builder.HasIndex(entity => entity.ReplacesProductId).IsUnique();
        builder.HasOne<ProductCategory>()
            .WithMany()
            .HasForeignKey(entity => entity.ProductCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(entity => entity.ReplacesProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
