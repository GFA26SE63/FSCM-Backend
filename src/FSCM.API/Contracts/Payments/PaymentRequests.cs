namespace FSCM.API.Contracts.Payments;

public sealed record CollectPaymentRequest(
    decimal Amount,
    string CollectionMethod,
    int ExpectedVersion,
    string? Note);

public sealed record CreatePayOsCheckoutRequest(int ExpectedVersion);

public sealed record AdjustReceivableRequest(
    decimal Amount,
    string Reason,
    int ExpectedVersion);

public sealed record PayOsWebhookRequest(
    string Code,
    string Desc,
    bool Success,
    PayOsWebhookDataRequest Data,
    string Signature);

public sealed record PayOsWebhookDataRequest(
    long OrderCode,
    long Amount,
    string Description,
    string AccountNumber,
    string Reference,
    string TransactionDateTime,
    string Currency,
    string PaymentLinkId,
    string Code,
    string Desc,
    string? CounterAccountBankId,
    string? CounterAccountBankName,
    string? CounterAccountName,
    string? CounterAccountNumber,
    string? VirtualAccountName,
    string? VirtualAccountNumber);
