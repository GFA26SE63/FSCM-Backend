namespace FSCM.Application.Features.Shipments;

public sealed class ShipmentOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public static ShipmentOperationException Validation(string message) => new("validation_error", message);
    public static ShipmentOperationException NotFound(string message) => new("not_found", message);
    public static ShipmentOperationException Forbidden(string message) => new("forbidden", message);
    public static ShipmentOperationException Conflict(string message) => new("conflict", message);
}
