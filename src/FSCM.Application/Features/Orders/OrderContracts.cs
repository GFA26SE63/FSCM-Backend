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
    int Version,
    DateTimeOffset ServerReceivedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyCollection<OrderLineResult> Lines,
    IReadOnlyCollection<OrderStatusHistoryResult> History,
    IReadOnlyCollection<OrderShipmentLinkResult> Shipments)
{
    public decimal IndicativeAmount => FinalAmount;
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

public sealed record OrderTraceResult(Guid OrderId, string OrderCode, string AllocationTraceJson);

public static class OrderStatuses
{
    public const string PendingApproval = "PendingApproval";
    public const string Approved = "Approved";
    public const string Picking = "Picking";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";
    public const string Issued = "Issued";
    public const string Delivering = "Delivering";
    public const string Delivered = "Delivered";
}

public static class OrderSources
{
    public const string Assisted = "Assisted";
    public const string Offline = "Offline";
}

public static class OrderPaymentMethods
{
    public const string Prepaid = "Prepaid";
    public const string Cod = "COD";
    public const string Credit = "Credit";

    public static bool IsSupported(string value) => value is Prepaid or Cod or Credit;
}
