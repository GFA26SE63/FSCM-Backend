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
    decimal NetAmount);

public sealed record ShipmentView(
    Shipment Shipment,
    SalesOrder Order,
    Warehouse Warehouse,
    string PickingCode,
    IReadOnlyCollection<ShipmentLineView> Lines);

/// <summary>
/// A Ready run picking list + one of its orders that has no shipment part yet (F92 reconciliation
/// source). A run picking list spans many orders, so each (list, order) pair is one source.
/// </summary>
public sealed record PendingShipmentSource(
    Guid RunPickingListId,
    Guid SalesOrderId,
    Guid WarehouseId,
    decimal TotalQuantity);

public interface IShipmentStore
{
    Task<bool> CanAccessWarehouseAsync(Guid userId, string role, Guid warehouseId, CancellationToken cancellationToken);
    Task<bool> CanViewOrderAsync(Guid orderId, Guid userId, string role, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ShipmentView>> ListAsync(
        Guid userId, string role, Guid? orderId, Guid? warehouseId, string? status, CancellationToken cancellationToken);
    Task<ShipmentView?> FindByIdAsync(Guid id, bool forUpdate, CancellationToken cancellationToken);

    /// <summary>Completed picking lists (optionally for one order) that do not yet have a shipment part.</summary>
    Task<IReadOnlyCollection<PendingShipmentSource>> ListPendingSourcesAsync(
        Guid? orderId, CancellationToken cancellationToken);

    Task<string> NextShipmentCodeAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<Shipment>> ListByOrderAsync(Guid orderId, CancellationToken cancellationToken);
    Task<Driver?> FindDriverAsync(Guid id, CancellationToken cancellationToken);
    Task<Vehicle?> FindVehicleAsync(Guid id, CancellationToken cancellationToken);

    void AddShipment(Shipment shipment);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
