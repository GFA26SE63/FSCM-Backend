namespace FSCM.Application.Features.TransitTables;

public sealed class TransitTableOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public static TransitTableOperationException Validation(string message) => new("validation_error", message);
    public static TransitTableOperationException NotFound(string message) => new("not_found", message);
    public static TransitTableOperationException Conflict(string message) => new("conflict", message);
}
