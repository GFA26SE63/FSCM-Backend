using System.Data;
using FSCM.Application.Features.QrLabels;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FSCM.Persistence.QrLabels;

public sealed class QrLabelStore(AppDbContext dbContext) : IQrLabelStore
{
    public Task<bool> CanAccessWarehouseAsync(Guid userId, string role, Guid warehouseId, CancellationToken cancellationToken) =>
        role is SystemRoles.Admin or SystemRoles.Operator
            ? Task.FromResult(true)
            : dbContext.UserWarehouseAssignmentRecords.AsNoTracking().AnyAsync(item =>
                item.UserAccountId == userId && item.WarehouseId == warehouseId && item.IsActive, cancellationToken);

    public Task<QrLabelView?> FindByCodeAsync(string qrCode, CancellationToken cancellationToken) =>
        CreateViewQuery(dbContext.BatchQrLabelRecords.AsNoTracking().Where(item => item.QrCode == qrCode))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<BatchQrLabel?> FindForUpdateAsync(string qrCode, CancellationToken cancellationToken) =>
        dbContext.BatchQrLabelRecords.SingleOrDefaultAsync(item => item.QrCode == qrCode, cancellationToken);

    public Task<QrLabelView?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        CreateViewQuery(dbContext.BatchQrLabelRecords.AsNoTracking().Where(item => item.Id == id))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyCollection<QrLabelView>> ListAsync(
        Guid userId, string role, Guid? warehouseId, Guid? batchId, CancellationToken cancellationToken)
    {
        var labels = dbContext.BatchQrLabelRecords.AsNoTracking().AsQueryable();
        if (warehouseId.HasValue) labels = labels.Where(item => item.CurrentWarehouseId == warehouseId.Value);
        if (batchId.HasValue) labels = labels.Where(item => item.BatchId == batchId.Value);
        if (role == SystemRoles.WarehouseKeeper)
            labels = labels.Where(item => dbContext.UserWarehouseAssignmentRecords.Any(assignment =>
                assignment.UserAccountId == userId && assignment.WarehouseId == item.CurrentWarehouseId && assignment.IsActive));
        return await CreateViewQuery(labels.OrderBy(item => item.QrCode)).ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<QrLabelView>> ListLineageAsync(Guid rootLabelId, CancellationToken cancellationToken) =>
        await CreateViewQuery(dbContext.BatchQrLabelRecords.AsNoTracking()
            .Where(item => item.Id == rootLabelId || item.RootLabelId == rootLabelId)
            .OrderBy(item => item.GeneratedAt).ThenBy(item => item.QrCode)).ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyCollection<QrLabelEventView>> ListEventsAsync(Guid rootLabelId, CancellationToken cancellationToken) =>
        await (from labelEvent in dbContext.QrLabelEventRecords.AsNoTracking()
               join label in dbContext.BatchQrLabelRecords.AsNoTracking() on labelEvent.BatchQrLabelId equals label.Id
               join warehouse in dbContext.WarehouseRecords.AsNoTracking() on labelEvent.WarehouseId equals warehouse.Id
               where label.Id == rootLabelId || label.RootLabelId == rootLabelId
               orderby labelEvent.OccurredAt descending
               select new QrLabelEventView(labelEvent, warehouse.Code)).ToArrayAsync(cancellationToken);

    public Task<QrLabelEvent?> FindEventAsync(string operationKey, CancellationToken cancellationToken) =>
        dbContext.QrLabelEventRecords.AsNoTracking().SingleOrDefaultAsync(item => item.OperationKey == operationKey, cancellationToken);

    public Task<QrLabelEvent?> FindLatestEventAsync(Guid labelId, CancellationToken cancellationToken) =>
        dbContext.QrLabelEventRecords.AsNoTracking().Where(item => item.BatchQrLabelId == labelId)
            .OrderByDescending(item => item.OccurredAt).FirstOrDefaultAsync(cancellationToken);

    public Task<Batch?> FindBatchAsync(Guid batchId, CancellationToken cancellationToken) =>
        dbContext.BatchRecords.AsNoTracking().SingleOrDefaultAsync(item => item.Id == batchId, cancellationToken);

    public Task<Guid?> FindGoodsReceiptIdForBatchAsync(Guid batchId, CancellationToken cancellationToken) =>
        (from batch in dbContext.BatchRecords.AsNoTracking()
         where batch.Id == batchId && batch.GoodsReceiptLineId != null
         join line in dbContext.GoodsReceiptLineRecords.AsNoTracking() on batch.GoodsReceiptLineId equals line.Id
         select (Guid?)line.GoodsReceiptId).SingleOrDefaultAsync(cancellationToken);

    public Task<InventoryBalance?> FindBalanceAsync(Guid batchId, CancellationToken cancellationToken) =>
        dbContext.InventoryBalanceRecords.AsNoTracking().SingleOrDefaultAsync(item => item.BatchId == batchId, cancellationToken);

    public Task<decimal> SumActiveQuantityAsync(Guid batchId, CancellationToken cancellationToken) =>
        dbContext.BatchQrLabelRecords.AsNoTracking()
            .Where(item => item.BatchId == batchId &&
                           (item.Status == QrLabelStatuses.Available || item.Status == QrLabelStatuses.Picked))
            .SumAsync(item => item.Quantity, cancellationToken);

    public async Task<string> NextQrCodeAsync(CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT nextval('\"QrLabelCodeSequence\"');";
            var number = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
            return $"TEM-{number:000000}";
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    public void AddLabel(BatchQrLabel label) => dbContext.BatchQrLabelRecords.Add(label);
    public void AddEvent(QrLabelEvent labelEvent) => dbContext.QrLabelEventRecords.Add(labelEvent);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw QrLabelOperationException.Conflict("QR label was changed by another operation. Refresh and retry.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw QrLabelOperationException.Conflict("A QR label operation with the same identifier already exists.");
        }
    }

    private IQueryable<QrLabelView> CreateViewQuery(IQueryable<BatchQrLabel> labels) =>
        from label in labels
        join batch in dbContext.BatchRecords.AsNoTracking() on label.BatchId equals batch.Id
        join warehouse in dbContext.WarehouseRecords.AsNoTracking() on label.CurrentWarehouseId equals warehouse.Id
        join product in dbContext.ProductRecords.AsNoTracking() on batch.ProductId equals product.Id
        join parent in dbContext.BatchQrLabelRecords.AsNoTracking() on label.ParentLabelId equals parent.Id into parents
        from parent in parents.DefaultIfEmpty()
        join root in dbContext.BatchQrLabelRecords.AsNoTracking() on label.RootLabelId equals root.Id into roots
        from root in roots.DefaultIfEmpty()
        join receipt in dbContext.GoodsReceiptRecords.AsNoTracking() on label.GoodsReceiptId equals receipt.Id into receipts
        from receipt in receipts.DefaultIfEmpty()
        select new QrLabelView(label, batch, warehouse, product,
            parent == null ? null : parent.QrCode, root == null ? label.QrCode : root.QrCode,
            receipt == null ? null : receipt.ReceiptCode);
}
