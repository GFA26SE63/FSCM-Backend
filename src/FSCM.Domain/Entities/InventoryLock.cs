// F100 — Inventory lock for damaged/expired stock held out of issuable quantity before a write-off.
// A lock records a quantity withheld from the issuable pool WITHOUT decrementing OnHand; the physical
// OnHand is only reduced later by a write-off document (F47). Multiple locks may exist per batch.

namespace FSCM.Domain.Entities;

public sealed class InventoryLock
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid ProductId { get; set; }
    public decimal LockedQuantity { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;

    /// <summary>True when the lock overlaps reserved stock and needs a privileged user to resolve.</summary>
    public bool RequiresResolution { get; set; }

    /// <summary>User who created the lock. Null when the nightly date-tracker job locked expired stock.</summary>
    public Guid? LockedByUserId { get; set; }
    public Guid? ReleasedByUserId { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public string? Note { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
