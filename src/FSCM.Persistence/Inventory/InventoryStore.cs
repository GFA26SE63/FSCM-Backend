using System.Data;
using FSCM.Application.Features.Inventory;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FSCM.Persistence.Inventory;

public sealed class InventoryStore(AppDbContext dbContext) : IInventoryStore, IInventoryLifecycleStore
{
    public Task<bool> CanAccessWarehouseAsync(Guid userId, string role, Guid warehouseId, CancellationToken cancellationToken) =>
        role is SystemRoles.Admin or SystemRoles.Operator
            ? Task.FromResult(true)
            : dbContext.UserWarehouseAssignmentRecords.AsNoTracking().AnyAsync(item =>
                item.UserAccountId == userId && item.WarehouseId == warehouseId && item.IsActive, cancellationToken);

    public Task<Warehouse?> FindWarehouseAsync(Guid warehouseId, CancellationToken cancellationToken) =>
        dbContext.WarehouseRecords.AsNoTracking().SingleOrDefaultAsync(item => item.Id == warehouseId, cancellationToken);

    public Task<Product?> FindProductAsync(Guid productId, CancellationToken cancellationToken) =>
        dbContext.ProductRecords.AsNoTracking().SingleOrDefaultAsync(item => item.Id == productId, cancellationToken);

    public async Task<GoodsReceiptView?> FindReceiptByIdempotencyKeyAsync(string key, CancellationToken cancellationToken)
    {
        var receiptId = await dbContext.GoodsReceiptRecords.AsNoTracking()
            .Where(item => item.IdempotencyKey == key).Select(item => (Guid?)item.Id)
            .SingleOrDefaultAsync(cancellationToken);
        return receiptId.HasValue ? await FindReceiptAsync(receiptId.Value, cancellationToken) : null;
    }

