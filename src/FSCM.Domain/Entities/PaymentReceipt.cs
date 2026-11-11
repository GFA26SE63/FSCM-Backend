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

/// <summary>Persisted PayOS checkout for a retailer's selected credit receivables.</summary>
public sealed class RetailerDebtPayOsCheckout
{
    public Guid Id { get; set; }
    public Guid RetailerId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Pending";
    public long PayOsOrderCode { get; set; }
    public string PaymentLinkId { get; set; } = string.Empty;
    public string CheckoutUrl { get; set; } = string.Empty;
    public string QrCode { get; set; } = string.Empty;
    public string? PayOsReference { get; set; }
    public string? WebhookHash { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public string OperationKey { get; set; } = string.Empty;
    public long? PayOsExpiredAt { get; set; }
    public Guid? PaymentReceiptId { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class RetailerDebtPayOsAllocation
{
    public Guid Id { get; set; }
    public Guid CheckoutId { get; set; }
    public Guid PaymentId { get; set; }
    public decimal Amount { get; set; }
}

public static class RetailerDebtPayOsStatuses
{
    public const string Pending = "Pending";
    public const string Paid = "Paid";
    public const string Failed = "Failed";
    public const string Expired = "Expired";
}
