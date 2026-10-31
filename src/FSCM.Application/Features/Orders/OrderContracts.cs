namespace FSCM.Application.Features.Orders;

public sealed record CreateOrderLineData(Guid ProductId, int Quantity);

public sealed record CreateAssistedOrderData(
    Guid ClientOrderKey,
    Guid RetailerId,
    string PaymentMethod,
    DateOnly? RequestedDeliveryDate,
    string? DeliveryAddress,
    string? Note,
    IReadOnlyCollection<CreateOrderLineData> Lines);

/// <summary>
/// F30: an offline draft a Sales rep captured without connectivity and submits once back online.
/// <see cref="ClientOrderKey"/> makes resubmission after a connection error idempotent (no duplicate
/// order). <see cref="OfflineEstimatedAmount"/> is the amount Sales quoted offline; the server
/// re-prices against current base price/MLOR and flags a difference for Sales to confirm.
/// </summary>
public sealed record SubmitOfflineOrderData(
    Guid ClientOrderKey,
    Guid RetailerId,
    string PaymentMethod,
    DateOnly? RequestedDeliveryDate,
    string? DeliveryAddress,
    string? Note,
    decimal OfflineEstimatedAmount,
    IReadOnlyCollection<CreateOrderLineData> Lines,
    string? SnapshotVersion = null,
    bool ConfirmConditionChanges = false,
    IReadOnlyCollection<OfflineExpectedConditionData>? ExpectedConditions = null);

public sealed record OfflineExpectedConditionData(
    Guid ProductId,
    decimal ExpectedBaseUnitPrice,
    int ExpectedMlorVersion,
    decimal ExpectedMlorRatio);

public sealed record OfflineConditionDifference(
    Guid ProductId,
    string Sku,
    decimal ExpectedBaseUnitPrice,
    decimal CurrentBaseUnitPrice,
    int ExpectedMlorVersion,
    int CurrentMlorVersion,
    decimal ExpectedMlorRatio,
    decimal CurrentMlorRatio);

public sealed record OfflineConditionConflictResult(
    Guid ClientOrderKey,
    string? SnapshotVersion,
    decimal OfflineEstimatedAmount,
    decimal CurrentIndicativeAmount,
    IReadOnlyCollection<OfflineConditionDifference> ChangedLines,
    bool RequiresExplicitConfirmation = true);

public sealed record OrderAllocationResult(
    Guid Id,
    Guid BatchId,
    string BatchCode,
    Guid WarehouseId,
    string WarehouseCode,
    decimal Quantity,
    int Sequence,
    decimal DistanceMeters,
    DateTimeOffset EstimatedArrivalAt,
    int RemainingLifeHoursAtArrival,
    decimal ShelfLifeScore,
    decimal DistanceScore,
    decimal CoverageScore,
    decimal WeightedScore,
    decimal UnitPrice,
    decimal GrossAmount,
    decimal DiscountPercent,
    decimal DiscountAmount,
    decimal NetAmount,
    string? DiscountSourceType,
    string? DiscountSourceCode,
    string Status);

public sealed record OrderAllocationFragmentResult(
    Guid AllocationId,
    Guid PlanPriceId,
    Guid BatchId,
    string BatchCode,
    DateOnly BatchExpirationDate,
    Guid WarehouseId,
    string WarehouseCode,
    decimal Quantity,
    int Sequence,
    DateTimeOffset EstimatedArrivalAt,
    decimal RemainingLifeHoursAtArrival,
    decimal ListUnitPrice,
    decimal OrdinaryPromotionDiscountPerUnit,
    decimal BaseUnitPrice,
    decimal RemainingShelfLifePercent,
    Guid? DiscountRuleId,
    string? DiscountRuleScope,
    string? DiscountRuleCode,
    int DiscountRuleVersion,
    string? DiscountType,
    decimal? DiscountValue,
    decimal ShelfLifeDiscountPerUnit,
    decimal FinalUnitPrice,
    decimal NetAmount,
    string DiscountReason);

public sealed record OrderMlorSnapshotResult(
    string SourceScope,
    Guid? SourceRuleId,
    int SourceVersion,
    Guid? ProductCategoryId,
    decimal MinimumLifeRatio,
    int? MinimumLifeHours,
    DateTimeOffset ResolvedAt);

public sealed record OrderOrdinaryPromotionSnapshotResult(
    Guid PromotionId,
    string Code,
    string Name,
    string Type,
    decimal MinimumQuantity,
    decimal DiscountPercent,
    decimal DiscountAmount,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset EffectiveTo,
    DateTimeOffset AppliedAt);

public sealed record OrderLineResult(
    Guid Id,
    int LineNumber,
    Guid ProductId,
    string Sku,
    string ProductName,
    decimal Quantity,
    decimal UnitPrice,
    decimal GrossAmount,
    decimal DiscountPercent,
    decimal DiscountAmount,
    decimal NetAmount,
    string? DiscountSourceType,
    string? DiscountSourceCode,
    Guid ProductPriceId,
    DateTimeOffset ProductPriceEffectiveFrom,
    DateTimeOffset? ProductPriceEffectiveTo,
    DateTimeOffset PricedAt,
    OrderMlorSnapshotResult Mlor,
    OrderOrdinaryPromotionSnapshotResult? OrdinaryPromotion,
    DateTimeOffset? EstimatedArrivalAt,
    bool FinalPricePendingAllocation,
    IReadOnlyCollection<OrderAllocationResult> Allocations)
{
    public decimal BaseUnitPrice => UnitPrice;
    public decimal OrdinaryPromotionDiscountAmount => DiscountAmount;
    public decimal IndicativeNetAmount => NetAmount;
    public IReadOnlyCollection<OrderAllocationFragmentResult> AllocationFragments { get; init; } = [];
    public decimal? OfficialNetAmount { get; init; }
}

