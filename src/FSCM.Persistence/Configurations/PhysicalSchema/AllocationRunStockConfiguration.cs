using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class AllocationRunStockConfiguration : IEntityTypeConfiguration<AllocationRunStock>
{
    public void Configure(EntityTypeBuilder<AllocationRunStock> builder)
    {
        builder.ToTable("ALLOCATION_RUN_STOCK");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AllocationRunId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.BatchId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.WarehouseId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ProductId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ExpirationDate).HasColumnType("date").IsRequired();
        builder.Property(entity => entity.SnapshotQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.RemainingQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.InventoryVersion).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();

        // One pool row per batch, so a draw-down can never be applied to two rows of the same batch.
        builder.HasIndex(entity => new { entity.AllocationRunId, entity.BatchId }).IsUnique();
        builder.HasIndex(entity => new { entity.AllocationRunId, entity.ProductId });
        builder.HasOne<AllocationRun>()
            .WithMany()
            .HasForeignKey(entity => entity.AllocationRunId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(entity => entity.BatchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
