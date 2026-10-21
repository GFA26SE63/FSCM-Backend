using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class AllocationConfiguration : IEntityTypeConfiguration<Allocation>
{
    public void Configure(EntityTypeBuilder<Allocation> builder)
    {
        builder.ToTable("ALLOCATION");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.SalesOrderLineId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.BatchId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.WarehouseId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AllocatedQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.AllocationSequence).IsRequired();
        builder.Property(entity => entity.DistanceMeters).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.EstimatedArrivalAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.RemainingLifeHoursAtArrival).IsRequired();
        builder.Property(entity => entity.ShelfLifeScore).HasPrecision(9, 6).IsRequired();
        builder.Property(entity => entity.DistanceScore).HasPrecision(9, 6).IsRequired();
        builder.Property(entity => entity.CoverageScore).HasPrecision(9, 6).IsRequired();
        builder.Property(entity => entity.WeightedScore).HasPrecision(9, 6).IsRequired();
        builder.Property(entity => entity.UnitPrice).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.GrossAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.DiscountPercent).HasPrecision(5, 2).IsRequired();
        builder.Property(entity => entity.DiscountAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.NetAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.PromotionId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.DiscountSourceType).HasMaxLength(30).IsRequired(false);
        builder.Property(entity => entity.DiscountSourceCode).HasMaxLength(40).IsRequired(false);
        builder.Property(entity => entity.Status).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne<SalesOrderLine>()
            .WithMany()
            .HasForeignKey(entity => entity.SalesOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(entity => entity.BatchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(entity => entity.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Promotion>()
            .WithMany()
            .HasForeignKey(entity => entity.PromotionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(entity => new { entity.SalesOrderLineId, entity.AllocationSequence }).IsUnique();
    }
}
