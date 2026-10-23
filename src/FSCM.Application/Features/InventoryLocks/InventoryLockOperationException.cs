namespace FSCM.Application.Features.InventoryLocks;

public sealed class InventoryLockOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
    public static InventoryLockOperationException Validation(string message) => new("validation_error", message);
    public static InventoryLockOperationException NotFound(string message) => new("not_found", message);
    public static InventoryLockOperationException Forbidden(string message) => new("forbidden", message);
    public static InventoryLockOperationException Conflict(string message) => new("conflict", message);
}
