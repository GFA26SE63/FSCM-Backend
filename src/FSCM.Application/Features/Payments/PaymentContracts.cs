namespace FSCM.Application.Features.Payments;

public static class PaymentStatuses
{
    public const string Unpaid = "Unpaid";
    public const string PartiallyPaid = "PartiallyPaid";
    public const string Paid = "Paid";
    public const string Overdue = "Overdue";
}

public static class PaymentCollectionMethods
{
    public const string Cash = "Cash";
    public const string BankTransfer = "BankTransfer";
    public const string DeliverySettlement = "DeliverySettlement";
}

public sealed record CollectPaymentData(
    string CollectionMethod,
    int ExpectedVersion,
    string? Note);

/// <summary>
/// F64 credit-receivable adjustment from an approved/rejected claim. A positive
/// <see cref="Amount"/> reduces the receivable (approved claim against the final price of the
/// affected goods); a negative amount restores a prior reduction (claim rejected). The reduction
/// is bounded so the adjusted amount never drops below what was already collected nor rises above
/// the original approved total. Disputed money stays out of the collectable/reminder base.
/// </summary>
public sealed record AdjustReceivableData(
    decimal Amount,
    string Reason,
    int ExpectedVersion);

public sealed record CreatePayOsCheckoutData(int ExpectedVersion);

public sealed record PayOsCreateLinkData(
    long OrderCode,
    long Amount,
    string Description,
    string BuyerName);

public sealed record PayOsLinkData(
    long OrderCode,
    long Amount,
    string PaymentLinkId,
    string CheckoutUrl,
    string QrCode,
    string Status,
    long? ExpiredAt);

public sealed record PayOsWebhookPayload(
    long OrderCode,
    long Amount,
    string Description,
    string AccountNumber,
    string Reference,
    string TransactionDateTime,
    string Currency,
    string PaymentLinkId,
    string Code,
    string Description2,
    string? CounterAccountBankId,
    string? CounterAccountBankName,
    string? CounterAccountName,
    string? CounterAccountNumber,
    string? VirtualAccountName,
    string? VirtualAccountNumber);

public sealed record PayOsWebhookEnvelope(
    string Code,
    string Description,
    bool Success,
    PayOsWebhookPayload Data,
    string Signature);

public sealed record PayOsVerifiedWebhook(
    string Code,
    bool Success,
    PayOsWebhookPayload Data);

public interface IPayOsGateway
{
    Task<PayOsLinkData> CreatePaymentLinkAsync(
        PayOsCreateLinkData data, CancellationToken cancellationToken);
    Task<PayOsVerifiedWebhook> VerifyWebhookAsync(
        PayOsWebhookEnvelope webhook, CancellationToken cancellationToken);
}

public sealed record PayOsCheckoutResult(
    Guid PaymentId,
    Guid OrderId,
    long OrderCode,
    decimal Amount,
    string PaymentLinkId,
    string CheckoutUrl,
    string QrCode,
    string Status,
    long? ExpiredAt,
    int PaymentVersion);

public sealed record PayOsWebhookResult(bool Processed, string Message, Guid? PaymentId);

public sealed record PaymentResult(
    Guid Id,
    Guid OrderId,
    string OrderCode,
    Guid RetailerId,
    string RetailerCode,
    string RetailerName,
    Guid SalesUserId,
    string OrderPaymentMethod,
    string Status,
    decimal Amount,
    decimal AdjustedAmount,
    decimal ConfirmedAmount,
    decimal OutstandingAmount,
    decimal PendingRefundAmount,
    decimal RefundedAmount,
    decimal RefundDueAmount,
    int CreditTermDays,
    DateTimeOffset FinalApprovedAt,
    string FinalApprovalReference,
    DateOnly? DeliveredDate,
    DateOnly? DueDate,
    int DaysOverdue,
    DateOnly? PaidDate,
    string? CollectionMethod,
    long? PayOsOrderCode,
    string? PayOsPaymentLinkId,
    string? PayOsCheckoutUrl,
    string? PayOsQrCode,
    string? PayOsLinkStatus,
    long? PayOsExpiredAt,
    string? PayOsReference,
    Guid? ConfirmedByUserId,
    DateTimeOffset? ConfirmedAt,
    string? Note,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
