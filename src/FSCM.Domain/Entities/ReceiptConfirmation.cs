// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class ReceiptConfirmation
{
    public Guid Id { get; set; }
    public Guid SalesOrderId { get; set; }
    public Guid ConfirmedByRetailerUserId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset ConfirmedAt { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

