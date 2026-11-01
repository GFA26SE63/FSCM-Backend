namespace FSCM.Domain.Entities;

/// <summary>An immutable receipt-to-receivable application. Reversal is represented at receipt level.</summary>
public sealed class PaymentReceiptAllocation
{
    public Guid Id { get; set; }
    public Guid PaymentReceiptId { get; set; }
    public Guid PaymentId { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
