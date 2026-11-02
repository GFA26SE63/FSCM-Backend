using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using FSCM.Persistence.Configurations;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class InventoryTransactionConfiguration : IEntityTypeConfiguration<InventoryTransaction>
{
    public void Configure(EntityTypeBuilder<InventoryTransaction> builder)
    {
        builder.ToTable("INVENTORY_TRANSACTION");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.TransactionType).HasConversion(new EnumCodeConverter("InventoryTransactionType")).HasColumnType("smallint").IsRequired();
        builder.Property(entity => entity.SourceType).HasMaxLength(50).IsRequired();
        builder.Property(entity => entity.SourceId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ReferenceCode).HasMaxLength(80).IsRequired();
        builder.Property(entity => entity.Note).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.PhysicalQuantityDelta).HasPrecision(18, 3);
        builder.Property(entity => entity.ReservedQuantityDelta).HasPrecision(18, 3);
        builder.Property(entity => entity.PhysicalQuantityAfter).HasPrecision(18, 3);
        builder.Property(entity => entity.ReservedQuantityAfter).HasPrecision(18, 3);
        builder.Property(entity => entity.AvailableQuantityAfter).HasPrecision(18, 3);
        builder.Property(entity => entity.OccurredAt).HasColumnType("timestamp with time zone");
        builder.HasIndex(entity => new { entity.BatchId, entity.OccurredAt });
        builder.HasIndex(entity => new { entity.SourceType, entity.SourceId });
        builder.HasOne<Batch>().WithMany().HasForeignKey(entity => entity.BatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(entity => entity.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(entity => entity.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GoodsReceipt>().WithMany().HasForeignKey(entity => entity.GoodsReceiptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GoodsReceiptLine>().WithMany().HasForeignKey(entity => entity.GoodsReceiptLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(entity => entity.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table => table.HasCheckConstraint("CK_INVENTORY_TRANSACTION_TransactionType", "\"TransactionType\" BETWEEN 0 AND 8"));
    }
}
