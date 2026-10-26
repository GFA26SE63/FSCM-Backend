namespace FSCM.API.Contracts.InventoryLocks;

/// <summary>
/// Locks a damaged/expired quantity of a batch out of the issuable pool (F100).
/// <c>Reason</c> accepts Expired, Damaged, QualityHold, Recall, or Other.
/// </summary>
public sealed record CreateInventoryLockRequest(
    Guid BatchId, decimal Quantity, string Reason, string? Note = null);

public sealed record ReleaseInventoryLockRequest(int ExpectedVersion, string? Note = null);
