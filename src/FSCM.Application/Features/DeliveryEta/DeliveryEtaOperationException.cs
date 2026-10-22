namespace FSCM.Application.Features.DeliveryEta;

public sealed class DeliveryEtaOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public static DeliveryEtaOperationException Validation(string message) => new("validation_error", message);
    public static DeliveryEtaOperationException NotFound(string message) => new("not_found", message);
    public static DeliveryEtaOperationException Conflict(string message) => new("conflict", message);
}
