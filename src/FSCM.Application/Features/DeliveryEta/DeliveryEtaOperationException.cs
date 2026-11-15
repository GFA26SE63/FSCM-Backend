namespace FSCM.Application.Features.DeliveryEta;

public sealed class DeliveryEtaOperationException(string code, string message, bool canRefreshDistance = false) : Exception(message)
{
    public string Code { get; } = code;
    public bool CanRefreshDistance { get; } = canRefreshDistance;
    public static DeliveryEtaOperationException MissingDistance(string message) => new("not_found", message, true);
    public static DeliveryEtaOperationException StaleDistance(string message) => new("conflict", message, true);

    public static DeliveryEtaOperationException Validation(string message) => new("validation_error", message);
    public static DeliveryEtaOperationException NotFound(string message) => new("not_found", message);
    public static DeliveryEtaOperationException Conflict(string message) => new("conflict", message);
}
