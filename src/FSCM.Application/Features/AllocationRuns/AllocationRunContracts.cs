namespace FSCM.Application.Features.AllocationRuns;

public static class AllocationRunStatuses
{
    /// <summary>Orders locked and the pool snapshotted; scoring has not started.</summary>
    public const string Snapshotted = "Snapshotted";

    /// <summary>Batch selection and pricing are running.</summary>
    public const string Planning = "Planning";

    /// <summary>A plan exists and is waiting for Admin to approve goods and price.</summary>
    public const string AwaitingApproval = "AwaitingApproval";

    public const string Approved = "Approved";
    public const string Closed = "Closed";
    public const string Failed = "Failed";
}

/// <summary>
/// Where a line's effective MLOR came from. Category sits between the two and arrives with F22;
/// until then a retailer-SKU ratio or the configured default applies.
/// </summary>
public static class MlorSources
{
    public const string RetailerProduct = "RetailerProduct";
    public const string RetailerCategory = "RetailerCategory";
    public const string ProductDefault = "ProductDefault";
}

public static class AllocationPlanVersionStatuses
{
    /// <summary>Put to Admin and waiting. Never leaves this state on its own.</summary>
    public const string AwaitingApproval = "AwaitingApproval";

    public const string Approved = "Approved";

    /// <summary>Admin asked for a recalculation; a later version replaces this one.</summary>
    public const string RecalculationRequested = "RecalculationRequested";

    /// <summary>Replaced by a later version without being decided.</summary>
    public const string Superseded = "Superseded";
}

public sealed record AllocationPlanPriceResult(
    Guid AllocationRunLineId,
    Guid BatchId,
    Guid WarehouseId,
    decimal Quantity,
    decimal BaseNetUnitPrice,
    decimal RemainingShelfLifePercent,
    Guid? ShelfLifeDiscountTierId,
    string? DiscountType,
    decimal? DiscountValue,
    decimal ShelfLifeDiscountPerUnit,
    decimal FinalNetUnitPrice,
    decimal Amount,
    int DiscountTableVersion,
    string Explanation);

public sealed record AllocationPlanVersionResult(
    Guid Id,
    Guid AllocationRunId,
    string RunCode,
    int VersionNumber,
    string Status,
    DateTimeOffset PricedAt,
    int FulfilledOrderCount,
    int ShortOrderCount,
    decimal TotalFinalAmount,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAt,
    string? RecalculationReason,
    int Version,
    IReadOnlyCollection<AllocationPlanPriceResult> Prices);

public static class AllocationRunOrderStatuses
{
    public const string Pending = "Pending";
    public const string Fulfilled = "Fulfilled";
    public const string Short = "Short";
}

public sealed record AllocationRunLineResult(
    Guid Id,
    Guid SalesOrderLineId,
    Guid ProductId,
    string Sku,
    int LineNumber,
    decimal OrderedQuantity,
    int TotalShelfLifeHours,
    decimal EffectiveMlorRatio,
    string MlorSource,
    decimal BaseNetUnitPrice,
    decimal AllocatedQuantity);

public sealed record AllocationRunOrderResult(
    Guid Id,
    Guid SalesOrderId,
    string OrderCode,
    Guid RetailerId,
    DateTimeOffset AcceptedAt,
    DateOnly? DeliveryDeadline,
    int PriorityRank,
    string Status,
    string? ShortageReason,
    IReadOnlyCollection<AllocationRunLineResult> Lines);

public sealed record AllocationRunStockResult(
    Guid BatchId,
    Guid WarehouseId,
    Guid ProductId,
    DateOnly ExpirationDate,
    decimal SnapshotQuantity,
    decimal RemainingQuantity,
    int InventoryVersion);

public sealed record AllocationRunResult(
    Guid Id,
    string RunCode,
    DateOnly BusinessDate,
    DateTimeOffset CutoffAt,
    string Status,
    AllocationSettings Settings,
    int TransitTableVersion,
    int EtaScheduleVersion,
    int OrderCount,
    string? FailureReason,
    DateTimeOffset StartedAt,
    DateTimeOffset? SnapshotCompletedAt,
    DateTimeOffset? CompletedAt,
    int Version,
    IReadOnlyCollection<AllocationRunOrderResult> Orders,
    IReadOnlyCollection<AllocationRunStockResult> Pool);

/// <summary>Weights and service radius frozen for one run, so a later config change cannot move a plan.</summary>
public sealed record AllocationSettings(
    decimal ShelfLifeWeight,
    decimal DistanceWeight,
    decimal CoverageWeight,
    int MaxServingDistanceMeters);
