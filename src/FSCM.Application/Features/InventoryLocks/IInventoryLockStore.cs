using FSCM.Domain.Entities;

namespace FSCM.Application.Features.InventoryLocks;

public sealed record BatchLockContext(
    Batch Batch, string WarehouseCode, string Sku,
    decimal PhysicalQuantity, decimal ReservedQuantity, decimal ActiveLockedQuantity);

public sealed record InventoryLockView(
    InventoryLock Lock, string BatchCode, string WarehouseCode, string Sku);

public interface IInventoryLockStore
{
    Task<bool> CanAccessWarehouseAsync(Guid userId, string role, Guid warehouseId, CancellationToken cancellationToken);

    /// <summary>Loads the batch with its balance and the current sum of ACTIVE locked quantity.</summary>
    Task<BatchLockContext?> FindBatchContextAsync(Guid batchId, CancellationToken cancellationToken);

    Task<InventoryLock?> FindForUpdateAsync(Guid lockId, CancellationToken cancellationToken);
    Task<InventoryLockView?> FindAsync(Guid lockId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<InventoryLockView>> ListAsync(
        Guid actorUserId, string actorRole, Guid? warehouseId, Guid? batchId, string? status,
        CancellationToken cancellationToken);

    void Add(InventoryLock inventoryLock);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