public sealed record OrderStatusHistoryResult(
    string? FromStatus,
    string ToStatus,
    string? Reason,
    Guid ChangedByUserId,
    DateTimeOffset ChangedAt);

public sealed record OrderSummaryResult(
    Guid Id,
    string OrderCode,
    Guid RetailerId,
    string RetailerCode,
    string RetailerName,
    Guid SalesGroupId,
    Guid CreatedByUserId,
    string Status,
    string PaymentMethod,
    decimal FinalAmount,
    bool FinalPricePendingAllocation,
    string AllocationState,
    Guid? AllocationRunId,
    string? AllocationProposalStatus,
    string? AllocationResult,
    int ShipmentCount,
    int TotalCartons,
    int LineCount,
    string Source,
    int Version,
    DateTimeOffset CreatedAt)
{
    public decimal IndicativeAmount => FinalAmount;
    public decimal? OfficialAmount { get; init; }
}

public sealed record OrderListQuery(
    string? Status,
    Guid? RetailerId,
    bool CreatedByMe,
    string? Search,
    int Page,
    int PageSize);

public sealed record PagedOrderResult(
    IReadOnlyCollection<OrderSummaryResult> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record OrderShipmentLinkResult(
    Guid Id,
    string Status,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? DeliveredAt);

public sealed record OrderWarehouseProgressResult(
    Guid WarehouseId,
    string WarehouseCode,
    Guid? PickingListId,
    string? PickingCode,
    string? PickingStatus,
    Guid? ShipmentId,
    string? ShipmentCode,
    string? ShipmentStatus,
    decimal PlannedQuantity,
    decimal PickedQuantity,
    decimal ReceivedQuantity);

public sealed record OrderDetailResult(
    Guid Id,
    string OrderCode,
    Guid RetailerId,
    string RetailerCode,
    string RetailerName,
    Guid SalesGroupId,
    Guid CreatedByUserId,
    Guid? ReviewedByUserId,
    string Source,
    string Status,
    string PaymentMethod,
    int CreditTermDays,
    string DeliveryAddress,
    DateOnly? RequestedDeliveryDate,
    string? Note,
    decimal SubtotalAmount,
    decimal DiscountAmount,
    decimal FinalAmount,
    bool FinalPricePendingAllocation,
    string AllocationState,
    Guid? AllocationRunId,
    string? AllocationProposalStatus,
    string? AllocationResult,
    string? RejectionReason,
    decimal? OfflineEstimatedAmount,
    bool HasOfflineDifference,
    int Version,
    DateTimeOffset ServerReceivedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyCollection<OrderLineResult> Lines,
    IReadOnlyCollection<OrderStatusHistoryResult> History,
    IReadOnlyCollection<OrderShipmentLinkResult> Shipments)
{
    public decimal IndicativeAmount => FinalAmount;
    public decimal? OfficialAmount { get; init; }
    public string? AllocationRunCode { get; init; }
    public int? AllocationPlanVersion { get; init; }
    public DateTimeOffset? OfficialPricedAt { get; init; }
    public IReadOnlyCollection<OrderWarehouseProgressResult> WarehouseProgress { get; init; } = [];
}

public sealed record OrderTimelineResult(
    Guid OrderId,
    string OrderCode,
    string CurrentStatus,
    string AllocationState,
    Guid? AllocationRunId,
    string? AllocationProposalStatus,
    string? AllocationResult,
    IReadOnlyCollection<OrderStatusHistoryResult> Events,
    IReadOnlyCollection<OrderShipmentLinkResult> Shipments);

public static class OrderStatuses
{
    public const string PendingApproval = "PendingApproval";

    /// <summary>
    /// Reviewed and waiting for the next allocation run. Approval commits no stock: the run
    /// decides which batches an order gets, and only run approval reserves them (F87, F88).
    /// </summary>
    public const string PendingAllocation = "PendingAllocation";

    public const string Approved = "Approved";
    public const string Picking = "Picking";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";
    public const string Issued = "Issued";
    public const string Delivering = "Delivering";
    public const string Delivered = "Delivered";

    /// <summary>
    /// Everything about the order is settled: every warehouse part was received, the money is in,
    /// and nothing is in dispute. This is the end of the commercial line and the only moment an
    /// order earns its loyalty points (F70, CH-14).
    ///
    /// Deliberately not the same as <see cref="Delivered"/>. A prepaid order that arrived but whose
    /// receipt is being disputed is delivered and not settled; a credit order delivered on terms is
    /// delivered and not yet paid. Collapsing the two would pay points on money that has not
    /// arrived and on quantities that are still being argued about.
    /// </summary>
    public const string Completed = "Completed";
}

public static class OrderSources
{
    public const string Assisted = "Assisted";
    public const string Offline = "Offline";
}

/// <summary>
/// The only order payment methods (SRS BR-51, Review 2): prepaid by bank transfer, or deferred
/// credit for a Retailer with an approved term. Cash on delivery and prepaid cash are not offered.
/// </summary>
public static class OrderPaymentMethods
{
    /// <summary>Prepaid by bank transfer (payOS or verified manual transfer) before the order leaves.</summary>
    public const string Prepaid = "Prepaid";

    /// <summary>Deferred payment on the Retailer's approved 7/14/30-day term.</summary>
    public const string Credit = "Credit";

    public static bool IsSupported(string value) => value is Prepaid or Credit;
}
