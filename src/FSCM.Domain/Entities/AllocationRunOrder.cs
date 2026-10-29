// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

// An order locked into a run. Priority is frozen here: a resubmitted order keeps the acceptance
// time it first earned, so later edits to the order cannot change where it sits in the queue.
public sealed class AllocationRunOrder
{
    public Guid Id { get; set; }
    public Guid AllocationRunId { get; set; }
    public Guid SalesOrderId { get; set; }
    public string OrderCodeSnapshot { get; set; } = string.Empty;
    public Guid RetailerId { get; set; }
    public DateTimeOffset AcceptedAt { get; set; }
    public DateOnly? DeliveryDeadline { get; set; }
    public int PriorityRank { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ShortageReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
