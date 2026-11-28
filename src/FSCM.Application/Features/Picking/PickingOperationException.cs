namespace FSCM.Application.Features.Picking;

public sealed class PickingOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
    public static PickingOperationException Validation(string message) => new("validation_error", message);
    public static PickingOperationException NotFound(string message) => new("not_found", message);
    public static PickingOperationException Forbidden(string message) => new("forbidden", message);
    public static PickingOperationException Conflict(string message) => new("conflict", message);
    public static PickingOperationException ServiceUnavailable(string message) => new("service_unavailable", message);
    public static PickingOperationException BadGateway(string message) => new("bad_gateway", message);
}

