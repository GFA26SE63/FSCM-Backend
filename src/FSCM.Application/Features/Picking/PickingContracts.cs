namespace FSCM.Application.Features.Picking;

public static class PickingListStatuses
{
    public const string Open = "Open";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
}

public static class PickingItemStatuses
{
    public const string Pending = "Pending";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
}

public sealed record ScanPickingLabelData(
    string QrCode,
    decimal? Quantity,
    int ExpectedPickingVersion,
    int ExpectedLabelVersion);

public sealed record PickingScanResult(
    string OperationKey,
    string QrCode,
    decimal Quantity,
    Guid PerformedByUserId,
    DateTimeOffset OccurredAt);

public sealed record PickingItemResult(
    Guid Id,
    Guid AllocationId,
    Guid ProductId,
    string Sku,
    string ProductName,
    Guid BatchId,
    string BatchCode,
    decimal RequestedQuantity,
    decimal PickedQuantity,
    decimal RemainingQuantity,
    string Status,
    DateTimeOffset? ScannedAt,
    DateTimeOffset? CompletedAt);

public sealed record PickingListResult(
    Guid Id,
    string PickingCode,
    Guid OrderId,
    string OrderCode,
    Guid WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    Guid? AssignedToUserId,
    string Status,
    int Version,
    DateTimeOffset? StartedAt,
    DateTimeOffset? ConfirmedAt,
    Guid? ConfirmedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyCollection<PickingItemResult> Items,
    IReadOnlyCollection<PickingScanResult> Scans);

