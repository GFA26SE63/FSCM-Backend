// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class Reservation
{
    public Guid Id { get; set; }
    public Guid AllocationId { get; set; }
    public Guid BatchId { get; set; }
    public decimal Quantity { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset ReservedAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}

