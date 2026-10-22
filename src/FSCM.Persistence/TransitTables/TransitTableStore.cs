using FSCM.Application.Features.TransitTables;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.TransitTables;

public sealed class TransitTableStore(AppDbContext dbContext) : ITransitTableStore
{
    public Task<TransitTableView?> FindCurrentAsync(CancellationToken cancellationToken) =>
        CreateViewQuery(dbContext.TransitTableRecords.AsNoTracking().Where(item => item.IsActive))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<TransitTableView?> FindByVersionAsync(int version, CancellationToken cancellationToken) =>
        CreateViewQuery(dbContext.TransitTableRecords.AsNoTracking().Where(item => item.Version == version))
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// The version in force at the given moment. EffectiveTo is exclusive, so the boundary instant
    /// belongs to the version that starts there.
    /// </summary>
    public Task<TransitTableView?> FindEffectiveAtAsync(DateTimeOffset at, CancellationToken cancellationToken) =>
        CreateViewQuery(dbContext.TransitTableRecords.AsNoTracking()
                .Where(item => item.EffectiveFrom <= at && (item.EffectiveTo == null || item.EffectiveTo > at)))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyCollection<TransitTableView>> ListAsync(CancellationToken cancellationToken) =>
        await CreateViewQuery(dbContext.TransitTableRecords.AsNoTracking()
                .OrderByDescending(item => item.Version))
            .ToArrayAsync(cancellationToken);

    public Task<TransitTable?> FindCurrentForUpdateAsync(CancellationToken cancellationToken) =>
        dbContext.TransitTableRecords.SingleOrDefaultAsync(item => item.IsActive, cancellationToken);

    public async Task<int> MaxVersionAsync(CancellationToken cancellationToken) =>
        await dbContext.TransitTableRecords.AnyAsync(cancellationToken)
            ? await dbContext.TransitTableRecords.MaxAsync(item => item.Version, cancellationToken)
            : 0;

    public void AddTable(TransitTable table) => dbContext.TransitTableRecords.Add(table);

    public void AddBands(IEnumerable<TransitTableBand> bands) => dbContext.TransitTableBandRecords.AddRange(bands);

    public void AddAuditLog(AuditLog auditLog) => dbContext.AuditLogRecords.Add(auditLog);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    private IQueryable<TransitTableView> CreateViewQuery(IQueryable<TransitTable> tables) =>
        tables.Select(table => new TransitTableView(
            table,
            dbContext.TransitTableBandRecords.AsNoTracking()
                .Where(band => band.TransitTableId == table.Id)
                .OrderBy(band => band.MinDistanceMeters)
                .ToArray()));
}
