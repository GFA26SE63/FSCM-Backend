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
    public string? ResolutionType { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset ReportedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string RequestType { get; set; } = "Complaint";
    public string SettlementMethod { get; set; } = "DebtOffset";
    public string? ApprovedSettlementMethod { get; set; }
    public decimal RequestedQuantity { get; set; }
    public decimal? ApprovedQuantity { get; set; }
    public decimal? HandledQuantity { get; set; }
    public bool RequiresCollection { get; set; }
    public bool PhysicalHandlingCompleted { get; set; }
    public bool FinancialAdjustmentApplied { get; set; }
    public decimal DebtOffsetAmount { get; set; }
    public decimal RefundAmount { get; set; }
    public Guid? RefundId { get; set; }
    public int Version { get; set; } = 1;
    public Guid? RequestId { get; set; }
    public string? RequestHash { get; set; }
}
