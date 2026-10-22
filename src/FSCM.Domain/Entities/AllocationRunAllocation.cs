// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

// One quantity of one batch assigned to one order line by the run. This is the plan, not a
// reservation: nothing is held in inventory until the run is approved.
public sealed class AllocationRunAllocation
{
    public Guid Id { get; set; }
    public Guid AllocationRunId { get; set; }
    public Guid AllocationRunLineId { get; set; }
    public Guid BatchId { get; set; }
    public Guid WarehouseId { get; set; }
    public decimal Quantity { get; set; }
    public int Sequence { get; set; }
    public DateTimeOffset EstimatedArrivalAt { get; set; }
    public decimal RemainingShelfLifeHours { get; set; }
    public decimal WeightedScore { get; set; }

    /// <summary>Set when a later order's one-level swap moved this quantity here.</summary>
    public Guid? SwappedFromBatchId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
