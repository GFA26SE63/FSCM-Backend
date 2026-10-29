using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class AllocationTraceScoreConfiguration : IEntityTypeConfiguration<AllocationTraceScore>
{
    public void Configure(EntityTypeBuilder<AllocationTraceScore> builder)
    {
        builder.ToTable("ALLOCATION_TRACE_SCORE");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AllocationLineTraceId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.Iteration).IsRequired();
        builder.Property(entity => entity.Rank).IsRequired();
        builder.Property(entity => entity.BatchId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.BatchCodeSnapshot).HasMaxLength(50).IsRequired();
        builder.Property(entity => entity.WarehouseId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.WarehouseCodeSnapshot).HasMaxLength(50).IsRequired();
        builder.Property(entity => entity.RemainingDemand).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.ShelfLifeScore).HasPrecision(9, 6).IsRequired();
        builder.Property(entity => entity.DistanceScore).HasPrecision(9, 6).IsRequired();
        builder.Property(entity => entity.CoverageScore).HasPrecision(9, 6).IsRequired();
        builder.Property(entity => entity.WeightedScore).HasPrecision(9, 6).IsRequired();
        builder.Property(entity => entity.Selected).IsRequired();
        builder.Property(entity => entity.SelectedQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => new { entity.AllocationLineTraceId, entity.Iteration, entity.BatchId }).IsUnique();
        builder.HasOne<AllocationLineTrace>()
            .WithMany()
            .HasForeignKey(entity => entity.AllocationLineTraceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
