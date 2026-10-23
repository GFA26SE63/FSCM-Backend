namespace FSCM.Application.Features.Reports;

public sealed class ReportOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public static ReportOperationException Validation(string message) => new("validation_error", message);
    public static ReportOperationException Forbidden(string message) => new("forbidden", message);
}
