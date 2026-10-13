using FSCM.BuildingBlocks.Infrastructure.Persistence;
using FSCM.Modules.Finance.Application;
using FSCM.Modules.Finance.Domain.Receivables;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Modules.Finance.Infrastructure;

public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> o) : ModuleDbContext(o), IFinanceUnitOfWork
{
    protected override string Schema => "finance";
    public DbSet<Receivable> Receivables => Set<Receivable>();
}

internal sealed class ReceivableConfig : IEntityTypeConfiguration<Receivable>
{
    public void Configure(EntityTypeBuilder<Receivable> b)
    {
        b.ToTable("receivable"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("order_code").HasMaxLength(12);
        b.Property(x => x.RetailerCode).HasMaxLength(8); b.Property(x => x.Amount).HasPrecision(14, 0);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Version).IsConcurrencyToken();     // two operators collecting the same debt → the second one fails
        b.HasIndex(x => x.Status); b.HasIndex(x => x.DueDate); b.HasIndex(x => x.SalesUserId); b.HasIndex(x => x.RetailerCode);
    }
}
internal sealed class ReceivableRepository(FinanceDbContext db) : IReceivableRepository
{
    public Task<Receivable?> GetAsync(string orderCode, CancellationToken ct) => db.Receivables.FirstOrDefaultAsync(r => r.Id == orderCode, ct);
    public async Task<IReadOnlyList<Receivable>> SearchAsync(string? salesUserId, string? retailerCode, CancellationToken ct) =>
        await db.Receivables.Where(r => (salesUserId == null || r.SalesUserId == salesUserId) && (retailerCode == null || r.RetailerCode == retailerCode)).ToListAsync(ct);
    public async Task<IReadOnlyList<Receivable>> PastDueAsync(DateOnly today, CancellationToken ct) =>
        await db.Receivables.Where(r => r.Status == ReceivableStatus.Unpaid && r.DueDate < today).ToListAsync(ct);
    public void Add(Receivable r) => db.Receivables.Add(r);
}
