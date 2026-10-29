// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class PurchaseOrder
{
    public Guid Id { get; set; }
    public string PurchaseOrderCode { get; set; } = string.Empty;
    public Guid SalesOrderId { get; set; }
    public Guid SalesOrderQuoteId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public DateTimeOffset IssuedAt { get; set; }
    public Guid IssuedByUserId { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
