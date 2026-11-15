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

public sealed class PickingStore(AppDbContext dbContext) : IPickingStore
{
    public Task<bool> CanAccessWarehouseAsync(
        Guid userId, string role, Guid warehouseId, CancellationToken cancellationToken) =>
        role is SystemRoles.Admin or SystemRoles.Operator
            ? Task.FromResult(true)
            : ReportingScopeQueries.ManagedWarehouses(dbContext, userId).AnyAsync(item => item.Id == warehouseId,
                cancellationToken);

    public async Task<IReadOnlyCollection<PickingView>> ListAsync(
        Guid userId,
        string role,
        Guid? warehouseId,
        string? status,
        CancellationToken cancellationToken)
    {
        var query = dbContext.PickingListRecords.AsNoTracking().AsQueryable();
        if (warehouseId.HasValue) query = query.Where(item => item.WarehouseId == warehouseId.Value);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(item => item.Status == status);
        if (role == SystemRoles.WarehouseKeeper)
        {
            query = query.Where(item => ReportingScopeQueries.ManagedWarehouses(dbContext, userId).Select(w => w.Id).Contains(item.WarehouseId));
        }
        else if (role is not (SystemRoles.Admin or SystemRoles.Operator))
        {
            query = query.Where(_ => false);
        }

        var codes = await query.OrderByDescending(item => item.CreatedAt)
            .Select(item => item.PickingCode).ToArrayAsync(cancellationToken);
        var results = new List<PickingView>(codes.Length);
        foreach (var code in codes)
        {
            var view = await FindAsync(code, false, cancellationToken);
            if (view is not null) results.Add(view);
        }
        return results;
    }

    public async Task<PickingView?> FindAsync(
        string pickingCode, bool forUpdate, CancellationToken cancellationToken)
    {
        var listQuery = dbContext.PickingListRecords.Where(item => item.PickingCode == pickingCode);
        if (!forUpdate) listQuery = listQuery.AsNoTracking();
        var list = await listQuery.SingleOrDefaultAsync(cancellationToken);
        if (list is null) return null;

        var purchaseOrder = await dbContext.PurchaseOrderRecords.AsNoTracking()
            .SingleAsync(item => item.Id == list.PurchaseOrderId, cancellationToken);
        var orderQuery = dbContext.SalesOrderRecords.Where(item => item.Id == purchaseOrder.SalesOrderId);
        if (!forUpdate) orderQuery = orderQuery.AsNoTracking();
        var order = await orderQuery.SingleAsync(cancellationToken);
        var warehouse = await dbContext.WarehouseRecords.AsNoTracking()
            .SingleAsync(item => item.Id == list.WarehouseId, cancellationToken);

        var itemQuery = dbContext.PickingItemRecords.Where(item => item.PickingListId == list.Id);
        if (!forUpdate) itemQuery = itemQuery.AsNoTracking();
        var items = await itemQuery.OrderBy(item => item.Id).ToArrayAsync(cancellationToken);
        var allocationIds = items.Select(item => item.AllocationId).ToArray();
        var allocationQuery = dbContext.AllocationRecords.Where(item => allocationIds.Contains(item.Id));
        if (!forUpdate) allocationQuery = allocationQuery.AsNoTracking();
        var allocations = await allocationQuery.ToDictionaryAsync(item => item.Id, cancellationToken);
        var lineIds = allocations.Values.Select(item => item.SalesOrderLineId).Distinct().ToArray();
        var lineQuery = dbContext.SalesOrderLineRecords.Where(item => lineIds.Contains(item.Id));
        if (!forUpdate) lineQuery = lineQuery.AsNoTracking();
        var lines = await lineQuery.ToDictionaryAsync(item => item.Id, cancellationToken);
        var batchIds = items.Select(item => item.SourceBatchId).Distinct().ToArray();
        var batchQuery = dbContext.BatchRecords.Where(item => batchIds.Contains(item.Id));
        if (!forUpdate) batchQuery = batchQuery.AsNoTracking();
        var batches = await batchQuery.ToDictionaryAsync(item => item.Id, cancellationToken);
        var versions = await dbContext.InventoryBalanceRecords.AsNoTracking()
            .Where(item => batchIds.Contains(item.BatchId))
            .ToDictionaryAsync(item => item.BatchId, item => item.Version, cancellationToken);
        var scans = await (
            from labelEvent in dbContext.QrLabelEventRecords.AsNoTracking()
            join label in dbContext.BatchQrLabelRecords.AsNoTracking()
                on labelEvent.BatchQrLabelId equals label.Id
            join parent in dbContext.BatchQrLabelRecords.AsNoTracking()
                on label.ParentLabelId equals (Guid?)parent.Id into parents
            from parent in parents.DefaultIfEmpty()
            join replacementEvent in dbContext.QrLabelEventRecords.AsNoTracking()
                on labelEvent.OperationKey + ":replacement" equals replacementEvent.OperationKey into replacementEvents
            from replacementEvent in replacementEvents.DefaultIfEmpty()
            join replacement in dbContext.BatchQrLabelRecords.AsNoTracking()
                on (Guid?)replacementEvent.BatchQrLabelId equals (Guid?)replacement.Id into replacements
            from replacement in replacements.DefaultIfEmpty()
            where labelEvent.EventType == QrLabelEventTypes.Picked && labelEvent.ReferenceCode == list.PickingCode
            orderby labelEvent.OccurredAt, labelEvent.Id
            select new PickingScanView(labelEvent, label.QrCode,
                parent == null || replacement == null ? label.QrCode : parent.QrCode,
                replacement == null ? null : replacement.QrCode,
                replacement == null ? 0 : replacement.Quantity)).ToArrayAsync(cancellationToken);
        var itemViews = items.Select(item =>
        {
            var allocation = allocations[item.AllocationId];
            return new PickingItemView(item, allocation, lines[allocation.SalesOrderLineId], batches[item.SourceBatchId]);
        }).ToArray();
        return new PickingView(list, order, warehouse, itemViews, scans, versions);
    }

