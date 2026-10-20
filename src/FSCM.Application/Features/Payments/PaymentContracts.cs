namespace FSCM.Application.Features.Payments;

/// <summary>Records a new payment against a sales order (COD or prepaid).</summary>
public sealed record RecordPaymentData(
    Guid SalesOrderId, string Method, decimal Amount, bool MarkPaid, string? Note);

public sealed record PaymentResult(
    Guid Id, Guid SalesOrderId, string OrderCode, string Method, string Status,
    decimal Amount, Guid? ConfirmedByUserId, DateTimeOffset? ConfirmedAt, string? Note,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
