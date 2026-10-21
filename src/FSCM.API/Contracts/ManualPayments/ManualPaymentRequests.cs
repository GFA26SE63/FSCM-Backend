namespace FSCM.API.Contracts.ManualPayments;

/// <summary>
/// Records a manual payment against a sales order. <c>Method</c> accepts COD, PrepaidTransfer or PrepaidCash.
/// Set <c>MarkPaid</c> to true to record an already-received payment (prepaid or cash-in-hand);
/// leave it false to register an expected COD amount that is confirmed later on delivery.
/// </summary>
public sealed record RecordManualPaymentRequest(
    Guid SalesOrderId, string Method, decimal Amount, bool MarkPaid = false, string? Note = null);

public sealed record ConfirmManualPaymentRequest(int ExpectedVersion, string? Note = null);

public sealed record CancelManualPaymentRequest(int ExpectedVersion, string? Reason = null);
