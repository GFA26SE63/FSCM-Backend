using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

/// <summary>
/// Maps the operational warehouse-transfer workflow.  Transfers reserve the same driver and
/// vehicle pool as sales shipments while their status is <c>InTransit</c>.
/// </summary>
public sealed class WarehouseTransferConfiguration : IEntityTypeConfiguration<WarehouseTransfer>
{
    public void Configure(EntityTypeBuilder<WarehouseTransfer> builder)
    {
        builder.ToTable("WAREHOUSE_TRANSFER");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.TransferCode).HasMaxLength(40).IsRequired();
        builder.Property(entity => entity.SourceWarehouseId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.DestinationWarehouseId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.CreatedByUserId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ApprovedByUserId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.DriverId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.VehicleId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.DriverCodeSnapshot).HasMaxLength(20).IsRequired(false);
        builder.Property(entity => entity.DriverNameSnapshot).HasMaxLength(150).IsRequired(false);
        builder.Property(entity => entity.PlateNumberSnapshot).HasMaxLength(30).IsRequired(false);
        builder.Property(entity => entity.AssignedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.Status).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.Note).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.RejectionReason).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.Version).IsConcurrencyToken().IsRequired();
        builder.Property(entity => entity.RequestedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.ApprovedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.DispatchedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.ReceivedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => entity.TransferCode).IsUnique();
        builder.HasIndex(entity => entity.SourceWarehouseId);
        builder.HasIndex(entity => entity.DestinationWarehouseId);
        builder.HasIndex(entity => entity.CreatedByUserId);
        builder.HasIndex(entity => entity.ApprovedByUserId);
        builder.HasIndex(entity => new { entity.DriverId, entity.Status });
        builder.HasIndex(entity => new { entity.VehicleId, entity.Status });
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(entity => entity.SourceWarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(entity => entity.DestinationWarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(entity => entity.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(entity => entity.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Driver>().WithMany().HasForeignKey(entity => entity.DriverId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Vehicle>().WithMany().HasForeignKey(entity => entity.VehicleId).OnDelete(DeleteBehavior.Restrict);
    }
}

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
        builder.HasIndex(entity => entity.WarehouseTransferId);
        builder.HasIndex(entity => entity.SourceBatchId);
        builder.HasIndex(entity => entity.DestinationBatchId);
        builder.HasOne<WarehouseTransfer>().WithMany().HasForeignKey(entity => entity.WarehouseTransferId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Batch>().WithMany().HasForeignKey(entity => entity.SourceBatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Batch>().WithMany().HasForeignKey(entity => entity.DestinationBatchId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class WarehouseTransferEventConfiguration : IEntityTypeConfiguration<WarehouseTransferEvent>
{
    public void Configure(EntityTypeBuilder<WarehouseTransferEvent> builder)
    {
        builder.ToTable("WAREHOUSE_TRANSFER_EVENT");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.WarehouseTransferId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.PerformedByUserId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.OperationKey).HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.Action).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.FromStatus).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.ToStatus).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.Note).HasMaxLength(500).IsRequired(false);
        builder.Property(entity => entity.OccurredAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => entity.OperationKey).IsUnique();
        builder.HasIndex(entity => new { entity.WarehouseTransferId, entity.OccurredAt });
        builder.HasIndex(entity => entity.PerformedByUserId);
        builder.HasOne<WarehouseTransfer>().WithMany().HasForeignKey(entity => entity.WarehouseTransferId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(entity => entity.PerformedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class WarehouseTransferLabelConfiguration : IEntityTypeConfiguration<WarehouseTransferLabel>
{
    public void Configure(EntityTypeBuilder<WarehouseTransferLabel> builder)
    {
        builder.ToTable("WAREHOUSE_TRANSFER_LABEL");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.WarehouseTransferItemId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.BatchQrLabelId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.Quantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.DispatchedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.ReceivedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.HasIndex(entity => entity.BatchQrLabelId);
        builder.HasIndex(entity => new { entity.WarehouseTransferItemId, entity.BatchQrLabelId }).IsUnique();
        builder.HasOne<WarehouseTransferItem>().WithMany().HasForeignKey(entity => entity.WarehouseTransferItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BatchQrLabel>().WithMany().HasForeignKey(entity => entity.BatchQrLabelId).OnDelete(DeleteBehavior.Restrict);
    }
}
