// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class SalesOrderQuote
{
    public Guid Id { get; set; }
    public Guid SalesOrderId { get; set; }
    public Guid AllocationRunId { get; set; }
    public Guid AllocationPlanVersionId { get; set; }
    public int RevisionNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public Guid? AcceptedByUserId { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public string? RejectionReason { get; set; }
    public Guid? PurchaseOrderId { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
