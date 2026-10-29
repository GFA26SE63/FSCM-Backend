// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

// One batch as one hard filter saw it. A batch appears once per stage it reached, so a reader can
// see how far it got and where it stopped: out of stock in the pool, unreachable or too far, too
// late for the deadline, or short of MLOR at arrival.
//
// Rejections are kept, not only the survivors. "Why was this batch not used" is the question the
// trace exists to answer, and it cannot be answered from the chosen batches alone.
public sealed class AllocationTraceCandidate
{
    public Guid Id { get; set; }
    public Guid AllocationLineTraceId { get; set; }
    public Guid BatchId { get; set; }
    public string BatchCodeSnapshot { get; set; } = string.Empty;
    public Guid WarehouseId { get; set; }
    public string WarehouseCodeSnapshot { get; set; } = string.Empty;

    /// <summary>Pool, Distance, or Delivery: the filter that looked at it.</summary>
    public string Stage { get; set; } = string.Empty;
    public bool Passed { get; set; }

    /// <summary>Eligible when it survived this stage, otherwise what stopped it.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Known from the Delivery stage on, where the shelf life at arrival is worked out.</summary>
    public DateTimeOffset? EstimatedArrivalAt { get; set; }
    public decimal? RemainingShelfLifeHours { get; set; }
    public decimal? RequiredShelfLifeHours { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
