using FSCM.BuildingBlocks.Infrastructure.Persistence;
using FSCM.Modules.Performance.Application;
using FSCM.Modules.Performance.Domain;
using FSCM.Modules.Performance.Domain.Facts;
using FSCM.Modules.Performance.Domain.Kpi;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Modules.Performance.Infrastructure;

/// <summary>Read model: only written by event handlers, never by controllers.</summary>
public sealed class PerformanceDbContext(DbContextOptions<PerformanceDbContext> o) : ModuleDbContext(o), IPerformanceUnitOfWork
{
    protected override string Schema => "performance";
    public DbSet<SalesOrderFact> OrderFacts => Set<SalesOrderFact>();
    public DbSet<NewStoreFact> NewStores => Set<NewStoreFact>();
    public DbSet<SalesTarget> Targets => Set<SalesTarget>();
}

internal sealed class OrderFactConfig : IEntityTypeConfiguration<SalesOrderFact>
{
    public void Configure(EntityTypeBuilder<SalesOrderFact> b)
    {
        b.ToTable("order_fact"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("order_code").HasMaxLength(12);
        b.Property(x => x.Gross).HasPrecision(14, 0); b.Property(x => x.Discount).HasPrecision(14, 0); b.Property(x => x.Net).HasPrecision(14, 0);
        b.Ignore(x => x.ClosedBySales); b.HasIndex(x => new { x.CompletedOn, x.SalesUserId });
        b.OwnsMany(x => x.Lines, l =>
        {
            l.ToTable("order_fact_line"); l.WithOwner().HasForeignKey("OrderCode"); l.HasKey("OrderCode", nameof(FactLine.Sku));
            l.Property(x => x.Gross).HasPrecision(14, 0); l.Property(x => x.Discount).HasPrecision(14, 0); l.Property(x => x.Net).HasPrecision(14, 0);
        });
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
internal sealed class NewStoreConfig : IEntityTypeConfiguration<NewStoreFact>
{ public void Configure(EntityTypeBuilder<NewStoreFact> b) { b.ToTable("new_store_fact"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("retailer_code").HasMaxLength(8); b.HasIndex(x => x.RegisteredOn); } }
internal sealed class TargetConfig : IEntityTypeConfiguration<SalesTarget>
{
    public void Configure(EntityTypeBuilder<SalesTarget> b)
    {
        b.ToTable("sales_target"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasMaxLength(60); b.Property(x => x.Period).HasMaxLength(7);
        b.Property(x => x.RevenueTarget).HasPrecision(14, 0); b.Property(x => x.WeightRevenue).HasPrecision(4, 3); b.Property(x => x.WeightNewStores).HasPrecision(4, 3); b.Property(x => x.WeightOrders).HasPrecision(4, 3);
        b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => x.Period);
    }
}
internal sealed class FactRepository(PerformanceDbContext db) : IFactRepository
{
    public Task<bool> OrderExistsAsync(string orderCode, CancellationToken ct) => db.OrderFacts.AnyAsync(f => f.Id == orderCode, ct);
    public void Add(SalesOrderFact f) => db.OrderFacts.Add(f);
    public async Task<IReadOnlyList<SalesOrderFact>> OrdersAsync(DateRange range, CancellationToken ct) => await db.OrderFacts.Where(f => f.CompletedOn >= range.From && f.CompletedOn <= range.To).ToListAsync(ct);
    public Task<bool> NewStoreExistsAsync(string retailerCode, CancellationToken ct) => db.NewStores.AnyAsync(f => f.Id == retailerCode, ct);
    public void Add(NewStoreFact f) => db.NewStores.Add(f);
    public async Task<IReadOnlyList<NewStoreFact>> NewStoresAsync(DateRange range, CancellationToken ct) => await db.NewStores.Where(f => f.RegisteredOn >= range.From && f.RegisteredOn <= range.To).ToListAsync(ct);
    public Task<SalesTarget?> TargetAsync(string period, string salesUserId, CancellationToken ct) => db.Targets.FirstOrDefaultAsync(t => t.Id == SalesTarget.Key(period, salesUserId), ct);
    public async Task<IReadOnlyList<SalesTarget>> TargetsAsync(string period, CancellationToken ct) => await db.Targets.Where(t => t.Period == period).ToListAsync(ct);
    public void Add(SalesTarget t) => db.Targets.Add(t);
}
