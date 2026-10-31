using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class AllocationPlanPriceConfiguration : IEntityTypeConfiguration<AllocationPlanPrice>
{
    public void Configure(EntityTypeBuilder<AllocationPlanPrice> builder)
    {
        builder.ToTable("ALLOCATION_PLAN_PRICE");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Ignore(entity => entity.AllocationPlanVersionId);
        builder.Property(entity => entity.AllocationRunId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AllocationRunLineId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.BatchId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.WarehouseId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.Quantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.ListUnitPrice).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.BaseNetUnitPrice).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.PricedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.BatchExpiresAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.TotalShelfLifeHours).IsRequired();
        builder.Property(entity => entity.RemainingShelfLifePercent).HasPrecision(5, 2).IsRequired();
        builder.Property(entity => entity.ShelfLifeDiscountTierId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.DiscountType).HasMaxLength(30).IsRequired(false);
        builder.Property(entity => entity.DiscountValue).HasPrecision(18, 2).IsRequired(false);
        builder.Property(entity => entity.ShelfLifeDiscountPerUnit).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.FinalNetUnitPrice).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.DiscountTableVersion).IsRequired();
        builder.Property(entity => entity.Explanation).HasMaxLength(500).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => new { entity.AllocationRunId, entity.AllocationRunLineId, entity.BatchId }).IsUnique();
        builder.HasOne<AllocationRun>()
            .WithMany()
            .HasForeignKey(entity => entity.AllocationRunId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AllocationRunLine>()
            .WithMany()
            .HasForeignKey(entity => entity.AllocationRunLineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
