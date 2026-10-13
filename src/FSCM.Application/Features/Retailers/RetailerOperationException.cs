namespace FSCM.Application.Features.Retailers;

public sealed class RetailerOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public static RetailerOperationException Validation(string message) => new("validation_error", message);
    public static RetailerOperationException NotFound(string message) => new("not_found", message);
    public static RetailerOperationException Conflict(string message) => new("conflict", message);
    public static RetailerOperationException Forbidden(string message) => new("forbidden", message);
    public static RetailerOperationException InvalidTransition(string message) => new("invalid_transition", message);
}
