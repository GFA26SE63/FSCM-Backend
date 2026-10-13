namespace FSCM.Application.Features.QrLabels;

public sealed class QrLabelOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
    public static QrLabelOperationException Validation(string message) => new("validation_error", message);
    public static QrLabelOperationException NotFound(string message) => new("not_found", message);
    public static QrLabelOperationException Forbidden(string message) => new("forbidden", message);
    public static QrLabelOperationException Conflict(string message) => new("conflict", message);
}
