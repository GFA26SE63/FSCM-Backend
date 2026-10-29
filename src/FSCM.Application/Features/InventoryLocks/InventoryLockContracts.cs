namespace FSCM.Application.Features.InventoryLocks;

public sealed record CreateInventoryLockData(
    Guid BatchId, decimal Quantity, string Reason, string? Note);

public sealed record ReleaseInventoryLockData(int ExpectedVersion, string? Note);

public sealed record InventoryLockResult(
    Guid Id, Guid BatchId, string BatchCode, Guid WarehouseId, string WarehouseCode,
    Guid ProductId, string Sku, decimal LockedQuantity, string Status, string Reason,
    bool RequiresResolution, Guid? LockedByUserId, Guid? ReleasedByUserId,
    DateTimeOffset? ReleasedAt, string? Note, int Version,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
