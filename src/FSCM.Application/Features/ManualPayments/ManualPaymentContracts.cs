namespace FSCM.Application.Features.ManualPayments;

/// <summary>Records a manual prepaid bank-transfer payment against a sales order.</summary>
public sealed record RecordManualPaymentData(
    Guid SalesOrderId, string Method, decimal Amount, bool MarkPaid, string? Note);

public sealed record ChangeManualPaymentData(int ExpectedVersion, string? Note);

public sealed record ManualPaymentResult(
    Guid Id, Guid SalesOrderId, string OrderCode, string Method, string Status,
    decimal Amount, Guid? ConfirmedByUserId, DateTimeOffset? ConfirmedAt, string? Note,
    int Version, decimal FinalApprovedAmount, decimal AdjustedAmount, decimal TotalConfirmedAmount,
    decimal OutstandingAmount, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
