namespace FSCM.Application.Features.WriteOffs;

/// <summary>Domain error for the write-off feature, mapped to ProblemDetails by WriteOffExceptionHandler.</summary>
public sealed class WriteOffOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public static WriteOffOperationException Validation(string message) => new("validation_error", message);
    public static WriteOffOperationException NotFound(string message) => new("not_found", message);
    public static WriteOffOperationException Forbidden(string message) => new("forbidden", message);
    public static WriteOffOperationException Conflict(string message) => new("conflict", message);
}
