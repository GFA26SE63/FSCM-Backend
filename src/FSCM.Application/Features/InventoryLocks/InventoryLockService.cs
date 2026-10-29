using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.InventoryLocks;

/// <summary>
/// F100 — Controls stock that must not be issued (damaged/expired) before a write-off.
/// Records a locked quantity, status, reason and handler; removes the not-yet-reserved locked
/// portion from the issuable pool WITHOUT decrementing OnHand. If a lock overlaps reserved stock,
/// it is flagged <see cref="InventoryLock.RequiresResolution"/> so a privileged user resolves it
/// (and the order is sent to "needs handling"), avoiding double-counting lock vs reserved.
/// Physical OnHand is only reduced later by a write-off document (F47).
/// </summary>
public sealed class InventoryLockService(IInventoryLockStore store, TimeProvider timeProvider)
{
    private static readonly string[] WriteRoles =
        [SystemRoles.Admin, SystemRoles.Operator, SystemRoles.WarehouseKeeper];

    public async Task<InventoryLockResult> CreateAsync(
        Guid actorUserId, string actorRole, CreateInventoryLockData data, CancellationToken cancellationToken)
    {
        EnsureWriteRole(actorRole);
        if (data.BatchId == Guid.Empty)
            throw InventoryLockOperationException.Validation("Batch identifier is required.");
        if (data.Quantity <= 0 || decimal.Truncate(data.Quantity) != data.Quantity)
            throw InventoryLockOperationException.Validation("Locked quantity must be a positive whole number of cartons.");
        var reason = NormalizeReason(data.Reason);
        var note = CleanNote(data.Note);

        var context = await store.FindBatchContextAsync(data.BatchId, cancellationToken)
            ?? throw InventoryLockOperationException.NotFound("Batch was not found.");
        await EnsureWarehouseAccessAsync(actorUserId, actorRole, context.Batch.WarehouseId, cancellationToken);

        // Total locked (existing active + new) can never exceed the physical OnHand.
        var newActiveLocked = context.ActiveLockedQuantity + data.Quantity;
        if (newActiveLocked > context.PhysicalQuantity)
            throw InventoryLockOperationException.Conflict(
                $"Locked quantity would exceed on-hand stock (on-hand {context.PhysicalQuantity:0.##}, " +
                $"already locked {context.ActiveLockedQuantity:0.##}).");

        // Unreserved stock still free after existing active locks. Anything beyond it overlaps
        // reserved stock, so this lock needs privileged resolution and must not double-count.
        var freeUnreserved = Math.Max(0, context.PhysicalQuantity - context.ReservedQuantity - context.ActiveLockedQuantity);
        var requiresResolution = data.Quantity > freeUnreserved;

        var now = timeProvider.GetUtcNow();
        var inventoryLock = new InventoryLock
        {
            Id = Guid.NewGuid(),
            BatchId = context.Batch.Id,
            WarehouseId = context.Batch.WarehouseId,
            ProductId = context.Batch.ProductId,
            LockedQuantity = data.Quantity,
            Status = InventoryLockStatuses.Active,
            Reason = reason,
            RequiresResolution = requiresResolution,
            LockedByUserId = actorUserId,
            Note = note,
            Version = 0,
            CreatedAt = now,
            UpdatedAt = now
        };
        store.Add(inventoryLock);
        await store.SaveChangesAsync(cancellationToken);
        return ToResult((await store.FindAsync(inventoryLock.Id, cancellationToken))!);
    }

