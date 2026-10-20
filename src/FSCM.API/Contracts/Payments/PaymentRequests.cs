namespace FSCM.API.Contracts.Payments;

/// <summary>
/// Records a payment against a sales order. <c>Method</c> accepts COD, PrepaidTransfer or PrepaidCash.
/// Set <c>MarkPaid</c> to true to record an already-received payment (prepaid or cash-in-hand);
/// leave it false to register an expected COD amount that is confirmed later on delivery.
/// </summary>
public sealed record RecordPaymentRequest(
    Guid SalesOrderId, string Method, decimal Amount, bool MarkPaid = false, string? Note = null);

public sealed record ConfirmPaymentRequest(string? Note = null);

public sealed record CancelPaymentRequest(string? Reason = null);
