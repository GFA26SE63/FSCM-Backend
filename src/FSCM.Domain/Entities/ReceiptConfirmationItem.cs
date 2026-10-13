// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class ReceiptConfirmationItem
{
    public Guid Id { get; set; }
    public Guid ReceiptConfirmationId { get; set; }
    public Guid BatchId { get; set; }
    public Guid BatchQrLabelId { get; set; }
    public decimal ExpectedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public DateTimeOffset ScannedAt { get; set; }
}
