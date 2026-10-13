using FSCM.BuildingBlocks.Infrastructure.Persistence;
using FSCM.Modules.Loyalty.Application;
using FSCM.Modules.Loyalty.Domain.Gifts;
using FSCM.Modules.Loyalty.Domain.Points;
using FSCM.Modules.Loyalty.Domain.Programs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Modules.Loyalty.Infrastructure;

public sealed class LoyaltyDbContext(DbContextOptions<LoyaltyDbContext> o) : ModuleDbContext(o), ILoyaltyUnitOfWork
{
    protected override string Schema => "loyalty";
    public DbSet<LoyaltyProgram> Programs => Set<LoyaltyProgram>();
    public DbSet<PointAccount> Accounts => Set<PointAccount>();
    public DbSet<PointLedgerEntry> Ledger => Set<PointLedgerEntry>();
    public DbSet<Gift> Gifts => Set<Gift>();
    public DbSet<GiftRedemption> Redemptions => Set<GiftRedemption>();
}

internal sealed class ProgramConfig : IEntityTypeConfiguration<LoyaltyProgram>
{
    public void Configure(EntityTypeBuilder<LoyaltyProgram> b)
    {
        b.ToTable("program"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("code").HasMaxLength(8);
        b.Property(x => x.Name).HasMaxLength(200); b.Property(x => x.AmountPerPoint).HasPrecision(14, 0); b.Property(x => x.Version).IsConcurrencyToken();
        b.OwnsMany(x => x.Retailers, m => { m.ToTable("program_retailer"); m.WithOwner().HasForeignKey("ProgramCode"); m.HasKey("ProgramCode", nameof(ProgramRetailer.RetailerCode)); });
        b.Navigation(x => x.Retailers).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
internal sealed class PointAccountConfig : IEntityTypeConfiguration<PointAccount>
{
    public void Configure(EntityTypeBuilder<PointAccount> b)
    {
        b.ToTable("point_account"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("retailer_code").HasMaxLength(8);
        b.Property(x => x.Version).IsConcurrencyToken();
    }
}
internal sealed class LedgerConfig : IEntityTypeConfiguration<PointLedgerEntry>
{
    public void Configure(EntityTypeBuilder<PointLedgerEntry> b)
    {
        b.ToTable("point_ledger"); b.HasKey(x => x.Id); b.Property(x => x.RetailerCode).HasMaxLength(8);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10); b.Property(x => x.Reference).HasMaxLength(20); b.Property(x => x.Note).HasMaxLength(300);
        b.HasIndex(x => new { x.RetailerCode, x.CreatedAtUtc }); b.HasIndex(x => x.Reference);
    }
}
internal sealed class GiftConfig : IEntityTypeConfiguration<Gift>
{
    public void Configure(EntityTypeBuilder<Gift> b) { b.ToTable("gift"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("code").HasMaxLength(8); b.Property(x => x.Name).HasMaxLength(200); b.Property(x => x.Version).IsConcurrencyToken(); }
}
internal sealed class RedemptionConfig : IEntityTypeConfiguration<GiftRedemption>
{
    public void Configure(EntityTypeBuilder<GiftRedemption> b)
    {
        b.ToTable("gift_redemption"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("code").HasMaxLength(14);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10); b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => x.RetailerCode); b.HasIndex(x => x.Status);
    }
}
internal sealed class ProgramRepository(LoyaltyDbContext db) : IProgramRepository
{
    public Task<LoyaltyProgram?> GetAsync(string code, CancellationToken ct) => db.Programs.FirstOrDefaultAsync(p => p.Id == code, ct);
    public async Task<IReadOnlyList<LoyaltyProgram>> AllAsync(CancellationToken ct) => await db.Programs.OrderBy(p => p.Id).ToListAsync(ct);
    public void Add(LoyaltyProgram p) => db.Programs.Add(p);
}
internal sealed class PointRepository(LoyaltyDbContext db) : IPointRepository
{
    public Task<PointAccount?> GetAccountAsync(string retailerCode, CancellationToken ct) => db.Accounts.FirstOrDefaultAsync(a => a.Id == retailerCode, ct);
    public void Add(PointAccount a) => db.Accounts.Add(a);
    public void Add(PointLedgerEntry e) => db.Ledger.Add(e);
    public Task<PointLedgerEntry?> EntryForOrderAsync(string retailerCode, string orderCode, CancellationToken ct) =>
        db.Ledger.FirstOrDefaultAsync(e => e.RetailerCode == retailerCode && e.Reference == orderCode, ct);
    public async Task<IReadOnlyList<PointLedgerEntry>> LedgerAsync(string retailerCode, CancellationToken ct) =>
        await db.Ledger.Where(e => e.RetailerCode == retailerCode).OrderByDescending(e => e.CreatedAtUtc).ToListAsync(ct);
}
internal sealed class GiftRepository(LoyaltyDbContext db) : IGiftRepository
{
    public Task<Gift?> GetAsync(string code, CancellationToken ct) => db.Gifts.FirstOrDefaultAsync(g => g.Id == code, ct);
    public async Task<IReadOnlyList<Gift>> AllAsync(CancellationToken ct) => await db.Gifts.OrderBy(g => g.Id).ToListAsync(ct);
    public void Add(Gift g) => db.Gifts.Add(g);
    public Task<GiftRedemption?> RedemptionAsync(string code, CancellationToken ct) => db.Redemptions.FirstOrDefaultAsync(r => r.Id == code, ct);
    public async Task<IReadOnlyList<GiftRedemption>> RedemptionsAsync(string? retailerCode, RedemptionStatus? status, CancellationToken ct) =>
        await db.Redemptions.Where(r => (retailerCode == null || r.RetailerCode == retailerCode) && (status == null || r.Status == status)).OrderByDescending(r => r.RequestedAtUtc).ToListAsync(ct);
    public void Add(GiftRedemption r) => db.Redemptions.Add(r);
}
