namespace FSCM.Application.Features.Payments;

public sealed class PaymentOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
    public static PaymentOperationException Validation(string message) => new("validation_error", message);
    public static PaymentOperationException NotFound(string message) => new("not_found", message);
    public static PaymentOperationException Forbidden(string message) => new("forbidden", message);
    public static PaymentOperationException Conflict(string message) => new("conflict", message);
}
