namespace FSCM.Application.Features.AllocationRuns;

public sealed class AllocationRunOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public static AllocationRunOperationException Validation(string message) => new("validation_error", message);
    public static AllocationRunOperationException NotFound(string message) => new("not_found", message);
    public static AllocationRunOperationException Conflict(string message) => new("conflict", message);
}
