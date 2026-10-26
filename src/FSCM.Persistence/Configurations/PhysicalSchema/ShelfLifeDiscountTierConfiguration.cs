using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class ShelfLifeDiscountTierConfiguration : IEntityTypeConfiguration<ShelfLifeDiscountTier>
{
    public void Configure(EntityTypeBuilder<ShelfLifeDiscountTier> builder)
    {
        builder.ToTable("SHELF_LIFE_DISCOUNT_TIER");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ShelfLifeDiscountTableId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.Scope).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.ProductId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.ProductCategoryId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.MinRemainingPercent).HasPrecision(5, 2).IsRequired();
        builder.Property(entity => entity.MaxRemainingPercent).HasPrecision(5, 2).IsRequired();
        builder.Property(entity => entity.DiscountType).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.DiscountValue).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => new { entity.ShelfLifeDiscountTableId, entity.Scope, entity.ProductId, entity.MinRemainingPercent });
        builder.HasOne<ShelfLifeDiscountTable>()
            .WithMany()
            .HasForeignKey(entity => entity.ShelfLifeDiscountTableId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(entity => entity.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductCategory>()
            .WithMany()
            .HasForeignKey(entity => entity.ProductCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
