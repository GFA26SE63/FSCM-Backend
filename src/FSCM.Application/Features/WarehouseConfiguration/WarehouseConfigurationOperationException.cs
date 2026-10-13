namespace FSCM.Application.Features.WarehouseConfiguration;

public sealed class WarehouseConfigurationOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public static WarehouseConfigurationOperationException Validation(string message) => new("validation_error", message);
    public static WarehouseConfigurationOperationException NotFound(string message) => new("not_found", message);
    public static WarehouseConfigurationOperationException Conflict(string message) => new("conflict", message);
}
