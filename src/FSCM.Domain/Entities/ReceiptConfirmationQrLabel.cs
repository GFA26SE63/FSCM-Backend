namespace FSCM.Domain.Entities;

/// <summary>Immutable evidence that a QR label was scanned for one shipment receipt.</summary>
public sealed class ReceiptConfirmationQrLabel
{
    public Guid Id { get; set; }
    public Guid ReceiptConfirmationId { get; set; }
    public Guid BatchQrLabelId { get; set; }
    public DateTimeOffset ScannedAt { get; set; }
}
