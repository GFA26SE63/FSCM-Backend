using FSCM.Application.Features.InventoryLocks;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FSCM.Persistence.InventoryLocks;

public sealed class InventoryLockStore(AppDbContext dbContext) : IInventoryLockStore
{
    public Task<bool> CanAccessWarehouseAsync(Guid userId, string role, Guid warehouseId, CancellationToken cancellationToken) =>
        role is SystemRoles.Admin or SystemRoles.Operator
            ? Task.FromResult(true)
            : dbContext.UserWarehouseAssignmentRecords.AsNoTracking().AnyAsync(item =>
                item.UserAccountId == userId && item.WarehouseId == warehouseId && item.IsActive, cancellationToken);

    public async Task<BatchLockContext?> FindBatchContextAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var row = await (
            from batch in dbContext.BatchRecords.AsNoTracking()
            join balance in dbContext.InventoryBalanceRecords.AsNoTracking() on batch.Id equals balance.BatchId
            join warehouse in dbContext.WarehouseRecords.AsNoTracking() on batch.WarehouseId equals warehouse.Id
            join product in dbContext.ProductRecords.AsNoTracking() on batch.ProductId equals product.Id
            where batch.Id == batchId
            select new
            {
                Batch = batch,
                warehouse.Code,
                product.Sku,
                balance.PhysicalQuantity,
                balance.ReservedQuantity
            }).SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;

        var activeLocked = await dbContext.InventoryLockRecords.AsNoTracking()
            .Where(item => item.BatchId == batchId && item.Status == InventoryLockStatuses.Active)
            .SumAsync(item => (decimal?)item.LockedQuantity, cancellationToken) ?? 0m;

        return new BatchLockContext(row.Batch, row.Code, row.Sku, row.PhysicalQuantity, row.ReservedQuantity, activeLocked);
    }

    public Task<InventoryLock?> FindForUpdateAsync(Guid lockId, CancellationToken cancellationToken) =>
        dbContext.InventoryLockRecords.SingleOrDefaultAsync(item => item.Id == lockId, cancellationToken);

    public Task<InventoryLockView?> FindAsync(Guid lockId, CancellationToken cancellationToken) =>
        CreateViewQuery(dbContext.InventoryLockRecords.AsNoTracking().Where(item => item.Id == lockId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyCollection<InventoryLockView>> ListAsync(
        Guid actorUserId, string actorRole, Guid? warehouseId, Guid? batchId, string? status,
        CancellationToken cancellationToken)
    {
        var locks = dbContext.InventoryLockRecords.AsNoTracking().AsQueryable();
        if (warehouseId.HasValue) locks = locks.Where(item => item.WarehouseId == warehouseId.Value);
        if (batchId.HasValue) locks = locks.Where(item => item.BatchId == batchId.Value);
        if (!string.IsNullOrWhiteSpace(status)) locks = locks.Where(item => item.Status == status);
        if (actorRole == SystemRoles.WarehouseKeeper)
            locks = locks.Where(item => dbContext.UserWarehouseAssignmentRecords.Any(assignment =>
                assignment.UserAccountId == actorUserId && assignment.WarehouseId == item.WarehouseId && assignment.IsActive));
        return await CreateViewQuery(locks.OrderByDescending(item => item.CreatedAt)).ToArrayAsync(cancellationToken);
    }

    public void Add(InventoryLock inventoryLock) => dbContext.InventoryLockRecords.Add(inventoryLock);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw InventoryLockOperationException.Conflict("Inventory lock was changed by another operation. Retry the request.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw InventoryLockOperationException.Conflict("A concurrent lock operation created the same record. Refresh and retry.");
        }
    }

    private IQueryable<InventoryLockView> CreateViewQuery(IQueryable<InventoryLock> locks) =>
        from inventoryLock in locks
        join batch in dbContext.BatchRecords.AsNoTracking() on inventoryLock.BatchId equals batch.Id
        join warehouse in dbContext.WarehouseRecords.AsNoTracking() on inventoryLock.WarehouseId equals warehouse.Id
        join product in dbContext.ProductRecords.AsNoTracking() on inventoryLock.ProductId equals product.Id
        select new InventoryLockView(inventoryLock, batch.BatchCode, warehouse.Code, product.Sku);
}
