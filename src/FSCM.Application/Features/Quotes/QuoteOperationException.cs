namespace FSCM.Application.Features.Quotes;

public sealed class QuoteOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
    public static QuoteOperationException NotFound(string message) => new("not_found", message);
    public static QuoteOperationException Forbidden(string message) => new("forbidden", message);
    public static QuoteOperationException Conflict(string message) => new("conflict", message);
    public static QuoteOperationException Validation(string message) => new("validation_error", message);
}
