namespace FSCM.Application.Features.Complaints;

public sealed class ComplaintOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
    public static ComplaintOperationException Validation(string message) => new("validation_error", message);
    public static ComplaintOperationException NotFound(string message) => new("not_found", message);
    public static ComplaintOperationException Forbidden(string message) => new("forbidden", message);
    public static ComplaintOperationException Conflict(string message) => new("conflict", message);
}
