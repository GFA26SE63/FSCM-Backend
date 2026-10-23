// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

// Held stock, for either path that can hold it: the older per-order allocation, or an allocation
// made by a 12:00 run. Exactly one of the two links is set.
public sealed class Reservation
{
    public Guid Id { get; set; }
    public Guid? AllocationId { get; set; }
    public Guid? AllocationRunAllocationId { get; set; }
    public Guid BatchId { get; set; }
    public decimal Quantity { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset ReservedAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}
