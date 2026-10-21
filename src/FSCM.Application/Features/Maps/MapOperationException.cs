namespace FSCM.Application.Features.Maps;

public sealed class MapOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
    public static MapOperationException Validation(string message) => new("validation_error", message);
    public static MapOperationException NotFound(string message) => new("not_found", message);
    public static MapOperationException Conflict(string message) => new("conflict", message);
    public static MapOperationException Provider(string message) => new("map_provider_error", message);
}