    public async Task<InventoryLockResult> ReleaseAsync(
        Guid lockId, Guid actorUserId, string actorRole, ReleaseInventoryLockData data,
        CancellationToken cancellationToken)
    {
        EnsureWriteRole(actorRole);
        var inventoryLock = await store.FindForUpdateAsync(lockId, cancellationToken)
            ?? throw InventoryLockOperationException.NotFound("Inventory lock was not found.");
        await EnsureWarehouseAccessAsync(actorUserId, actorRole, inventoryLock.WarehouseId, cancellationToken);

        if (inventoryLock.Status == InventoryLockStatuses.Released)
            return ToResult((await store.FindAsync(inventoryLock.Id, cancellationToken))!);
        if (inventoryLock.Status != InventoryLockStatuses.Active)
            throw InventoryLockOperationException.Conflict("Only an active lock can be released.");
        if (inventoryLock.Version != data.ExpectedVersion)
            throw InventoryLockOperationException.Conflict(
                "Inventory lock was changed by another operation. Refresh and retry.");

        var now = timeProvider.GetUtcNow();
        inventoryLock.Status = InventoryLockStatuses.Released;
        inventoryLock.ReleasedByUserId = actorUserId;
        inventoryLock.ReleasedAt = now;
        if (!string.IsNullOrWhiteSpace(data.Note)) inventoryLock.Note = CleanNote(data.Note);
        inventoryLock.Version++;
        inventoryLock.UpdatedAt = now;
        await store.SaveChangesAsync(cancellationToken);
        return ToResult((await store.FindAsync(inventoryLock.Id, cancellationToken))!);
    }

    public async Task<InventoryLockResult> GetAsync(
        Guid lockId, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        var view = await store.FindAsync(lockId, cancellationToken)
            ?? throw InventoryLockOperationException.NotFound("Inventory lock was not found.");
        await EnsureWarehouseAccessAsync(actorUserId, actorRole, view.Lock.WarehouseId, cancellationToken);
        return ToResult(view);
    }

    public async Task<IReadOnlyCollection<InventoryLockResult>> ListAsync(
        Guid actorUserId, string actorRole, Guid? warehouseId, Guid? batchId, string? status,
        CancellationToken cancellationToken)
    {
        var normalizedStatus = NormalizeStatusFilter(status);
        return (await store.ListAsync(actorUserId, actorRole, warehouseId, batchId, normalizedStatus, cancellationToken))
            .Select(ToResult).ToArray();
    }

    private async Task EnsureWarehouseAccessAsync(Guid userId, string role, Guid warehouseId, CancellationToken cancellationToken)
    {
        if (!await store.CanAccessWarehouseAsync(userId, role, warehouseId, cancellationToken))
            throw InventoryLockOperationException.Forbidden("You cannot manage locks for this warehouse.");
    }

    private static void EnsureWriteRole(string role)
    {
        if (!WriteRoles.Contains(role, StringComparer.Ordinal))
            throw InventoryLockOperationException.Forbidden("Only Admin, Operator or Warehouse Keeper can manage inventory locks.");
    }

    private static string NormalizeReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw InventoryLockOperationException.Validation("Lock reason is required.");
        var value = reason.Trim();
        return InventoryLockReasons.All.Contains(value)
            ? value
            : throw InventoryLockOperationException.Validation(
                "Lock reason must be Expired, Damaged, QualityHold, Recall, or Other.");
    }

    private static string? NormalizeStatusFilter(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;
        var value = status.Trim();
        return InventoryLockStatuses.All.Contains(value)
            ? value
            : throw InventoryLockOperationException.Validation("Status filter is invalid.");
    }

    private static string? CleanNote(string? value)
    {
        value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (value?.Length > 500) throw InventoryLockOperationException.Validation("Note cannot exceed 500 characters.");
        return value;
    }

    private static InventoryLockResult ToResult(InventoryLockView view) => new(
        view.Lock.Id, view.Lock.BatchId, view.BatchCode, view.Lock.WarehouseId, view.WarehouseCode,
        view.Lock.ProductId, view.Sku, view.Lock.LockedQuantity, view.Lock.Status, view.Lock.Reason,
        view.Lock.RequiresResolution, view.Lock.LockedByUserId, view.Lock.ReleasedByUserId,
        view.Lock.ReleasedAt, view.Lock.Note, view.Lock.Version, view.Lock.CreatedAt, view.Lock.UpdatedAt);
}
