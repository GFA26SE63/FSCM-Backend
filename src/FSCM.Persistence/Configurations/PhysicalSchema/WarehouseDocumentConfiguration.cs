using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class WarehouseDocumentConfiguration : IEntityTypeConfiguration<WarehouseDocument>
{
    public void Configure(EntityTypeBuilder<WarehouseDocument> b)
    {
        b.ToTable("WAREHOUSE_DOCUMENT");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(80);
        b.Property(x => x.OperationKey).HasMaxLength(120);
        b.Property(x => x.RequestHash).HasMaxLength(64);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => x.Code).IsUnique();
        b.HasIndex(x => x.OperationKey).IsUnique();
        b.HasIndex(x => x.SourceDocumentId).IsUnique();
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.DestinationWarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WarehouseDocument>().WithMany().HasForeignKey(x => x.SourceDocumentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<GoodsReceipt>().WithMany().HasForeignKey(x => x.GoodsReceiptId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class WarehouseDocumentLineConfiguration : IEntityTypeConfiguration<WarehouseDocumentLine>
{
    public void Configure(EntityTypeBuilder<WarehouseDocumentLine> b)
    {
        b.ToTable("WAREHOUSE_DOCUMENT_LINE");
        b.HasKey(x => x.Id);
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.HasOne<WarehouseDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Batch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PickingItem>().WithMany().HasForeignKey(x => x.PickingItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PickingList>().WithMany().HasForeignKey(x => x.PickingListId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.DocumentId, x.PickingItemId }).IsUnique();
    }
}

public sealed class WarehouseDocumentCartonConfiguration : IEntityTypeConfiguration<WarehouseDocumentCarton>
{
    public void Configure(EntityTypeBuilder<WarehouseDocumentCarton> b)
    {
        b.ToTable("WAREHOUSE_DOCUMENT_CARTON");
        b.HasKey(x => x.Id);
        b.Property(x => x.CartonId).HasMaxLength(120);
        b.HasIndex(x => new { x.DocumentId, x.CartonId }).IsUnique();
        b.HasOne<WarehouseDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WarehouseDocumentLine>().WithMany().HasForeignKey(x => x.LineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BatchQrLabel>().WithMany().HasForeignKey(x => x.LabelId).OnDelete(DeleteBehavior.Restrict);
    }
}
