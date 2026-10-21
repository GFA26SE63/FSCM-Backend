namespace FSCM.Application.Features.ManualPayments;

public sealed class ManualPaymentOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
    public static ManualPaymentOperationException Validation(string message) => new("validation_error", message);
    public static ManualPaymentOperationException NotFound(string message) => new("not_found", message);
    public static ManualPaymentOperationException Forbidden(string message) => new("forbidden", message);
    public static ManualPaymentOperationException Conflict(string message) => new("conflict", message);
}
