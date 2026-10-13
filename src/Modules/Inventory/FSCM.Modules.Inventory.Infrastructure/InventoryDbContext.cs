using FSCM.BuildingBlocks.Infrastructure.Persistence;
using FSCM.Modules.Inventory.Application;
using FSCM.Modules.Inventory.Domain.Batches;
using FSCM.Modules.Inventory.Domain.Disposals;
using FSCM.Modules.Inventory.Domain.Receipts;
using FSCM.Modules.Inventory.Domain.Rotation;
using FSCM.Modules.Inventory.Domain.Transfers;
using FSCM.Modules.Inventory.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Modules.Inventory.Infrastructure;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> o) : ModuleDbContext(o), IInventoryUnitOfWork
{
    protected override string Schema => "inventory";
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<Label> Labels => Set<Label>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<RotationProposal> RotationProposals => Set<RotationProposal>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<Disposal> Disposals => Set<Disposal>();
    public DbSet<StockHold> Holds => Set<StockHold>();
    public DbSet<InventorySetting> Settings => Set<InventorySetting>();
    public void ClearTracked() => ChangeTracker.Clear();
}

/// <summary>Soft hold / reservation of a batch for an order (Reserved = order approved).</summary>
public sealed class StockHold { public long Id { get; set; } public string OrderCode { get; set; } = ""; public string BatchCode { get; set; } = ""; public int Quantity { get; set; } public bool Reserved { get; set; } }
public sealed class InventorySetting { public string Key { get; set; } = ""; public string Value { get; set; } = ""; }

internal sealed class WarehouseConfig : IEntityTypeConfiguration<Warehouse>
{ public void Configure(EntityTypeBuilder<Warehouse> b) { b.ToTable("warehouse"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("code").HasMaxLength(6); b.Property(x => x.Type).HasConversion<string>().HasMaxLength(10); b.Property(x => x.Version).IsConcurrencyToken(); } }
internal sealed class BatchConfig : IEntityTypeConfiguration<Batch>
{
    public void Configure(EntityTypeBuilder<Batch> b)
    {
        b.ToTable("batch", t => t.HasCheckConstraint("ck_batch_non_negative", "on_hand >= 0 AND held >= 0 AND reserved >= 0 AND on_hand - held - reserved >= 0"));
        b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("code").HasMaxLength(32);
        b.Property(x => x.OnHand).HasColumnName("on_hand"); b.Property(x => x.Held).HasColumnName("held"); b.Property(x => x.Reserved).HasColumnName("reserved");
        b.Property(x => x.State).HasConversion<string>().HasMaxLength(12); b.Ignore(x => x.Available);
        b.Property(x => x.Version).IsConcurrencyToken();           // optimistic locking
        b.HasIndex(x => new { x.Sku, x.Expiry });                 // FEFO lookup
        b.HasIndex(x => x.WarehouseCode);
    }
}
internal sealed class LabelConfig : IEntityTypeConfiguration<Label>
{ public void Configure(EntityTypeBuilder<Label> b) { b.ToTable("label"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("code").HasMaxLength(10); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(12); b.HasIndex(x => x.BatchCode); b.Property(x => x.Version).IsConcurrencyToken(); } }
internal sealed class TransferConfig : IEntityTypeConfiguration<Transfer>
{ public void Configure(EntityTypeBuilder<Transfer> b) { b.ToTable("transfer"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("code").HasMaxLength(14); b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(12); b.Property(x => x.Score).HasPrecision(5, 3); b.Ignore(x => x.DestinationBatch); b.HasIndex(x => x.LabelCode); b.Property(x => x.Version).IsConcurrencyToken(); } }
internal sealed class ProposalConfig : IEntityTypeConfiguration<RotationProposal>
{ public void Configure(EntityTypeBuilder<RotationProposal> b) { b.ToTable("rotation_proposal"); b.HasKey(x => x.Id); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10); b.Property(x => x.TraceJson).HasColumnType("jsonb"); b.Property(x => x.Score).HasPrecision(5, 3); b.Property(x => x.SalvageValue).HasPrecision(14, 0); b.Property(x => x.TripCost).HasPrecision(14, 0); b.HasIndex(x => x.RunDate); b.Property(x => x.Version).IsConcurrencyToken(); } }
internal sealed class GoodsReceiptConfig : IEntityTypeConfiguration<GoodsReceipt>
{
    public void Configure(EntityTypeBuilder<GoodsReceipt> b)
    {
        b.ToTable("goods_receipt"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("code").HasMaxLength(12);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10); b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => x.WarehouseCode);
        b.OwnsMany(x => x.Lines, m => { m.ToTable("goods_receipt_line"); m.WithOwner().HasForeignKey("ReceiptCode"); m.HasKey("ReceiptCode", nameof(GoodsReceiptLine.LineNo)); m.Property(x => x.LineNo).ValueGeneratedNever(); });
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
internal sealed class DisposalConfig : IEntityTypeConfiguration<Disposal>
{ public void Configure(EntityTypeBuilder<Disposal> b) { b.ToTable("disposal"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("code").HasMaxLength(12); b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => x.WarehouseCode); } }
internal sealed class HoldConfig : IEntityTypeConfiguration<StockHold>
{ public void Configure(EntityTypeBuilder<StockHold> b) { b.ToTable("stock_hold"); b.HasKey(x => x.Id); b.HasIndex(x => x.OrderCode); } }
internal sealed class InvSettingConfig : IEntityTypeConfiguration<InventorySetting>
{ public void Configure(EntityTypeBuilder<InventorySetting> b) { b.ToTable("setting"); b.HasKey(x => x.Key); } }
