namespace FSCM.Domain.Entities;

public sealed class ReturnHandlingDocument
{
    public Guid Id { get; set; }
    public Guid ReceiptDiscrepancyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public decimal ApprovedQuantity { get; set; }
    public decimal? ActualQuantity { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? CompletedByUserId { get; set; }
    public string? Note { get; set; }
    public string? EvidenceReference { get; set; }
}
