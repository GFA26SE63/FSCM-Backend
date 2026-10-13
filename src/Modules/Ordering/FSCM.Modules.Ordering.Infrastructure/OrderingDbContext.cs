using FSCM.BuildingBlocks.Infrastructure.Persistence;
using FSCM.Modules.Ordering.Application;
using FSCM.Modules.Ordering.Domain.Complaints;
using FSCM.Modules.Ordering.Domain.Orders;
using FSCM.Modules.Ordering.Domain.Picking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Modules.Ordering.Infrastructure;

public sealed class OrderingDbContext(DbContextOptions<OrderingDbContext> o) : ModuleDbContext(o), IOrderingUnitOfWork
{
    protected override string Schema => "ordering";
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<PickingList> PickingLists => Set<PickingList>();
    public DbSet<Complaint> Complaints => Set<Complaint>();
    public DbSet<OrderingSetting> Settings => Set<OrderingSetting>();
    public void ClearTracked() => ChangeTracker.Clear();
}

public sealed class OrderingSetting { public string Key { get; set; } = ""; public string Value { get; set; } = ""; }

internal sealed class OrderConfig : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("order"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("code").HasMaxLength(12);
        b.Property(x => x.RetailerCode).HasMaxLength(8); b.Property(x => x.RetailerName).HasMaxLength(200);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(12); b.Property(x => x.Payment).HasConversion<string>().HasMaxLength(8); b.Property(x => x.Source).HasConversion<string>().HasMaxLength(8);
        b.Property(x => x.AllocationTraceJson).HasColumnType("jsonb");
        b.Property(x => x.EstimatedDeliveryCost).HasPrecision(14, 0);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Ignore(x => x.GrossAmount); b.Ignore(x => x.PromotionAmount); b.Ignore(x => x.BatchDiscountAmount); b.Ignore(x => x.DiscountAmount);
        b.Ignore(x => x.NetAmount); b.Ignore(x => x.TotalCases); b.Ignore(x => x.ClosedBySales); b.Ignore(x => x.Warehouses);
        b.HasIndex(x => x.ClientRequestId).IsUnique();            // offline idempotency key
        b.HasIndex(x => x.Status); b.HasIndex(x => x.RetailerCode); b.HasIndex(x => x.SalesUserId); b.HasIndex(x => x.CreatedAtUtc);

        b.OwnsMany(x => x.Lines, l =>
        {
            l.ToTable("order_line"); l.WithOwner().HasForeignKey("OrderCode");
            l.HasKey("OrderCode", nameof(OrderLine.LineNo));
            l.Property(x => x.LineNo).ValueGeneratedNever();              // domain-assigned; by convention EF would treat it as store-generated
            l.Property(x => x.UnitPrice).HasPrecision(14, 0); l.Property(x => x.GrossAmount).HasPrecision(14, 0);
            l.Property(x => x.PromotionAmount).HasPrecision(14, 0); l.Property(x => x.PromotionPercent).HasPrecision(5, 2);
            l.Ignore(x => x.BatchDiscountAmount); l.Ignore(x => x.DiscountAmount); l.Ignore(x => x.NetAmount);
            l.OwnsMany(x => x.Allocations, a =>
            {
                a.ToTable("order_line_allocation"); a.WithOwner().HasForeignKey("OrderCode", "LineNo");
                a.Property<int>("Id").ValueGeneratedOnAdd();                   // shadow surrogate key: the same batch may appear once per line, but keep keys simple
                a.HasKey("Id");
                a.Property(x => x.DistanceKm).HasPrecision(7, 2); a.Property(x => x.Score).HasPrecision(5, 3);
                a.Property(x => x.TripCost).HasPrecision(14, 0); a.Property(x => x.BatchDiscountPercent).HasPrecision(5, 2); a.Property(x => x.BatchDiscountAmount).HasPrecision(14, 0);
                a.HasIndex(x => x.BatchCode); a.HasIndex(x => x.WarehouseCode);
            });
            l.Navigation(x => x.Allocations).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.OwnsMany(x => x.History, h =>
        {
            h.ToTable("order_status_history"); h.WithOwner().HasForeignKey("OrderCode");
            h.HasKey("OrderCode", nameof(OrderStatusEntry.Seq));
            // Seq is assigned by the domain. Without ValueGeneratedNever the owned-collection convention makes it store-generated, and an entry
            // appended to an already-tracked order (Approve, Dispatch…) is then seen as Modified → UPDATE of a missing row → concurrency error.
            h.Property(x => x.Seq).ValueGeneratedNever();
            h.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
        });
        b.Navigation(x => x.History).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PickingListConfig : IEntityTypeConfiguration<PickingList>
{
    public void Configure(EntityTypeBuilder<PickingList> b)
    {
        b.ToTable("picking_list"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("code").HasMaxLength(14);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(12); b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => x.OrderCode); b.HasIndex(x => new { x.WarehouseCode, x.Status });
        b.OwnsMany(x => x.Lines, l => { l.ToTable("picking_line"); l.WithOwner().HasForeignKey("PickingCode"); l.HasKey("PickingCode", nameof(PickingLine.LineNo)); l.Property(x => x.LineNo).ValueGeneratedNever(); l.Ignore(x => x.Remaining); });
        // Seq is domain-assigned; scans are appended to a tracked list, so it must not be store-generated (see order_status_history).
        b.OwnsMany(x => x.Scans, s => { s.ToTable("picking_scan"); s.WithOwner().HasForeignKey("PickingCode"); s.HasKey("PickingCode", nameof(PickingScan.Seq)); s.Property(x => x.Seq).ValueGeneratedNever(); });
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(x => x.Scans).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.IsComplete);
    }
}
internal sealed class ComplaintConfig : IEntityTypeConfiguration<Complaint>
{
    public void Configure(EntityTypeBuilder<Complaint> b)
    {
        b.ToTable("complaint"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("code").HasMaxLength(12);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10); b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => x.OrderCode); b.HasIndex(x => x.RetailerCode);
    }
}
internal sealed class OrderingSettingConfig : IEntityTypeConfiguration<OrderingSetting>
{ public void Configure(EntityTypeBuilder<OrderingSetting> b) { b.ToTable("setting"); b.HasKey(x => x.Key); } }
