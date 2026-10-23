using System.Data;
using FSCM.Application.Features.Picking;
using FSCM.Application.Features.QrLabels;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace FSCM.Persistence.Picking;

public sealed class RunPickingStore(AppDbContext dbContext) : IRunPickingStore
{
    public Task<bool> CanAccessWarehouseAsync(Guid userId, string role, Guid warehouseId, CancellationToken ct) =>
        role is SystemRoles.Admin or SystemRoles.Operator
            ? Task.FromResult(true)
            : dbContext.UserWarehouseAssignmentRecords.AsNoTracking().AnyAsync(x =>
                x.UserAccountId == userId && x.WarehouseId == warehouseId && x.IsActive, ct);

    public async Task<IReadOnlyCollection<RunPickingWorkflowView>> ListAsync(Guid userId, string role,
        Guid? warehouseId, string? status, CancellationToken ct)
    {
        var query = dbContext.RunPickingListRecords.AsNoTracking().AsQueryable();
        if (warehouseId.HasValue) query = query.Where(x => x.WarehouseId == warehouseId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        if (role == SystemRoles.WarehouseKeeper)
            query = query.Where(x => dbContext.UserWarehouseAssignmentRecords.Any(a =>
                a.UserAccountId == userId && a.WarehouseId == x.WarehouseId && a.IsActive));
        else if (role is not (SystemRoles.Admin or SystemRoles.Operator)) query = query.Where(_ => false);
        var codes = await query.OrderByDescending(x => x.IssuedAt).Select(x => x.PickingCode).ToArrayAsync(ct);
        var result = new List<RunPickingWorkflowView>(codes.Length);
        foreach (var code in codes)
            if (await FindAsync(code, false, ct) is { } view) result.Add(view);
        return result;
    }

    public async Task<RunPickingWorkflowView?> FindAsync(string code, bool update, CancellationToken ct)
    {
        var lists = dbContext.RunPickingListRecords.Where(x => x.PickingCode == code);
        if (!update) lists = lists.AsNoTracking();
        var list = await lists.SingleOrDefaultAsync(ct);
        if (list is null) return null;
        var gathersQuery = dbContext.RunPickingGatherRecords.Where(x => x.RunPickingListId == list.Id);
        if (!update) gathersQuery = gathersQuery.AsNoTracking();
        var gathers = await gathersQuery.OrderBy(x => x.BatchCodeSnapshot).ToArrayAsync(ct);
        var gatherIds = gathers.Select(x => x.Id).ToArray();
        var splitsQuery = dbContext.RunPickingSplitRecords.Where(x => gatherIds.Contains(x.RunPickingGatherId));
        if (!update) splitsQuery = splitsQuery.AsNoTracking();
        var splits = await splitsQuery.OrderBy(x => x.OrderCodeSnapshot).ToArrayAsync(ct);
        var scans = await (from e in dbContext.QrLabelEventRecords.AsNoTracking()
            join label in dbContext.BatchQrLabelRecords.AsNoTracking() on e.BatchQrLabelId equals label.Id
            where e.EventType == QrLabelEventTypes.Picked && e.ReferenceCode == list.PickingCode
            orderby e.OccurredAt, e.Id
            select new RunPickingScanView(e, label.QrCode)).ToArrayAsync(ct);
        return new RunPickingWorkflowView(list, gathers, splits, scans);
    }

    public Task<QrLabelEvent?> FindOperationAsync(string key, CancellationToken ct) =>
        dbContext.QrLabelEventRecords.AsNoTracking().SingleOrDefaultAsync(x => x.OperationKey == key, ct);
    public Task<BatchQrLabel?> FindLabelForUpdateAsync(string code, CancellationToken ct) =>
        dbContext.BatchQrLabelRecords.SingleOrDefaultAsync(x => x.QrCode == code, ct);

    public async Task<string> NextQrCodeAsync(CancellationToken ct)
    {
        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT nextval('\"QrLabelCodeSequence\"');";
            command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            return $"TEM-{Convert.ToInt64(await command.ExecuteScalarAsync(ct)):000000}";
        }
        finally { if (openedHere) await connection.CloseAsync(); }
    }

    public void AddLabel(BatchQrLabel label) => dbContext.BatchQrLabelRecords.Add(label);
    public void AddLabelEvent(QrLabelEvent labelEvent) => dbContext.QrLabelEventRecords.Add(labelEvent);
    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try { await dbContext.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        { throw PickingOperationException.Conflict("Picking or QR data changed concurrently. Refresh and retry."); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw PickingOperationException.Conflict("Picking operation conflicts with an existing record."); }
    }
    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
            try { await operation(ct); await transaction.CommitAsync(ct); }
            catch { await transaction.RollbackAsync(ct); throw; }
        });
    }
    public void ClearTracking() => dbContext.ChangeTracker.Clear();
}
