using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class WarehouseTransferEventConfiguration : IEntityTypeConfiguration<WarehouseTransferEvent>
{
    public void Configure(EntityTypeBuilder<WarehouseTransferEvent> builder)
    {
        builder.ToTable("WAREHOUSE_TRANSFER_EVENT");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.OperationKey).HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.Action).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.FromStatus).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.ToStatus).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.Note).HasMaxLength(500).IsRequired(false);
        builder.Property(entity => entity.OccurredAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => entity.OperationKey).IsUnique();
        builder.HasIndex(entity => new { entity.WarehouseTransferId, entity.OccurredAt });
        builder.HasOne<WarehouseTransfer>().WithMany().HasForeignKey(entity => entity.WarehouseTransferId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(entity => entity.PerformedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
