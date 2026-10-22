using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class AllocationRunAllocationConfiguration : IEntityTypeConfiguration<AllocationRunAllocation>
{
    public void Configure(EntityTypeBuilder<AllocationRunAllocation> builder)
    {
        builder.ToTable("ALLOCATION_RUN_ALLOCATION");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AllocationRunId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AllocationRunLineId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.BatchId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.WarehouseId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.Quantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.Sequence).IsRequired();
        builder.Property(entity => entity.EstimatedArrivalAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.RemainingShelfLifeHours).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.WeightedScore).HasPrecision(18, 6).IsRequired();
        builder.Property(entity => entity.SwappedFromBatchId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => new { entity.AllocationRunLineId, entity.BatchId }).IsUnique();
        builder.HasIndex(entity => new { entity.AllocationRunId, entity.BatchId });
        builder.HasOne<AllocationRun>()
            .WithMany()
            .HasForeignKey(entity => entity.AllocationRunId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AllocationRunLine>()
            .WithMany()
            .HasForeignKey(entity => entity.AllocationRunLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(entity => entity.BatchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
