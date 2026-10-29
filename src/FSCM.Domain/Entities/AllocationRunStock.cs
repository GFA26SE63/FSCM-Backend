// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

// The run's temporary stock pool. RemainingQuantity is drawn down as lines are served and returned
// when a short order gives its tentative quantity back; no real balance moves until run approval.
public sealed class AllocationRunStock
{
    public Guid Id { get; set; }
    public Guid AllocationRunId { get; set; }
    public Guid BatchId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid ProductId { get; set; }
    public DateOnly ExpirationDate { get; set; }
    public decimal SnapshotQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
    public int InventoryVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
