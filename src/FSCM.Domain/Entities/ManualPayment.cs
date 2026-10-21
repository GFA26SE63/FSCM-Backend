// Manual payment record for ad-hoc COD / prepaid collection against a sales order.
// Independent of the PayOS receivable on PAYMENT; multiple manual records may exist per order.

namespace FSCM.Domain.Entities;

public sealed class ManualPayment
{
    public Guid Id { get; set; }
    public Guid SalesOrderId { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string OperationKey { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public Guid? ConfirmedByUserId { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? Note { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
