using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class PickingItemConfiguration : IEntityTypeConfiguration<PickingItem>
{
    public void Configure(EntityTypeBuilder<PickingItem> builder)
    {
        builder.ToTable("PICKING_ITEM");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.PickingListId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AllocationId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.SourceBatchId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.StockOutBatchId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.RequestedQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.PickedQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.Status).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.ScannedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.CompletedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.HasOne<PickingList>()
            .WithMany()
            .HasForeignKey(entity => entity.PickingListId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Allocation>()
            .WithMany()
            .HasForeignKey(entity => entity.AllocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(entity => entity.SourceBatchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(entity => entity.StockOutBatchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
