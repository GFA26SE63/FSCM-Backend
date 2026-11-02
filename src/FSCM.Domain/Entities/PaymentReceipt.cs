namespace FSCM.Domain.Entities;

/// <summary>A verified collection event. Its allocations, not the receipt total, settle individual receivables.</summary>
public sealed class PaymentReceipt
{
    public Guid Id { get; set; }
    public string ReceiptCode { get; set; } = string.Empty;
    public Guid RetailerId { get; set; }
    public decimal ReceivedAmount { get; set; }
    public decimal UnappliedCreditAmount { get; set; }
    public PaymentReceiptStatus Status { get; set; }
    public string? Reference { get; set; }
    public string? Note { get; set; }
    public Guid? ConfirmedByUserId { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public Guid? ReversedByUserId { get; set; }
    public DateTimeOffset? ReversedAt { get; set; }
    public string? ReversalReason { get; set; }
    public string OperationKey { get; set; } = string.Empty;
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum PaymentReceiptStatus : short
{
    Draft = 0,
    Confirmed = 1,
    Reversed = 2
}
