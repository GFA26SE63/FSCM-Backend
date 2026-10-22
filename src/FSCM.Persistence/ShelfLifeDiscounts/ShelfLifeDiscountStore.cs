using FSCM.Application.Features.ShelfLifeDiscounts;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.ShelfLifeDiscounts;

public sealed class ShelfLifeDiscountStore(AppDbContext dbContext) : IShelfLifeDiscountStore
{
    public Task<ShelfLifeDiscountTableView?> FindCurrentAsync(CancellationToken cancellationToken) =>
        CreateViewQuery(dbContext.ShelfLifeDiscountTableRecords.AsNoTracking().Where(item => item.IsActive))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<ShelfLifeDiscountTableView?> FindByVersionAsync(int version, CancellationToken cancellationToken) =>
        CreateViewQuery(dbContext.ShelfLifeDiscountTableRecords.AsNoTracking().Where(item => item.Version == version))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<ShelfLifeDiscountTableView?> FindEffectiveAtAsync(
        DateTimeOffset at, CancellationToken cancellationToken) =>
        CreateViewQuery(dbContext.ShelfLifeDiscountTableRecords.AsNoTracking()
                .Where(item => item.EffectiveFrom <= at && (item.EffectiveTo == null || item.EffectiveTo > at)))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyCollection<ShelfLifeDiscountTableView>> ListAsync(CancellationToken cancellationToken) =>
        await CreateViewQuery(dbContext.ShelfLifeDiscountTableRecords.AsNoTracking()
                .OrderByDescending(item => item.Version))
            .ToArrayAsync(cancellationToken);

    public Task<ShelfLifeDiscountTable?> FindCurrentForUpdateAsync(CancellationToken cancellationToken) =>
        dbContext.ShelfLifeDiscountTableRecords.SingleOrDefaultAsync(item => item.IsActive, cancellationToken);

    public async Task<int> MaxVersionAsync(CancellationToken cancellationToken) =>
        await dbContext.ShelfLifeDiscountTableRecords.AnyAsync(cancellationToken)
            ? await dbContext.ShelfLifeDiscountTableRecords.MaxAsync(item => item.Version, cancellationToken)
            : 0;

    public void AddTable(ShelfLifeDiscountTable table) => dbContext.ShelfLifeDiscountTableRecords.Add(table);

    public void AddTiers(IEnumerable<ShelfLifeDiscountTier> tiers) =>
        dbContext.ShelfLifeDiscountTierRecords.AddRange(tiers);

    public void AddAuditLog(AuditLog auditLog) => dbContext.AuditLogRecords.Add(auditLog);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    private IQueryable<ShelfLifeDiscountTableView> CreateViewQuery(IQueryable<ShelfLifeDiscountTable> tables) =>
        tables.Select(table => new ShelfLifeDiscountTableView(
            table,
            dbContext.ShelfLifeDiscountTierRecords.AsNoTracking()
                .Where(tier => tier.ShelfLifeDiscountTableId == table.Id)
                .OrderBy(tier => tier.MinRemainingPercent)
                .ToArray()));
}
