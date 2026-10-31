namespace FSCM.API.Contracts.ManualPayments;

/// <summary>
/// Records a manual prepaid payment against a sales order. <c>Method</c> accepts only
/// <c>PrepaidTransfer</c>; COD and prepaid cash are rejected (SRS BR-51).
/// Set <c>MarkPaid</c> to true when the transfer is already verified; leave it false to register
/// the transfer evidence and confirm it later.
/// </summary>
public sealed record RecordManualPaymentRequest(
    Guid SalesOrderId, string Method, decimal Amount, bool MarkPaid = false, string? Note = null);

public sealed record ConfirmManualPaymentRequest(int ExpectedVersion, string? Note = null);

public sealed record CancelManualPaymentRequest(int ExpectedVersion, string? Reason = null);
