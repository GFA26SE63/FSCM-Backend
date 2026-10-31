using FSCM.Application.Features.Orders;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Picking;

public sealed record PickingItemView(
    PickingItem Item,
    Allocation Allocation,
    SalesOrderLine OrderLine,
    Batch Batch);

public sealed record PickingScanView(QrLabelEvent Event, string QrCode);

public sealed record PickingView(
    PickingList List,
    SalesOrder Order,
    Warehouse Warehouse,
    IReadOnlyCollection<PickingItemView> Items,
    IReadOnlyCollection<PickingScanView> Scans,
    IReadOnlyDictionary<Guid, int> InventoryVersions);

public interface IPickingPlanService
{
    Task CancelForOrderAsync(Guid orderId, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IPickingStore
{
    Task<bool> CanAccessWarehouseAsync(Guid userId, string role, Guid warehouseId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<PickingView>> ListAsync(
        Guid userId, string role, Guid? warehouseId, string? status, CancellationToken cancellationToken);
    Task<PickingView?> FindAsync(string pickingCode, bool forUpdate, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<PickingList>> ListByOrderAsync(Guid orderId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<PickingItem>> ListItemsByOrderAsync(Guid orderId, CancellationToken cancellationToken);
    Task<QrLabelEvent?> FindOperationAsync(string operationKey, CancellationToken cancellationToken);
    Task<BatchQrLabel?> FindLabelForUpdateAsync(string qrCode, CancellationToken cancellationToken);
    Task<BatchQrLabel?> FindLabelByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<string> NextQrCodeAsync(CancellationToken cancellationToken);
    Task<string> NextShipmentCodeAsync(CancellationToken cancellationToken);
    void AddList(PickingList list);
    void AddItem(PickingItem item);
    void AddLabel(BatchQrLabel label);
    void AddLabelEvent(QrLabelEvent labelEvent);
    void AddOrderHistory(OrderStatusHistory history);
    void AddShipment(Shipment shipment);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
    void ClearTracking();
}
