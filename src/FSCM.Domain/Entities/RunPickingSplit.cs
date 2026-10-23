// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

// How one gathered quantity divides between orders. Each split keeps the allocation it came from
// and the price record that was approved for it, so a line taken from several batches keeps a
// different price per part and nothing is ever repriced while picking.
public sealed class RunPickingSplit
{
    public Guid Id { get; set; }
    public Guid RunPickingGatherId { get; set; }
    public Guid AllocationRunAllocationId { get; set; }
    public Guid AllocationRunLineId { get; set; }
    public Guid AllocationPlanPriceId { get; set; }
    public Guid SalesOrderId { get; set; }
    public Guid SalesOrderLineId { get; set; }
    public string OrderCodeSnapshot { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal PickedQuantity { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
