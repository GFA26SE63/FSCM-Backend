using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class FactoryProductionBatchConfiguration : IEntityTypeConfiguration<FactoryProductionBatch>
{
    private readonly bool includeWarehouseReferences;
    public FactoryProductionBatchConfiguration() : this(true) { }
    public FactoryProductionBatchConfiguration(bool includeWarehouseReferences) => this.includeWarehouseReferences = includeWarehouseReferences;
    public void Configure(EntityTypeBuilder<FactoryProductionBatch> b)
    {
        b.ToTable("FACTORY_PRODUCTION_BATCH", t => {
            t.HasCheckConstraint("CK_FACTORY_BATCH_Dates", "\"ProductionDate\" < \"ExpirationDate\"");
            t.HasCheckConstraint("CK_FACTORY_BATCH_Line", "\"ProductionLine\" IN ('A','B','C','D')");
            t.HasCheckConstraint("CK_FACTORY_BATCH_Count", "\"DailySequence\" BETWEEN 1 AND 9999 AND \"UnitsPerCarton\" > 0");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(80);
        b.Property(x => x.FactoryCode).HasMaxLength(12);
        b.Property(x => x.Sku).HasMaxLength(50);
        b.Property(x => x.ProductName).HasMaxLength(300);
        b.Property(x => x.ProductionLine).HasMaxLength(1);
        b.Property(x => x.OperationKey).HasMaxLength(100);
        b.Property(x => x.RequestHash).HasMaxLength(64);
        b.HasIndex(x => x.Code).IsUnique();
        b.HasIndex(x => x.OperationKey).IsUnique();
        b.HasIndex(x => new { x.FactoryCode, x.ProductionDate, x.DailySequence }).IsUnique();
        if (includeWarehouseReferences)
        {
            b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        }
    }
}

public sealed class FactoryReceiptSyncConfiguration : IEntityTypeConfiguration<FactoryReceiptSync>
{
    public void Configure(EntityTypeBuilder<FactoryReceiptSync> b)
    {
        b.ToTable("FACTORY_RECEIPT_SYNC");
        b.HasKey(x => x.PalletId);
        b.Property(x => x.LastError).HasMaxLength(300);
        b.HasIndex(x => x.SyncedAt);
        b.HasOne<FactoryPallet>().WithMany().HasForeignKey(x => x.PalletId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WarehouseDocument>().WithMany().HasForeignKey(x => x.ReceiptDocumentId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class FactoryPalletConfiguration : IEntityTypeConfiguration<FactoryPallet>
{
    private readonly bool includeWarehouseReferences;
    public FactoryPalletConfiguration() : this(true) { }
    public FactoryPalletConfiguration(bool includeWarehouseReferences) => this.includeWarehouseReferences = includeWarehouseReferences;
    public void Configure(EntityTypeBuilder<FactoryPallet> b)
    {
        b.ToTable("FACTORY_PALLET", t => t.HasCheckConstraint("CK_FACTORY_PALLET_Status", "\"Status\" IN ('Packed','Dispatched','Received')"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(100);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => x.Code).IsUnique();
        b.HasOne<FactoryProductionBatch>().WithMany().HasForeignKey(x => x.ProductionBatchId).OnDelete(DeleteBehavior.Restrict);
        if (includeWarehouseReferences)
        {
            b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.DestinationWarehouseId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<WarehouseDocument>().WithMany().HasForeignKey(x => x.ReceiptDocumentId).OnDelete(DeleteBehavior.Restrict);
        }
    }
}
public sealed class FactoryCartonConfiguration : IEntityTypeConfiguration<FactoryCarton>
{
    public void Configure(EntityTypeBuilder<FactoryCarton> b)
    {
        b.ToTable("FACTORY_CARTON"); b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(120);
        b.HasIndex(x => x.Code).IsUnique();
        b.HasOne<FactoryPallet>().WithMany().HasForeignKey(x => x.PalletId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class FactoryProductUnitConfiguration : IEntityTypeConfiguration<FactoryProductUnit>
{
    public void Configure(EntityTypeBuilder<FactoryProductUnit> b)
    {
        b.ToTable("FACTORY_PRODUCT_UNIT"); b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(160);
        b.HasIndex(x => x.Code).IsUnique();
        b.HasOne<FactoryCarton>().WithMany().HasForeignKey(x => x.CartonId).OnDelete(DeleteBehavior.Restrict);
    }
}
