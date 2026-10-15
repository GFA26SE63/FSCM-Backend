namespace FSCM.Application.Features.Deliveries;

public sealed class DeliveryOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public static DeliveryOperationException Validation(string message) => new("validation_error", message);
    public static DeliveryOperationException NotFound(string message) => new("not_found", message);
    public static DeliveryOperationException Forbidden(string message) => new("forbidden", message);
    public static DeliveryOperationException Conflict(string message) => new("conflict", message);
}