    public async Task<GoodsReceiptView?> FindReceiptAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        var header = await (
            from receipt in dbContext.GoodsReceiptRecords.AsNoTracking()
            join warehouse in dbContext.WarehouseRecords.AsNoTracking() on receipt.WarehouseId equals warehouse.Id
            where receipt.Id == receiptId
            select new { Receipt = receipt, WarehouseCode = warehouse.Code, WarehouseName = warehouse.Name })
            .SingleOrDefaultAsync(cancellationToken);
        if (header is null) return null;
        var lines = await CreateReceiptLinesQuery(receiptId, header.Receipt.WarehouseId).ToArrayAsync(cancellationToken);
        return new GoodsReceiptView(header.Receipt, header.WarehouseCode, header.WarehouseName, lines);
    }

    public async Task<IReadOnlyCollection<GoodsReceiptView>> ListReceiptsAsync(
        Guid actorUserId, string actorRole, Guid? warehouseId, CancellationToken cancellationToken)
    {
        var receipts = dbContext.GoodsReceiptRecords.AsNoTracking().AsQueryable();
        if (warehouseId.HasValue) receipts = receipts.Where(item => item.WarehouseId == warehouseId.Value);
        if (actorRole == SystemRoles.WarehouseKeeper)
            receipts = receipts.Where(item => dbContext.UserWarehouseAssignmentRecords.Any(assignment =>
                assignment.UserAccountId == actorUserId && assignment.WarehouseId == item.WarehouseId && assignment.IsActive));
        var ids = await receipts.OrderByDescending(item => item.ReceivedAt).Select(item => item.Id).ToArrayAsync(cancellationToken);
        var results = new List<GoodsReceiptView>(ids.Length);
        foreach (var id in ids)
        {
            var view = await FindReceiptAsync(id, cancellationToken);
            if (view is not null) results.Add(view);
        }
        return results;
    }

    public Task<Batch?> FindBatchForUpdateAsync(
        Guid warehouseId, Guid productId, DateOnly expirationDate, CancellationToken cancellationToken) =>
        dbContext.BatchRecords.SingleOrDefaultAsync(item => item.WarehouseId == warehouseId &&
            item.ProductId == productId && item.ExpirationDate == expirationDate, cancellationToken);

    public Task<InventoryBalance?> FindBalanceForUpdateAsync(Guid batchId, CancellationToken cancellationToken) =>
        dbContext.InventoryBalanceRecords.SingleOrDefaultAsync(item => item.BatchId == batchId, cancellationToken);

    public async Task<InventoryLifecycleView?> FindForUpdateAsync(
        Guid allocationId, CancellationToken cancellationToken)
    {
        var allocation = await dbContext.AllocationRecords
            .SingleOrDefaultAsync(item => item.Id == allocationId, cancellationToken);
        if (allocation is null) return null;
        var batch = await dbContext.BatchRecords
            .SingleOrDefaultAsync(item => item.Id == allocation.BatchId, cancellationToken);
        if (batch is null) return null;
        var balance = await dbContext.InventoryBalanceRecords
            .SingleOrDefaultAsync(item => item.BatchId == batch.Id, cancellationToken);
        if (balance is null) return null;
        var hold = await dbContext.StockHoldRecords
            .SingleOrDefaultAsync(item => item.AllocationId == allocationId, cancellationToken);
        var reservation = await dbContext.ReservationRecords
            .SingleOrDefaultAsync(item => item.AllocationId == allocationId, cancellationToken);
        return new InventoryLifecycleView(allocation, batch, balance, hold, reservation);
    }

    public async Task<IReadOnlyCollection<BatchInventoryView>> ListInventoryAsync(
        Guid actorUserId, string actorRole, Guid? warehouseId, Guid? productId, CancellationToken cancellationToken)
    {
        var batches = dbContext.BatchRecords.AsNoTracking().AsQueryable();
        if (warehouseId.HasValue) batches = batches.Where(item => item.WarehouseId == warehouseId.Value);
        if (productId.HasValue) batches = batches.Where(item => item.ProductId == productId.Value);
        if (actorRole == SystemRoles.WarehouseKeeper)
            batches = batches.Where(item => dbContext.UserWarehouseAssignmentRecords.Any(assignment =>
                assignment.UserAccountId == actorUserId && assignment.WarehouseId == item.WarehouseId && assignment.IsActive));
        return await CreateInventoryQuery(batches).ToArrayAsync(cancellationToken);
    }

    public Task<BatchInventoryView?> FindBatchInventoryAsync(Guid batchId, CancellationToken cancellationToken) =>
        CreateInventoryQuery(dbContext.BatchRecords.AsNoTracking().Where(item => item.Id == batchId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyCollection<InventoryHistoryView>> ListHistoryAsync(
        Guid actorUserId, string actorRole, Guid? warehouseId, Guid? batchId, CancellationToken cancellationToken)
    {
        var history = dbContext.InventoryTransactionRecords.AsNoTracking().AsQueryable();
        if (warehouseId.HasValue) history = history.Where(item => item.WarehouseId == warehouseId.Value);
        if (batchId.HasValue) history = history.Where(item => item.BatchId == batchId.Value);
        if (actorRole == SystemRoles.WarehouseKeeper)
            history = history.Where(item => dbContext.UserWarehouseAssignmentRecords.Any(assignment =>
                assignment.UserAccountId == actorUserId && assignment.WarehouseId == item.WarehouseId && assignment.IsActive));
        return await (
            from transaction in history
            join batch in dbContext.BatchRecords.AsNoTracking() on transaction.BatchId equals batch.Id
            join warehouse in dbContext.WarehouseRecords.AsNoTracking() on transaction.WarehouseId equals warehouse.Id
            join product in dbContext.ProductRecords.AsNoTracking() on transaction.ProductId equals product.Id
            orderby transaction.OccurredAt descending
            select new InventoryHistoryView(transaction, batch.BatchCode, warehouse.Code, product.Sku))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<string> NextReceiptCodeAsync(DateOnly businessDate, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO "BUSINESS_DOCUMENT_SEQUENCE" ("DocumentType", "BusinessDate", "LastNumber")
                VALUES ('PN', @businessDate, 1)
                ON CONFLICT ("DocumentType", "BusinessDate") DO UPDATE
                SET "LastNumber" = "BUSINESS_DOCUMENT_SEQUENCE"."LastNumber" + 1
                RETURNING "LastNumber";
                """;
            command.Parameters.Add(new NpgsqlParameter<DateOnly>("businessDate", businessDate));
            var number = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            if (number > 99) throw InventoryOperationException.Conflict("Daily goods receipt code range is exhausted.");
            return $"PN-{businessDate:yyMMdd}-{number:00}";
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    public void AddReceipt(GoodsReceipt receipt) => dbContext.GoodsReceiptRecords.Add(receipt);
    public void AddReceiptLine(GoodsReceiptLine line) => dbContext.GoodsReceiptLineRecords.Add(line);
    public void AddBatch(Batch batch) => dbContext.BatchRecords.Add(batch);
    public void AddBalance(InventoryBalance balance) => dbContext.InventoryBalanceRecords.Add(balance);
    public void AddTransaction(InventoryTransaction transaction) => dbContext.InventoryTransactionRecords.Add(transaction);
    public void AddHold(StockHold hold) => dbContext.StockHoldRecords.Add(hold);
    public void AddReservation(Reservation reservation) => dbContext.ReservationRecords.Add(reservation);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw InventoryOperationException.Conflict("Inventory was changed by another operation. Retry the request.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw InventoryOperationException.Conflict("A concurrent inventory operation created the same business record. Refresh and retry.");
        }
    }

    private IQueryable<GoodsReceiptLineView> CreateReceiptLinesQuery(Guid receiptId, Guid warehouseId) =>
        from line in dbContext.GoodsReceiptLineRecords.AsNoTracking()
        join product in dbContext.ProductRecords.AsNoTracking() on line.ProductId equals product.Id
        join batch in dbContext.BatchRecords.AsNoTracking() on new { WarehouseId = warehouseId, line.ProductId, line.ExpirationDate }
            equals new { batch.WarehouseId, batch.ProductId, batch.ExpirationDate }
        where line.GoodsReceiptId == receiptId
        orderby product.Sku, line.CreatedAt
        select new GoodsReceiptLineView(line, product.Sku, product.Name, batch.Id, batch.BatchCode);

    private IQueryable<BatchInventoryView> CreateInventoryQuery(IQueryable<Batch> batches) =>
        from batch in batches
        join balance in dbContext.InventoryBalanceRecords.AsNoTracking() on batch.Id equals balance.BatchId
        join warehouse in dbContext.WarehouseRecords.AsNoTracking() on batch.WarehouseId equals warehouse.Id
        join product in dbContext.ProductRecords.AsNoTracking() on batch.ProductId equals product.Id
        select new BatchInventoryView(batch, balance, warehouse.Code, warehouse.Name,
            product.Sku, product.Name, product.NearDateThresholdHours);
}
