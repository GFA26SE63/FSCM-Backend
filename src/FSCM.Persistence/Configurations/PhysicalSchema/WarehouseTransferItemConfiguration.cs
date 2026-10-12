using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class WarehouseTransferItemConfiguration : IEntityTypeConfiguration<WarehouseTransferItem>
{
    public void Configure(EntityTypeBuilder<WarehouseTransferItem> builder)
    {
        builder.ToTable("WAREHOUSE_TRANSFER_ITEM");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.WarehouseTransferId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.SourceBatchId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.DestinationBatchId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.RequestedQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.DispatchedQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.ReceivedQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.Status).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.Note).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne<WarehouseTransfer>()
            .WithMany()
            .HasForeignKey(entity => entity.WarehouseTransferId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(entity => entity.SourceBatchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(entity => entity.DestinationBatchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
