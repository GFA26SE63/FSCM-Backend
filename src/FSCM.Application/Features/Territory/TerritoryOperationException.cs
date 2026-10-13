namespace FSCM.Application.Features.Territory;

public sealed class TerritoryOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public static TerritoryOperationException Validation(string message) => new("validation_error", message);
    public static TerritoryOperationException NotFound(string message) => new("not_found", message);
    public static TerritoryOperationException Conflict(string message) => new("conflict", message);
    public static TerritoryOperationException Forbidden(string message) => new("forbidden", message);
}
