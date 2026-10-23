using FSCM.Application.Features.Payments;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Shipments;

/// <summary>
/// F92: manages delivery parts (shipments) per (sales order, warehouse) pair. Shipments are
/// reconciled from the warehouse picking lists once issued, aggregate the picked quantity and
/// line/batch/price linkage, and carry the per-part lifecycle. Driver/vehicle assignment (F58)
/// and issue confirmation (F96) drive later transitions; partial receipt never auto-completes.
/// Also composes the F60 retailer delivery view (shipments + payment status).
/// </summary>
public sealed class ShipmentService(IShipmentStore store, IPaymentStore paymentStore, TimeProvider timeProvider)
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public async Task<IReadOnlyCollection<ShipmentResult>> ListAsync(
        Guid actorUserId, string actorRole, Guid? orderId, Guid? warehouseId, string? status,
        CancellationToken cancellationToken)
    {
        ValidateReadRole(actorRole);
        if (!string.IsNullOrWhiteSpace(status) && !ShipmentStatusSet.Contains(status))
            throw ShipmentOperationException.Validation("Shipment status filter is invalid.");
        await ReconcileAsync(orderId, cancellationToken);
        var views = await store.ListAsync(actorUserId, actorRole, orderId, warehouseId, status, cancellationToken);
        return views.Select(ToResult).ToArray();
    }

    public async Task<ShipmentResult> GetAsync(
        Guid id, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        ValidateReadRole(actorRole);
        var view = await store.FindByIdAsync(id, false, cancellationToken)
            ?? throw ShipmentOperationException.NotFound("Shipment was not found.");
        if (!await store.CanViewOrderAsync(view.Order.Id, actorUserId, actorRole, cancellationToken))
            throw ShipmentOperationException.Forbidden("You cannot view this shipment.");
        return ToResult(view);
    }

    /// <summary>
    /// F60: returns the logged-in retailer's delivery view of one order — its per-warehouse
    /// shipments (with assigned driver/vehicle) and the payment status. Visibility is enforced via
    /// the order-scope check; locked per-batch prices are returned as stored (no recomputation).
    /// </summary>
    public async Task<RetailerOrderDeliveryResult> GetRetailerOrderDeliveryAsync(
        Guid orderId, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        ValidateReadRole(actorRole);
        if (orderId == Guid.Empty)
            throw ShipmentOperationException.Validation("Order id is required.");
        if (!await store.CanViewOrderAsync(orderId, actorUserId, actorRole, cancellationToken))
            throw ShipmentOperationException.Forbidden("You cannot view this order's deliveries.");
        await ReconcileAsync(orderId, cancellationToken);
        var views = await store.ListAsync(actorUserId, actorRole, orderId, null, null, cancellationToken);
        var shipments = views.Select(ToResult).ToArray();
        var orderCode = shipments.FirstOrDefault()?.OrderCode ?? string.Empty;

        var payment = await paymentStore.FindByOrderAsync(orderId, false, cancellationToken);
        OrderPaymentStatusResult? paymentStatus = null;
        if (payment is not null)
        {
            if (string.IsNullOrEmpty(orderCode)) orderCode = payment.Order.OrderCode;
            var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().ToOffset(VietnamOffset).DateTime);
            var overdue = payment.Payment.Status != PaymentStatuses.Paid &&
                          payment.Payment.DueDate.HasValue && today > payment.Payment.DueDate.Value;
            paymentStatus = new OrderPaymentStatusResult(
                payment.Payment.Method,
                overdue ? PaymentStatuses.Overdue : payment.Payment.Status,
                payment.Payment.Amount,
                payment.Payment.AdjustedAmount,
                payment.Payment.ConfirmedAmount,
                Math.Max(0, payment.Payment.AdjustedAmount - payment.Payment.ConfirmedAmount),
                payment.Payment.DueDate,
                overdue ? today.DayNumber - payment.Payment.DueDate!.Value.DayNumber : 0);
        }

        return new RetailerOrderDeliveryResult(orderId, orderCode, shipments, paymentStatus);
    }

    /// <summary>
    /// Creates any missing shipment parts for completed picking lists (idempotent). Invoked
    /// implicitly before reads and exposed so an Operator can force reconciliation for an order.
    /// </summary>
    public async Task<IReadOnlyCollection<ShipmentResult>> SyncOrderAsync(
        Guid orderId, string actorRole, CancellationToken cancellationToken)
    {
        EnsureOperator(actorRole);
        if (orderId == Guid.Empty)
            throw ShipmentOperationException.Validation("Order id is required.");
        await ReconcileAsync(orderId, cancellationToken);
        var views = await store.ListAsync(Guid.Empty, SystemRoles.Operator, orderId, null, null, cancellationToken);
        return views.Select(ToResult).ToArray();
    }

    public async Task<ShipmentResult> FlagExceptionAsync(
        Guid id, FlagShipmentExceptionData data, string actorRole, CancellationToken cancellationToken)
    {
        EnsureOperator(actorRole);
        var reason = NormalizeReason(data.Reason);
        var view = await store.FindByIdAsync(id, true, cancellationToken)
            ?? throw ShipmentOperationException.NotFound("Shipment was not found.");
        if (view.Shipment.Version != data.ExpectedVersion)
            throw ShipmentOperationException.Conflict(
                $"Shipment version is {view.Shipment.Version}; refresh and retry.");
        if (view.Shipment.Status == ShipmentStatuses.Received)
            throw ShipmentOperationException.Conflict("A received shipment cannot be flagged as an exception.");

        var now = timeProvider.GetUtcNow();
        view.Shipment.Status = ShipmentStatuses.Exception;
        view.Shipment.ExceptionReason = reason;
        view.Shipment.Version++;
        view.Shipment.UpdatedAt = now;
        await store.SaveChangesAsync(cancellationToken);
        return ToResult((await store.FindByIdAsync(id, false, cancellationToken))!);
    }

    private async Task ReconcileAsync(Guid? orderId, CancellationToken cancellationToken)
    {
        var sources = await store.ListPendingSourcesAsync(orderId, cancellationToken);
        if (sources.Count == 0) return;
        var now = timeProvider.GetUtcNow();
        foreach (var source in sources)
        {
            store.AddShipment(new Shipment
            {
                Id = Guid.NewGuid(),
                SalesOrderId = source.SalesOrderId,
                WarehouseId = source.WarehouseId,
                PickingListId = source.PickingList.Id,
                ShipmentCode = await store.NextShipmentCodeAsync(cancellationToken),
                Status = ShipmentStatuses.Ready,
                TotalQuantity = source.TotalQuantity,
                ReceivedQuantity = 0,
                ReadyAt = now,
                Version = 0,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task<ShipmentResult> AssignAsync(
        Guid id, AssignShipmentData data, string actorRole, CancellationToken cancellationToken)
    {
        EnsureOperator(actorRole);
        if (data.DriverId == Guid.Empty || data.VehicleId == Guid.Empty)
            throw ShipmentOperationException.Validation("Driver and vehicle are required.");
        var view = await store.FindByIdAsync(id, true, cancellationToken)
            ?? throw ShipmentOperationException.NotFound("Shipment was not found.");
        if (view.Shipment.Version != data.ExpectedVersion)
            throw ShipmentOperationException.Conflict(
                $"Shipment version is {view.Shipment.Version}; refresh and retry.");
        // F58: assignment is only valid on a part that is ready or already issued; it never
        // changes stock or forces "Delivering" — that follows the F96 issue confirmation.
        if (view.Shipment.Status is not (ShipmentStatuses.Ready or ShipmentStatuses.Issued))
            throw ShipmentOperationException.Conflict(
                "Only a ready or issued shipment can be assigned a driver and vehicle.");

        var driver = await store.FindDriverAsync(data.DriverId, cancellationToken)
            ?? throw ShipmentOperationException.NotFound("Driver was not found.");
        var vehicle = await store.FindVehicleAsync(data.VehicleId, cancellationToken)
            ?? throw ShipmentOperationException.NotFound("Vehicle was not found.");
        if (!driver.IsActive || !vehicle.IsActive)
            throw ShipmentOperationException.Conflict("Driver and vehicle must both be active.");
        if (driver.WarehouseId != view.Shipment.WarehouseId || vehicle.WarehouseId != view.Shipment.WarehouseId)
            throw ShipmentOperationException.Conflict(
                "Driver and vehicle must belong to the shipment's warehouse.");

        var now = timeProvider.GetUtcNow();
        view.Shipment.DriverId = driver.Id;
        view.Shipment.VehicleId = vehicle.Id;
        view.Shipment.DriverCodeSnapshot = driver.Code;
        view.Shipment.DriverNameSnapshot = driver.FullName;
        view.Shipment.DriverPhoneSnapshot = driver.Phone;
        view.Shipment.DriverLicenseSnapshot = driver.LicenseNumber;
        view.Shipment.PlateNumberSnapshot = vehicle.PlateNumber;
        view.Shipment.VehicleTypeSnapshot = vehicle.VehicleType;
        view.Shipment.AssignedAt = now;
        view.Shipment.Version++;
        view.Shipment.UpdatedAt = now;
        await store.SaveChangesAsync(cancellationToken);
        return ToResult((await store.FindByIdAsync(id, false, cancellationToken))!);
    }

    private static ShipmentResult ToResult(ShipmentView view) => new(
        view.Shipment.Id, view.Order.Id, view.Order.OrderCode,
        view.Warehouse.Id, view.Warehouse.Code, view.Warehouse.Name,
        view.PickingList.Id, view.PickingList.PickingCode,
        view.Shipment.ShipmentCode, view.Shipment.Status,
        view.Shipment.TotalQuantity, view.Shipment.ReceivedQuantity,
        view.Shipment.DriverId, view.Shipment.DriverCodeSnapshot, view.Shipment.DriverNameSnapshot,
        view.Shipment.DriverPhoneSnapshot, view.Shipment.DriverLicenseSnapshot,
        view.Shipment.VehicleId, view.Shipment.PlateNumberSnapshot, view.Shipment.VehicleTypeSnapshot,
        view.Shipment.AssignedAt,
        view.Shipment.ReadyAt, view.Shipment.IssuedAt, view.Shipment.DispatchedAt,
        view.Shipment.ReceivedAt, view.Shipment.ExceptionReason,
        view.Shipment.Version, view.Shipment.CreatedAt, view.Shipment.UpdatedAt,
        view.Lines.Select(line => new ShipmentLineResult(
            line.OrderLineId, line.LineNumber, line.ProductId, line.Sku, line.ProductName,
            line.BatchId, line.BatchCode, line.Quantity, line.UnitPrice, line.NetAmount)).ToArray());

    private static void EnsureOperator(string actorRole)
    {
        if (actorRole is not (SystemRoles.Admin or SystemRoles.Operator))
            throw ShipmentOperationException.Forbidden("Only Admin or Operator can manage shipments.");
    }

    private static void ValidateReadRole(string role)
    {
        if (role is not (SystemRoles.Admin or SystemRoles.Operator or SystemRoles.SalesManager or
            SystemRoles.Sales or SystemRoles.WarehouseKeeper or SystemRoles.Retailer))
            throw ShipmentOperationException.Forbidden("Role cannot view shipments.");
    }

    private static string NormalizeReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw ShipmentOperationException.Validation("Exception reason is required.");
        reason = reason.Trim();
        if (reason.Length > 500)
            throw ShipmentOperationException.Validation("Exception reason cannot exceed 500 characters.");
        return reason;
    }

    private static readonly HashSet<string> ShipmentStatusSet =
    [
        ShipmentStatuses.WaitingToPick,
        ShipmentStatuses.Ready,
        ShipmentStatuses.Issued,
        ShipmentStatuses.Delivering,
        ShipmentStatuses.Received,
        ShipmentStatuses.Exception
    ];
}
