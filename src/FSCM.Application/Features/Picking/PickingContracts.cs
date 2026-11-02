namespace FSCM.Application.Features.Picking;

// Legacy aggregate statuses remain while cancellation/dispatch consumers are migrated in F93/F96.
public static class PickingListStatuses
{
    public const string Pending = "Pending";
    public const string Open = Pending;
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Exception = "Exception";
    public const string Cancelled = Exception;
}

public static class PickingItemStatuses
{
    public const string Pending = "Pending";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
}

public sealed record RunPickingSplitQuantityData(Guid AllocationRunAllocationId, decimal Quantity);
public sealed record ScanPickingLabelData(string QrCode, decimal? Quantity, int ExpectedPickingVersion,
    int ExpectedLabelVersion, IReadOnlyCollection<RunPickingSplitQuantityData> Splits);
public sealed record CompleteRunPickingData(int ExpectedPickingVersion, string? ExceptionType, string? Note);
public sealed record PickingScanResult(string OperationKey, string QrCode, decimal Quantity,
    Guid PerformedByUserId, DateTimeOffset OccurredAt);
public sealed record RunPickingSplitWorkflowResult(Guid Id, Guid AllocationRunAllocationId,
    Guid AllocationPlanPriceId, Guid SalesOrderId, Guid SalesOrderLineId, string OrderCode,
    decimal RequiredQuantity, decimal PickedQuantity, decimal RemainingQuantity);
public sealed record RunPickingGatherWorkflowResult(Guid Id, Guid ProductId, string Sku, Guid BatchId,
    string BatchCode, decimal RequiredQuantity, decimal PickedQuantity, decimal RemainingQuantity,
    string Status, IReadOnlyCollection<RunPickingSplitWorkflowResult> Splits);
public sealed record PickingListResult(Guid Id, string PickingCode, Guid AllocationRunId,
    Guid AllocationPlanVersionId, Guid WarehouseId, Guid? AssignedToUserId, string Status, int Version,
    DateTimeOffset IssuedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt,
    Guid? CompletedByUserId, string? ExceptionType, string? ExceptionNote,
    IReadOnlyCollection<RunPickingGatherWorkflowResult> Gathers,
    IReadOnlyCollection<PickingScanResult> Scans);
