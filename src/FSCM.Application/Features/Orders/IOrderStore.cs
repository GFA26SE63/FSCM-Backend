using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Orders;

public sealed record OrderRetailerContext(Retailer Retailer, Guid SalesGroupId);

public sealed record OrderInventoryCandidate(
    Batch Batch,
    InventoryBalance Balance,
    Warehouse Warehouse,
    decimal? WarehouseLatitude,
    decimal? WarehouseLongitude);

public sealed record OrderMlorSnapshot(
    string SourceScope,
    Guid? SourceRuleId,
    int SourceVersion,
    Guid? ProductCategoryId,
    decimal MinimumLifeRatio,
    int? MinimumLifeHours,
    DateTimeOffset ResolvedAt);

public sealed record OrderLineView(
    SalesOrderLine Line,
    IReadOnlyCollection<Allocation> Allocations,
    OrderPromotion? OrdinaryPromotion);

public sealed record OrderView(
    SalesOrder Order,
    IReadOnlyCollection<OrderLineView> Lines,
    IReadOnlyCollection<OrderStatusHistory> History,
    IReadOnlyCollection<Delivery> Deliveries,
    IReadOnlyDictionary<Guid, string> BatchCodes,
    IReadOnlyDictionary<Guid, string> WarehouseCodes,
    IReadOnlyDictionary<Guid, int> InventoryVersions)
{
    public OrderAllocationReadContext? AllocationContext { get; init; }
}

public sealed record OrderAllocationReadContext(
    Guid RunId,
    string RunCode,
    string RunStatus,
    string OrderResult,
    string? ShortageReason,
    Guid PlanVersionId,
    int PlanVersionNumber,
    string PlanStatus,
    DateTimeOffset PricedAt,
    IReadOnlyCollection<OrderAllocationFragmentView> Fragments,
    IReadOnlyCollection<OrderWarehouseProgressView> WarehouseProgress);

public sealed record OrderAllocationFragmentView(
    Guid AllocationId,
    Guid PlanPriceId,
    Guid SalesOrderLineId,
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

public sealed record OrderWarehouseProgressView(
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

public interface IOrderStore
{
    Task<SalesOrder?> FindByClientOrderKeyAsync(Guid clientOrderKey, CancellationToken cancellationToken);
    Task<OrderRetailerContext?> FindRetailerContextAsync(Guid salesUserId, Guid retailerId, CancellationToken cancellationToken);
    Task<Product?> FindPublishedProductAsync(Guid productId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<OrderInventoryCandidate>> ListCandidatesAsync(Guid productId, CancellationToken cancellationToken);
    Task<MapLocation?> FindRetailerLocationAsync(Guid retailerId, CancellationToken cancellationToken);
    Task<OrderMlorSnapshot?> ResolveMlorAsync(
        Guid retailerId, Guid productId, DateTimeOffset at, CancellationToken cancellationToken);
    Task<string?> FindConfigurationAsync(string key, CancellationToken cancellationToken);
    Task<string> NextOrderCodeAsync(CancellationToken cancellationToken);
    Task<OrderView?> FindAsync(Guid orderId, bool forUpdate, CancellationToken cancellationToken);
    Task<OrderView?> FindByCodeAsync(string orderCode, bool forUpdate, CancellationToken cancellationToken);
    Task<(IReadOnlyCollection<OrderView> Items, int TotalCount)> ListAsync(
        Guid actorUserId, string actorRole, OrderListQuery query, CancellationToken cancellationToken);
    Task<bool> CanViewAsync(OrderView order, Guid actorUserId, string actorRole, CancellationToken cancellationToken);
    void AddOrder(SalesOrder order);
    void AddLine(SalesOrderLine line);
    void AddAllocation(Allocation allocation);
    void AddPromotion(OrderPromotion promotion);
    void AddHistory(OrderStatusHistory history);
    void AddAuditLog(AuditLog auditLog);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken);
    void ClearTracking();
}
