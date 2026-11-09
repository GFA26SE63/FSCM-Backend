namespace FSCM.Application.Features.Storage;

/// <summary>Domain error for image upload/validation, mapped to ProblemDetails by ImageStorageExceptionHandler.</summary>
public sealed class ImageStorageException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public static ImageStorageException Validation(string message) => new("validation_error", message);
    public static ImageStorageException NotFound(string message) => new("not_found", message);
    public static ImageStorageException Forbidden(string message) => new("forbidden", message);
    public static ImageStorageException Unavailable(string message) => new("service_unavailable", message);
}
