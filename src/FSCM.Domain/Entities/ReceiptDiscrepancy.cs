// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class ReceiptDiscrepancy
{
    public Guid Id { get; set; }
    public Guid SalesOrderId { get; set; }
    public Guid ReceiptConfirmationId { get; set; }
    public Guid ReportedByRetailerUserId { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTimeOffset ReportedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}
