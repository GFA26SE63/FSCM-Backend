// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class ReceiptDiscrepancyItem
{
    public Guid Id { get; set; }
    public Guid ReceiptDiscrepancyId { get; set; }
    public Guid? BatchId { get; set; }
    public string IssueType { get; set; } = string.Empty;
    public decimal ExpectedQuantity { get; set; }
    public decimal ActualReceivedQuantity { get; set; }
    public string? EvidenceBlobUrl { get; set; }
    public string? Note { get; set; }
}

