using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Shipments;

public sealed record ShipmentLineView(
    Guid OrderLineId,
    int LineNumber,
    Guid ProductId,
    string Sku,
    string ProductName,
    Guid BatchId,
    string BatchCode,
    decimal Quantity,
    decimal UnitPrice,
    decimal NetAmount,
    decimal? ReceivedQuantity = null,
    decimal? DamagedQuantity = null,
    decimal? MissingQuantity = null);

public sealed record ShipmentReceiptView(
    ReceiptConfirmation Receipt,
    IReadOnlyCollection<ReceiptConfirmationItem> Lines,
    IReadOnlyCollection<string> QrCodes);

public sealed record ShipmentLabelView(BatchQrLabel Label, Guid BatchId);

public sealed record ShipmentView(
    Shipment Shipment,
    SalesOrder Order,
    Warehouse Warehouse,
    string PickingCode,
    IReadOnlyCollection<ShipmentLineView> Lines,
    ShipmentReceiptView? Receipt = null);

/// <summary>
/// A Ready run picking list + one of its orders that has no shipment part yet (F92 reconciliation
/// source). A run picking list spans many orders, so each (list, order) pair is one source.
/// </summary>
/// <summary>
/// A warehouse an order is expected to leave from, taken from the run picking sheets the approved
/// plan produced, together with the state of that sheet.
///
/// This exists because a shipment part is only created once its warehouse closes its sheet, so the
/// set of shipment rows is not the set of warehouses an order is waiting on. Reading readiness from
/// the rows alone made a half-picked order look ready.
/// </summary>
public sealed record ExpectedWarehousePart(
    Guid WarehouseId,
    string WarehouseCode,
    string PickingCode,
    string PickingStatus);

public sealed record PendingShipmentSource(
    Guid RunPickingListId,
    Guid SalesOrderId,
    Guid WarehouseId,
    decimal TotalQuantity);

/// <summary>
/// One batch of a shipment at the moment it is issued: how much leaves, the balance it leaves, and
/// the holds that paid for it. The holds are carried so issuing consumes the very rows the approval
/// created, rather than decrementing a reserved total that might have been held for something else.
/// </summary>
public sealed record ShipmentIssueRow(
    Guid BatchId,
    decimal Quantity,
    Batch Batch,
    InventoryBalance Balance,
    IReadOnlyCollection<Reservation> Holds);

public interface IShipmentStore
{
    Task<bool> CanAccessWarehouseAsync(Guid userId, string role, Guid warehouseId, CancellationToken cancellationToken);
    Task<bool> CanViewOrderAsync(Guid orderId, Guid userId, string role, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ShipmentView>> ListAsync(
        Guid userId, string role, Guid? orderId, Guid? warehouseId, string? status, CancellationToken cancellationToken);
    Task<ShipmentView?> FindByIdAsync(Guid id, bool forUpdate, CancellationToken cancellationToken);
    Task<ShipmentView?> FindByReceiptOperationKeyAsync(string operationKey, CancellationToken cancellationToken);
    Task<SalesOrder?> FindOrderForUpdateAsync(Guid orderId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ShipmentLabelView>> ListLabelsAsync(
        Guid shipmentId, bool forUpdate, CancellationToken cancellationToken);

    /// <summary>Completed picking lists (optionally for one order) that do not yet have a shipment part.</summary>
    /// <summary>
    /// Every warehouse the approved plan expects this order to leave from, whether or not a
    /// shipment part exists for it yet.
    /// </summary>
    Task<IReadOnlyCollection<ExpectedWarehousePart>> ListExpectedWarehousesAsync(
        Guid orderId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<PendingShipmentSource>> ListPendingSourcesAsync(
        Guid? orderId, CancellationToken cancellationToken);

    Task<string> NextShipmentCodeAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<Shipment>> ListByOrderAsync(Guid orderId, CancellationToken cancellationToken);
    Task<Driver?> FindDriverAsync(Guid id, CancellationToken cancellationToken);
    Task<Vehicle?> FindVehicleAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>What one ready shipment would take out of stock, per batch, locked for update.</summary>
    Task<IReadOnlyCollection<ShipmentIssueRow>> ListIssuableAsync(
        Guid shipmentId, CancellationToken cancellationToken);

    void AddInventoryTransaction(InventoryTransaction transaction);

    void AddShipment(Shipment shipment);
    void AddReceipt(ReceiptConfirmation receipt);
    void AddReceiptLine(ReceiptConfirmationItem line);
    void AddReceiptQrLabel(ReceiptConfirmationQrLabel qrLabel);
    void AddLabelEvent(QrLabelEvent labelEvent);
    void AddOrderHistory(OrderStatusHistory history);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
    void ClearTracking();
}
