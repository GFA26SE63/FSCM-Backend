namespace FSCM.Application.Features.Orders;

public sealed class OrderOperationException(string code, string message, object? details = null) : Exception(message)
{
    public string Code { get; } = code;
    public object? Details { get; } = details;
    public static OrderOperationException Validation(string message) => new("validation_error", message);
    public static OrderOperationException NotFound(string message) => new("not_found", message);
    public static OrderOperationException Forbidden(string message) => new("forbidden", message);
    public static OrderOperationException Conflict(string message) => new("conflict", message);
    public static OrderOperationException Conflict(string message, object details) =>
        new("conflict", message, details);
}
