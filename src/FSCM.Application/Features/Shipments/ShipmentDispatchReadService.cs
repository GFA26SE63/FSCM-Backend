using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Shipments;

public sealed record DispatchPickingItem(string Sku, string ProductName, decimal RequestedQuantity);
public sealed record DispatchPickingRead(Guid Id, Guid OrderId, Guid WarehouseId, IReadOnlyCollection<DispatchPickingItem> Items);
public sealed record ShipmentReadinessSnapshot(SalesOrder Order,
    IReadOnlyCollection<ShipmentReadinessPartResult> Parts,
    IReadOnlyCollection<ExpectedWarehousePart> Expected, string? PaymentStatus);

public interface IShipmentDispatchReadStore
{
    Task<IReadOnlyCollection<DispatchPickingRead>> ListPickingAsync(Guid actor, string role, CancellationToken ct);
    Task<IReadOnlyCollection<ShipmentReadinessSnapshot>> ReadAsync(Guid[] orderIds, Guid actor, string role, CancellationToken ct);
}

public sealed class ShipmentDispatchReadService(IShipmentDispatchReadStore store)
{
    public Task<IReadOnlyCollection<DispatchPickingRead>> ListPickingAsync(Guid actor, string role, CancellationToken ct) =>
        store.ListPickingAsync(actor, role, ct);

    public async Task<IReadOnlyCollection<OrderShipmentReadinessResult>> ReadAsync(Guid[] orderIds, Guid actor, string role, CancellationToken ct)
    {
        if (orderIds.Length > 50 || orderIds.Contains(Guid.Empty))
            throw ShipmentOperationException.Validation("Request between zero and 50 valid order IDs.");
        var ids = orderIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        var snapshots = await store.ReadAsync(ids, actor, role, ct);
        // Do not leak which requested IDs exist outside the caller's resource scope.
        if (snapshots.Count != ids.Length)
            throw ShipmentOperationException.Forbidden("You cannot view all requested order deliveries.");
        return snapshots.Select(row => ShipmentService.ComposeReadiness(row.Order, row.Parts, row.Expected,
            ShipmentService.IsPaymentSettled(row.Order, row.PaymentStatus))).ToArray();
    }
}
