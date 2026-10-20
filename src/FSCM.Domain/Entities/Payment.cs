// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class Payment
{
    public Guid Id { get; set; }
    public Guid SalesOrderId { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateOnly DeliveredDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public DateOnly? PaidDate { get; set; }
    public string? CollectionMethod { get; set; }
    public string? OperationKey { get; set; }
    public string? RequestHash { get; set; }
    public long? PayOsOrderCode { get; set; }
    public string? PayOsPaymentLinkId { get; set; }
    public string? PayOsCheckoutUrl { get; set; }
    public string? PayOsQrCode { get; set; }
    public string? PayOsLinkStatus { get; set; }
    public long? PayOsExpiredAt { get; set; }
    public string? PayOsReference { get; set; }
    public Guid? ConfirmedByUserId { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? Note { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
