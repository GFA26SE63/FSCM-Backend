namespace FSCM.Application.Features.InternalTransfers;

public sealed class InternalTransferOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
    public static InternalTransferOperationException Validation(string message) => new("validation_error", message);
    public static InternalTransferOperationException NotFound(string message) => new("not_found", message);
    public static InternalTransferOperationException Forbidden(string message) => new("forbidden", message);
    public static InternalTransferOperationException Conflict(string message) => new("conflict", message);
}
