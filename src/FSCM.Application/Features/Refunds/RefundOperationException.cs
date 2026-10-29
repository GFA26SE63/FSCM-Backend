namespace FSCM.Application.Features.Refunds;

public sealed class RefundOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
    public static RefundOperationException Validation(string message) => new("validation_error", message);
    public static RefundOperationException NotFound(string message) => new("not_found", message);
    public static RefundOperationException Forbidden(string message) => new("forbidden", message);
    public static RefundOperationException Conflict(string message) => new("conflict", message);
}
