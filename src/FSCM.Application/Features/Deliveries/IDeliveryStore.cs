using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Deliveries;

public sealed record DeliveryOrderView(SalesOrder Order, Retailer Retailer);

public sealed record DeliveryLabelView(
    BatchQrLabel Label,
    Guid BatchId,
    string QrCode,
    string PickingCode);

public sealed record DeliveryInventoryIssue(Guid AllocationId, Guid BatchId, int InventoryVersion);

public sealed record ReceiptItemView(ReceiptConfirmationItem Item, string QrCode);

public sealed record DeliveryView(
    Delivery Delivery,
    SalesOrder Order,
    Retailer Retailer,
    ReceiptConfirmation? Receipt,
    IReadOnlyCollection<ReceiptItemView> ReceiptItems);

public interface IDeliveryStore
{
    Task<IReadOnlyCollection<Driver>> ListDriversAsync(bool includeInactive, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<Vehicle>> ListVehiclesAsync(bool includeInactive, CancellationToken cancellationToken);
    Task<Driver?> FindDriverAsync(Guid id, CancellationToken cancellationToken);
    Task<Vehicle?> FindVehicleAsync(Guid id, CancellationToken cancellationToken);
    Task<DeliveryOrderView?> FindOrderAsync(Guid orderId, bool forUpdate, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<DeliveryLabelView>> ListOrderLabelsAsync(
        Guid orderId, bool forUpdate, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<DeliveryInventoryIssue>> ListOrderInventoryIssuesAsync(
        Guid orderId, CancellationToken cancellationToken);
    Task<DeliveryView?> FindByOrderAsync(Guid orderId, bool forUpdate, CancellationToken cancellationToken);
    Task<DeliveryView?> FindByOperationKeyAsync(string operationKey, CancellationToken cancellationToken);
    Task<DeliveryView?> FindByReceiptOperationKeyAsync(string operationKey, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<DeliveryView>> ListAsync(
        Guid actorUserId, string actorRole, string? status, CancellationToken cancellationToken);
    Task<bool> CanViewAsync(DeliveryView delivery, Guid actorUserId, string actorRole,
        CancellationToken cancellationToken);
    void AddDelivery(Delivery delivery);
    void AddReceipt(ReceiptConfirmation receipt);
    void AddReceiptItem(ReceiptConfirmationItem item);
    void AddLabelEvent(QrLabelEvent labelEvent);
    void AddOrderHistory(OrderStatusHistory history);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
    void ClearTracking();
}
