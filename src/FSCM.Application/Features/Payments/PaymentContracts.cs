namespace FSCM.Application.Features.Payments;

public static class PaymentStatuses
{
    public const string Unpaid = "Unpaid";
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
    decimal Amount,
    string CollectionMethod,
    int ExpectedVersion,
    string? Note);

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
    decimal ConfirmedAmount,
    decimal OutstandingAmount,
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