    public async Task<IReadOnlyCollection<PickingList>> ListByOrderAsync(
        Guid orderId, CancellationToken cancellationToken) =>
        await dbContext.PickingListRecords.Where(item => dbContext.PurchaseOrderRecords.Any(po =>
                po.Id == item.PurchaseOrderId && po.SalesOrderId == orderId))
            .OrderBy(item => item.PickingCode).ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyCollection<PickingItem>> ListItemsByOrderAsync(
        Guid orderId, CancellationToken cancellationToken) =>
        await dbContext.PickingItemRecords
            .Where(item => dbContext.PickingListRecords.Any(list =>
                list.Id == item.PickingListId && dbContext.PurchaseOrderRecords.Any(po =>
                    po.Id == list.PurchaseOrderId && po.SalesOrderId == orderId)))
            .ToArrayAsync(cancellationToken);

    public Task<QrLabelEvent?> FindOperationAsync(string operationKey, CancellationToken cancellationToken) =>
        dbContext.QrLabelEventRecords.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OperationKey == operationKey, cancellationToken);

    public Task<BatchPickState?> FindBatchPickStateAsync(Guid batchId, CancellationToken cancellationToken) =>
        dbContext.BatchRecords.AsNoTracking()
            .Where(batch => batch.Id == batchId)
            .Join(dbContext.InventoryBalanceRecords.AsNoTracking(),
                batch => batch.Id, balance => balance.BatchId, (batch, balance) => new { batch, balance })
            .Select(item => new BatchPickState(
                item.batch.BatchCode, item.batch.ExpiryStatus, item.batch.ExpirationDate,
                item.balance.PhysicalQuantity, item.balance.ReservedQuantity,
                dbContext.InventoryLockRecords.AsNoTracking()
                    .Where(l => l.BatchId == batchId && l.Status == "Active")
                    .Sum(l => (decimal?)l.LockedQuantity) ?? 0m,
                dbContext.InventoryLockRecords.AsNoTracking()
                    .Any(l => l.BatchId == batchId && l.Status == "Active" && l.RequiresResolution)))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<BatchQrLabel?> FindLabelForUpdateAsync(string qrCode, CancellationToken cancellationToken) =>
        dbContext.BatchQrLabelRecords.SingleOrDefaultAsync(item => item.QrCode == qrCode, cancellationToken);

    public Task<BatchQrLabel?> FindLabelByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.BatchQrLabelRecords.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public async Task<string> NextQrCodeAsync(CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT nextval('\"QrLabelCodeSequence\"');";
            command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            var number = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
            return $"TEM-{number:000000}";
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    public async Task<string> NextShipmentCodeAsync(CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT nextval('\"ShipmentCodeSequence\"');";
            command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            var number = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
            return $"SHIP-{number:000000}";
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    public Task<bool> ShipmentExistsAsync(
        Guid salesOrderId, Guid warehouseId, CancellationToken cancellationToken) =>
        dbContext.ShipmentRecords.AsNoTracking().AnyAsync(item =>
            item.SalesOrderId == salesOrderId && item.WarehouseId == warehouseId,
            cancellationToken);

    public void AddList(PickingList list) => dbContext.PickingListRecords.Add(list);
    public void AddItem(PickingItem item) => dbContext.PickingItemRecords.Add(item);
    public void AddLabel(BatchQrLabel label) => dbContext.BatchQrLabelRecords.Add(label);
    public void AddLabelEvent(QrLabelEvent labelEvent) => dbContext.QrLabelEventRecords.Add(labelEvent);
    public void AddOrderHistory(OrderStatusHistory history) => dbContext.OrderStatusHistoryRecords.Add(history);
    public void AddShipment(Shipment shipment) => dbContext.ShipmentRecords.Add(shipment);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw PickingOperationException.Conflict("Picking, QR label, or inventory changed concurrently. Refresh and retry.");
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw PickingOperationException.Conflict("Picking operation conflicts with an existing business record.");
        }
    }

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await operation(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    public void ClearTracking() => dbContext.ChangeTracker.Clear();
}

