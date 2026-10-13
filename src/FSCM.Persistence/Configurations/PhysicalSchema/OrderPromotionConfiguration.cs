using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class OrderPromotionConfiguration : IEntityTypeConfiguration<OrderPromotion>
{
    public void Configure(EntityTypeBuilder<OrderPromotion> builder)
    {
        builder.ToTable("ORDER_PROMOTION");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.SalesOrderLineId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.PromotionId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.DiscountPercent).HasPrecision(5, 2).IsRequired();
        builder.Property(entity => entity.DiscountAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.IsServerFinal).IsRequired();
        builder.Property(entity => entity.AppliedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne<SalesOrderLine>()
            .WithMany()
            .HasForeignKey(entity => entity.SalesOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Promotion>()
            .WithMany()
            .HasForeignKey(entity => entity.PromotionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
