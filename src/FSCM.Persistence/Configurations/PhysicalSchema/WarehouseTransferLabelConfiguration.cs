using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class WarehouseTransferLabelConfiguration : IEntityTypeConfiguration<WarehouseTransferLabel>
{
    public void Configure(EntityTypeBuilder<WarehouseTransferLabel> builder)
    {
        builder.ToTable("WAREHOUSE_TRANSFER_LABEL");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Quantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.DispatchedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.ReceivedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.HasIndex(entity => new { entity.WarehouseTransferItemId, entity.BatchQrLabelId }).IsUnique();
        builder.HasOne<WarehouseTransferItem>().WithMany().HasForeignKey(entity => entity.WarehouseTransferItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BatchQrLabel>().WithMany().HasForeignKey(entity => entity.BatchQrLabelId).OnDelete(DeleteBehavior.Restrict);
    }
}
