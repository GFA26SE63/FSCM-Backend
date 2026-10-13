using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class PromotionCategoryConfiguration : IEntityTypeConfiguration<PromotionCategory>
{
    public void Configure(EntityTypeBuilder<PromotionCategory> builder)
    {
        builder.ToTable("PROMOTION_CATEGORY");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.PromotionId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ProductCategoryId).HasColumnType("uuid").IsRequired();
        builder.HasOne<Promotion>()
            .WithMany()
            .HasForeignKey(entity => entity.PromotionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductCategory>()
            .WithMany()
            .HasForeignKey(entity => entity.ProductCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
