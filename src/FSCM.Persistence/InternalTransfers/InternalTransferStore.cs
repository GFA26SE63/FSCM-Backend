using System.Data;
using FSCM.Application.Features.InternalTransfers;
using FSCM.Application.Features.WarehouseConfiguration;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FSCM.Persistence.InternalTransfers;

public sealed class InternalTransferStore(AppDbContext dbContext) : IInternalTransferStore
{
    public Task<bool> CanAccessWarehouseAsync(Guid userId, string role, Guid warehouseId, CancellationToken cancellationToken) =>
        role is SystemRoles.Admin or SystemRoles.Operator
            ? Task.FromResult(true)
            : dbContext.UserWarehouseAssignmentRecords.AsNoTracking().AnyAsync(item =>
                item.UserAccountId == userId && item.WarehouseId == warehouseId && item.IsActive, cancellationToken);

    public Task<Warehouse?> FindWarehouseAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.WarehouseRecords.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public async Task<TransferInventoryView?> FindInventoryForUpdateAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var batch = await dbContext.BatchRecords.SingleOrDefaultAsync(item => item.Id == batchId, cancellationToken);
        if (batch is null) return null;
        var balance = await dbContext.InventoryBalanceRecords.SingleOrDefaultAsync(
            item => item.BatchId == batchId, cancellationToken);
        if (balance is null) return null;
        var warehouse = await dbContext.WarehouseRecords.AsNoTracking().SingleAsync(
            item => item.Id == batch.WarehouseId, cancellationToken);
        var product = await dbContext.ProductRecords.AsNoTracking().SingleAsync(
            item => item.Id == batch.ProductId, cancellationToken);
        return new(batch, balance, warehouse, product);
    }

    public async Task<IReadOnlyCollection<TransferInventoryView>> ListSuggestionInventoryAsync(CancellationToken cancellationToken) =>
        await (from batch in dbContext.BatchRecords.AsNoTracking()
               join balance in dbContext.InventoryBalanceRecords.AsNoTracking() on batch.Id equals balance.BatchId
               join warehouse in dbContext.WarehouseRecords.AsNoTracking() on batch.WarehouseId equals warehouse.Id
               join product in dbContext.ProductRecords.AsNoTracking() on batch.ProductId equals product.Id
               select new TransferInventoryView(batch, balance, warehouse, product)).ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyCollection<Warehouse>> ListActiveRegionalWarehousesAsync(CancellationToken cancellationToken) =>
        await dbContext.WarehouseRecords.AsNoTracking().Where(item => item.Type == WarehouseTypes.Regional && item.IsActive)
            .OrderBy(item => item.Code).ToArrayAsync(cancellationToken);

    public async Task<int> GetRegionalMinimumStockAsync(CancellationToken cancellationToken)
    {
        var value = await dbContext.SystemConfigurationRecords.AsNoTracking()
            .Where(item => item.ConfigKey == BusinessConfigurationKeys.RegionalMinimumStockCartons && item.IsActive)
            .OrderByDescending(item => item.EffectiveFrom).Select(item => item.ConfigValue)
            .FirstOrDefaultAsync(cancellationToken);
        return int.TryParse(value, out var threshold) && threshold >= 0 ? threshold : 35;
    }

    public Task<WarehouseTransferEvent?> FindEventAsync(string operationKey, CancellationToken cancellationToken) =>
        dbContext.WarehouseTransferEventRecords.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OperationKey == operationKey, cancellationToken);

    public async Task<TransferView?> FindTransferAsync(Guid transferId, bool tracking, CancellationToken cancellationToken)
    {
        var transfers = tracking ? dbContext.WarehouseTransferRecords : dbContext.WarehouseTransferRecords.AsNoTracking();
        var transfer = await transfers.SingleOrDefaultAsync(item => item.Id == transferId, cancellationToken);
        if (transfer is null) return null;
        var source = await dbContext.WarehouseRecords.AsNoTracking().SingleAsync(item => item.Id == transfer.SourceWarehouseId, cancellationToken);
        var destination = await dbContext.WarehouseRecords.AsNoTracking().SingleAsync(item => item.Id == transfer.DestinationWarehouseId, cancellationToken);
        var itemsQuery = tracking ? dbContext.WarehouseTransferItemRecords : dbContext.WarehouseTransferItemRecords.AsNoTracking();
        var items = await itemsQuery.Where(item => item.WarehouseTransferId == transferId)
            .OrderBy(item => item.CreatedAt).ToArrayAsync(cancellationToken);
        var itemViews = new List<TransferItemView>(items.Length);
        foreach (var item in items)
        {
            var sourceBatch = await dbContext.BatchRecords.AsNoTracking().SingleAsync(candidate => candidate.Id == item.SourceBatchId, cancellationToken);
            var product = await dbContext.ProductRecords.AsNoTracking().SingleAsync(candidate => candidate.Id == sourceBatch.ProductId, cancellationToken);
            var destinationBatchCode = item.DestinationBatchId.HasValue
                ? await dbContext.BatchRecords.AsNoTracking().Where(candidate => candidate.Id == item.DestinationBatchId.Value)
                    .Select(candidate => candidate.BatchCode).SingleAsync(cancellationToken)
                : null;
            var linksQuery = tracking ? dbContext.WarehouseTransferLabelRecords : dbContext.WarehouseTransferLabelRecords.AsNoTracking();
            var links = await linksQuery.Where(link => link.WarehouseTransferItemId == item.Id).ToArrayAsync(cancellationToken);
            var labelViews = new List<TransferLabelView>(links.Length);
            foreach (var link in links)
            {
                var labelQuery = tracking ? dbContext.BatchQrLabelRecords : dbContext.BatchQrLabelRecords.AsNoTracking();
                var label = await labelQuery.SingleAsync(candidate => candidate.Id == link.BatchQrLabelId, cancellationToken);
                labelViews.Add(new(link, label));
            }
            itemViews.Add(new(item, sourceBatch, product, destinationBatchCode, labelViews));
        }
        var events = await dbContext.WarehouseTransferEventRecords.AsNoTracking()
            .Where(item => item.WarehouseTransferId == transferId).OrderBy(item => item.OccurredAt)
            .ToArrayAsync(cancellationToken);
        return new(transfer, source, destination, itemViews, events);
    }

    public async Task<IReadOnlyCollection<TransferView>> ListTransfersAsync(
        Guid userId, string role, Guid? warehouseId, string? status, CancellationToken cancellationToken)
    {
        var query = dbContext.WarehouseTransferRecords.AsNoTracking().AsQueryable();
        if (warehouseId.HasValue)
            query = query.Where(item => item.SourceWarehouseId == warehouseId || item.DestinationWarehouseId == warehouseId);
        if (status is not null) query = query.Where(item => item.Status == status);
        if (role == SystemRoles.WarehouseKeeper)
            query = query.Where(item => dbContext.UserWarehouseAssignmentRecords.Any(assignment =>
                assignment.UserAccountId == userId && assignment.IsActive &&
                (assignment.WarehouseId == item.SourceWarehouseId || assignment.WarehouseId == item.DestinationWarehouseId)));
        var ids = await query.OrderByDescending(item => item.RequestedAt).Select(item => item.Id).ToArrayAsync(cancellationToken);
        var results = new List<TransferView>(ids.Length);
        foreach (var id in ids)
        {
            var view = await FindTransferAsync(id, false, cancellationToken);
            if (view is not null) results.Add(view);
        }
        return results;
    }

    public async Task<IReadOnlyCollection<BatchQrLabel>> FindLabelsForUpdateAsync(
        IReadOnlyCollection<string> qrCodes, CancellationToken cancellationToken) =>
        await dbContext.BatchQrLabelRecords.Where(item => qrCodes.Contains(item.QrCode)).ToArrayAsync(cancellationToken);

    public Task<Batch?> FindDestinationBatchForUpdateAsync(
        Guid warehouseId, Guid productId, DateOnly expirationDate, CancellationToken cancellationToken) =>
        dbContext.BatchRecords.SingleOrDefaultAsync(item => item.WarehouseId == warehouseId &&
            item.ProductId == productId && item.ExpirationDate == expirationDate, cancellationToken);

    public Task<InventoryBalance?> FindBalanceForUpdateAsync(Guid batchId, CancellationToken cancellationToken) =>
        dbContext.InventoryBalanceRecords.SingleOrDefaultAsync(item => item.BatchId == batchId, cancellationToken);

    public async Task<string> NextTransferCodeAsync(DateOnly businessDate, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO "BUSINESS_DOCUMENT_SEQUENCE" ("DocumentType", "BusinessDate", "LastNumber")
                VALUES ('DC', @businessDate, 1)
                ON CONFLICT ("DocumentType", "BusinessDate") DO UPDATE
                SET "LastNumber" = "BUSINESS_DOCUMENT_SEQUENCE"."LastNumber" + 1
                RETURNING "LastNumber";
                """;
            command.Parameters.Add(new NpgsqlParameter<DateOnly>("businessDate", businessDate));
            var number = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            if (number > 99) throw InternalTransferOperationException.Conflict("Daily internal transfer code range is exhausted.");
            return $"DC-{businessDate:yyMMdd}-{number:00}";
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    public void AddTransfer(WarehouseTransfer transfer) => dbContext.WarehouseTransferRecords.Add(transfer);
    public void AddTransferItem(WarehouseTransferItem item) => dbContext.WarehouseTransferItemRecords.Add(item);
    public void AddTransferEvent(WarehouseTransferEvent transferEvent) => dbContext.WarehouseTransferEventRecords.Add(transferEvent);
    public void AddTransferLabel(WarehouseTransferLabel link) => dbContext.WarehouseTransferLabelRecords.Add(link);
    public void AddQrLabelEvent(QrLabelEvent labelEvent) => dbContext.QrLabelEventRecords.Add(labelEvent);
    public void AddBatch(Batch batch) => dbContext.BatchRecords.Add(batch);
    public void AddBalance(InventoryBalance balance) => dbContext.InventoryBalanceRecords.Add(balance);
    public void AddInventoryTransaction(InventoryTransaction transaction) => dbContext.InventoryTransactionRecords.Add(transaction);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw InternalTransferOperationException.Conflict("Inventory or transfer state changed concurrently. Refresh and retry.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw InternalTransferOperationException.Conflict("A transfer operation with the same business identifier already exists.");
        }
    }
}
