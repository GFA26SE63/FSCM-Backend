namespace FSCM.Application.Features.ManualPayments;

/// <summary>Records a manual payment (COD or prepaid) against a sales order.</summary>
public sealed record RecordManualPaymentData(
    Guid SalesOrderId, string Method, decimal Amount, bool MarkPaid, string? Note);

public sealed record ManualPaymentResult(
    Guid Id, Guid SalesOrderId, string OrderCode, string Method, string Status,
    decimal Amount, Guid? ConfirmedByUserId, DateTimeOffset? ConfirmedAt, string? Note,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
