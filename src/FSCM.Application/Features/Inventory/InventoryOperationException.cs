namespace FSCM.Application.Features.Inventory;

public sealed class InventoryOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
    public static InventoryOperationException Validation(string message) => new("validation_error", message);
    public static InventoryOperationException NotFound(string message) => new("not_found", message);
    public static InventoryOperationException Forbidden(string message) => new("forbidden", message);
    public static InventoryOperationException Conflict(string message) => new("conflict", message);
}
