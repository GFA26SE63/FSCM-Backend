using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Orders;

public sealed record OrderRetailerContext(Retailer Retailer, Guid SalesGroupId);

public sealed record OrderInventoryCandidate(
    Batch Batch,
    InventoryBalance Balance,
    Warehouse Warehouse,
    decimal? WarehouseLatitude,
    decimal? WarehouseLongitude);

public sealed record OrderLineView(
    SalesOrderLine Line,
    IReadOnlyCollection<Allocation> Allocations);

public sealed record OrderView(
    SalesOrder Order,
    IReadOnlyCollection<OrderLineView> Lines,
    IReadOnlyCollection<OrderStatusHistory> History,
    IReadOnlyDictionary<Guid, string> BatchCodes,
    IReadOnlyDictionary<Guid, string> WarehouseCodes,
    IReadOnlyDictionary<Guid, int> InventoryVersions);

public interface IOrderStore
{
    Task<SalesOrder?> FindByClientOrderKeyAsync(Guid clientOrderKey, CancellationToken cancellationToken);
    Task<OrderRetailerContext?> FindRetailerContextAsync(Guid salesUserId, Guid retailerId, CancellationToken cancellationToken);
    Task<Product?> FindPublishedProductAsync(Guid productId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<OrderInventoryCandidate>> ListCandidatesAsync(Guid productId, CancellationToken cancellationToken);
    Task<MapLocation?> FindRetailerLocationAsync(Guid retailerId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<TransitTimeRule>> ListTransitRulesAsync(IReadOnlyCollection<Guid> warehouseIds, CancellationToken cancellationToken);
    Task<decimal?> FindMlorRatioAsync(Guid retailerId, Guid productId, CancellationToken cancellationToken);
    Task<string?> FindConfigurationAsync(string key, CancellationToken cancellationToken);
    Task<string> NextOrderCodeAsync(CancellationToken cancellationToken);
    Task<OrderView?> FindAsync(Guid orderId, bool forUpdate, CancellationToken cancellationToken);
    Task<OrderView?> FindByCodeAsync(string orderCode, bool forUpdate, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<OrderView>> ListAsync(Guid actorUserId, string actorRole, string? status, Guid? retailerId, CancellationToken cancellationToken);
    Task<bool> CanViewAsync(OrderView order, Guid actorUserId, string actorRole, CancellationToken cancellationToken);
    void AddOrder(SalesOrder order);
    void AddLine(SalesOrderLine line);
    void AddAllocation(Allocation allocation);
    void AddPromotion(OrderPromotion promotion);
    void AddHistory(OrderStatusHistory history);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken);
    void ClearTracking();
}
