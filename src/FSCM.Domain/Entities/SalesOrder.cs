// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class SalesOrder
{
    public Guid Id { get; set; }
    public string OrderCode { get; set; } = string.Empty;
    public Guid RetailerId { get; set; }
    public string RetailerCodeSnapshot { get; set; } = string.Empty;
    public string RetailerNameSnapshot { get; set; } = string.Empty;
    public Guid SalesGroupId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public Guid ClientOrderKey { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string DeliveryAddress { get; set; } = string.Empty;
    public DateOnly? RequestedDeliveryDate { get; set; }
    public string? Note { get; set; }
    public int CreditTermDays { get; set; }
    public decimal SubtotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal FinalAmount { get; set; }
    public bool FinalPricePendingAllocation { get; set; }
    public decimal? OfflineEstimatedAmount { get; set; }
    public bool HasOfflineDifference { get; set; }
    public DateTimeOffset ServerReceivedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? RejectionReason { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
