namespace FSCM.Application.Features.Retailers;

public sealed class RetailerOperationException(string code, string message, string? field = null) : Exception(message)
{
    public string Code { get; } = code;

    /// <summary>Request field the error is about (camelCase, e.g. "phone"), so a form can show it inline.</summary>
    public string? Field { get; } = field;

    public static RetailerOperationException Validation(string message) => new("validation_error", message);
    public static RetailerOperationException NotFound(string message) => new("not_found", message);
    public static RetailerOperationException Conflict(string message) => new("conflict", message);
    public static RetailerOperationException Forbidden(string message) => new("forbidden", message);
    public static RetailerOperationException InvalidTransition(string message) => new("invalid_transition", message);

    /// <summary>A contact value (phone, email, name + address) already belongs to another retailer or account.</summary>
    public static RetailerOperationException Duplicate(string field, string message) => new("conflict", message, field);
}
