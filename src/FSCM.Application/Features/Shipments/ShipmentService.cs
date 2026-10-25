using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using FSCM.Application.Features.Deliveries;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.Payments;
using FSCM.Application.Features.QrLabels;
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
public sealed class ShipmentService(
    IShipmentStore store,
    IPaymentStore paymentStore,
    IPaymentPlanService paymentPlanService,
    TimeProvider timeProvider)
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

    /// <summary>
    /// F93: whether an order may leave the warehouses at all, and what is holding it if not.
    ///
    /// The run already refuses to split an order across days; the door refuses to split it across
    /// warehouses. Every part must be picked and the payment condition met before any part moves,
    /// so one warehouse short or damaged stops the whole order rather than sending what happens to
    /// be ready and leaving the retailer to chase the rest.
    ///
    /// Parts that have already left are not re-checked. They are gone, and counting them as
    /// blockers would make a half-dispatched order permanently unready.
    /// </summary>
    public async Task<OrderShipmentReadinessResult> GetOrderReadinessAsync(
        Guid orderId, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        if (!await store.CanViewOrderAsync(orderId, actorUserId, actorRole, cancellationToken))
            throw ShipmentOperationException.Forbidden("You cannot view this order deliveries.");
        await ReconcileAsync(orderId, cancellationToken);
        var views = await store.ListAsync(actorUserId, actorRole, orderId, null, null, cancellationToken);
        if (views.Count == 0)
            throw ShipmentOperationException.NotFound("This order has no delivery parts yet.");
        return await BuildReadinessAsync(views, cancellationToken);
    }

    private async Task<OrderShipmentReadinessResult> BuildReadinessAsync(
        IReadOnlyCollection<ShipmentView> views, CancellationToken cancellationToken)
    {
        var order = views.First().Order;
        var parts = views
            .OrderBy(item => item.Warehouse.Code, StringComparer.Ordinal)
            .Select(item => new ShipmentReadinessPartResult(
                item.Shipment.Id, item.Shipment.ShipmentCode, item.Warehouse.Id, item.Warehouse.Code,
                item.Shipment.Status, item.Shipment.ExceptionReason))
            .ToArray();

        var reasons = new List<string>();
        foreach (var part in parts.Where(item => item.Status == ShipmentStatuses.Exception))
        {
            var detail = part.ExceptionReason ?? "chua ghi ly do";
            reasons.Add($"Kho {part.WarehouseCode} dang co ngoai le: {detail}.");
        }

        foreach (var part in parts.Where(item => item.Status == ShipmentStatuses.WaitingToPick))
        {
            reasons.Add($"Kho {part.WarehouseCode} chua soan xong phan hang cua don.");
        }

        var paymentSettled = await IsPaymentSettledAsync(order, cancellationToken);
        if (!paymentSettled)
        {
            reasons.Add("Don tra truoc chua thanh toan du, chua du dieu kien xuat kho.");
        }

        return new OrderShipmentReadinessResult(
            order.Id,
            order.OrderCode,
            reasons.Count == 0,
            paymentSettled,
            parts.Any(item => item.Status == ShipmentStatuses.Exception),
            reasons,
            parts);
    }

    /// <summary>
    /// Only a prepaid order has to be paid before it moves. COD is collected on delivery and credit
    /// is settled on terms, so demanding payment from either would stop orders that are in order.
    /// </summary>
    private async Task<bool> IsPaymentSettledAsync(SalesOrder order, CancellationToken cancellationToken)
    {
        if (order.PaymentMethod != OrderPaymentMethods.Prepaid) return true;
        var payment = await paymentStore.FindByOrderAsync(order.Id, false, cancellationToken);
        return payment?.Payment.Status == PaymentStatuses.Paid;
    }

    /// <summary>
    /// F96: the warehouse confirms this part physically left.
    ///
    /// This is the first and only moment the stock actually moves. Approval held it, which put
    /// Reserved up and left OnHand alone, and picking gathered it; issuing takes both down
    /// together, so the two numbers cannot drift apart.
    ///
    /// Calling it again returns the part as it stands instead of deducting a second time. A
    /// warehouse keeper whose connection dropped mid-confirmation will press the button again, and
    /// a duplicated deduction would stay invisible until someone counted the shelf.
    ///
    /// The order only becomes Issued once every part has left, which is the same all-or-nothing
    /// rule the readiness check applies before the first one does.
    /// </summary>
    public async Task<ShipmentResult> ConfirmIssueAsync(
        Guid id, ConfirmShipmentIssueData data, Guid actorUserId, string actorRole,
        CancellationToken cancellationToken)
    {
        if (actorRole is not (SystemRoles.Admin or SystemRoles.Operator or SystemRoles.WarehouseKeeper))
            throw ShipmentOperationException.Forbidden(
                "Only Admin, Operator or the warehouse keeper can confirm a shipment issue.");

        await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var view = await store.FindByIdAsync(id, true, transactionCancellationToken)
                ?? throw ShipmentOperationException.NotFound("Shipment was not found.");
            if (!await store.CanAccessWarehouseAsync(
                    actorUserId, actorRole, view.Shipment.WarehouseId, transactionCancellationToken))
            {
                throw ShipmentOperationException.Forbidden("You cannot issue this warehouse shipments.");
            }

            // Already gone: say so by returning it unchanged rather than deducting again.
            if (view.Shipment.Status is ShipmentStatuses.Issued or ShipmentStatuses.Delivering
                or ShipmentStatuses.Received)
            {
                return;
            }

            if (view.Shipment.Status != ShipmentStatuses.Ready)
            {
                throw ShipmentOperationException.Conflict(
                    $"Shipment is {view.Shipment.Status}; only a ready part can be issued.");
            }

            if (view.Shipment.Version != data.ExpectedVersion)
            {
                throw ShipmentOperationException.Conflict(
                    $"Shipment version is {view.Shipment.Version}; refresh and retry.");
            }

            var siblings = await store.ListAsync(
                actorUserId, SystemRoles.Admin, view.Order.Id, null, null, transactionCancellationToken);
            var readiness = await BuildReadinessAsync(siblings, transactionCancellationToken);
            if (!readiness.IsReady)
            {
                throw ShipmentOperationException.Conflict(string.Join(" ", readiness.BlockingReasons));
            }

            var now = timeProvider.GetUtcNow();
            var rows = await store.ListIssuableAsync(id, transactionCancellationToken);
            if (rows.Count == 0)
            {
                throw ShipmentOperationException.Conflict(
                    "This part has no held stock to issue; its reservation may already have been released.");
            }

            foreach (var row in rows)
            {
                if (row.Balance.PhysicalQuantity < row.Quantity || row.Balance.ReservedQuantity < row.Quantity)
                {
                    throw ShipmentOperationException.Conflict(
                        $"Batch {row.Batch.BatchCode} holds {row.Balance.ReservedQuantity:0.###} of " +
                        $"{row.Balance.PhysicalQuantity:0.###} on hand, which cannot cover {row.Quantity:0.###}.");
                }

                // Both sides fall together: the goods leave and the hold that paid for them ends.
                row.Balance.PhysicalQuantity -= row.Quantity;
                row.Balance.ReservedQuantity -= row.Quantity;
                row.Balance.AvailableQuantity = row.Balance.PhysicalQuantity - row.Balance.ReservedQuantity;
                row.Balance.Version++;
                row.Balance.UpdatedAt = now;

                foreach (var hold in row.Holds)
                {
                    hold.Status = ReservationStatuses.Consumed;
                    hold.ConsumedAt = now;
                }

                store.AddInventoryTransaction(new InventoryTransaction
                {
                    Id = Guid.NewGuid(),
                    BatchId = row.Batch.Id,
                    WarehouseId = row.Batch.WarehouseId,
                    ProductId = row.Batch.ProductId,
                    CreatedByUserId = actorUserId,
                    TransactionType = InventoryTransactionTypes.OrderIssue,
                    SourceType = InventoryTransactionSourceTypes.Allocation,
                    SourceId = view.Shipment.Id,
                    ReferenceCode = view.Shipment.ShipmentCode,
                    PhysicalQuantityDelta = -row.Quantity,
                    ReservedQuantityDelta = -row.Quantity,
                    PhysicalQuantityAfter = row.Balance.PhysicalQuantity,
                    ReservedQuantityAfter = row.Balance.ReservedQuantity,
                    AvailableQuantityAfter = row.Balance.AvailableQuantity,
                    OccurredAt = now
                });
            }

            view.Shipment.Status = ShipmentStatuses.Issued;
            view.Shipment.IssuedAt = now;
            view.Shipment.Version++;
            view.Shipment.UpdatedAt = now;

            var allIssued = siblings.All(item =>
                item.Shipment.Id == view.Shipment.Id ||
                item.Shipment.Status is ShipmentStatuses.Issued or ShipmentStatuses.Delivering
                    or ShipmentStatuses.Received);
            if (allIssued)
            {
                var order = await store.FindOrderForUpdateAsync(view.Order.Id, transactionCancellationToken)
                    ?? throw ShipmentOperationException.NotFound("Order was not found.");
                var fromStatus = order.Status;
                order.Status = OrderStatuses.Issued;
                order.Version++;
                order.UpdatedAt = now;
                store.AddOrderHistory(new OrderStatusHistory
                {
                    Id = Guid.NewGuid(),
                    SalesOrderId = order.Id,
                    FromStatus = fromStatus,
                    ToStatus = OrderStatuses.Issued,
                    ChangedByUserId = actorUserId,
                    Reason = "Moi phan giao theo kho da xuat",
                    ChangedAt = now
                });
            }

            await store.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);

        store.ClearTracking();
        return ToResult(await store.FindByIdAsync(id, false, cancellationToken)
            ?? throw ShipmentOperationException.Conflict("Issue was committed but could not be reloaded."));
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
                RunPickingListId = source.RunPickingListId,
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

    public async Task<ShipmentResult> ConfirmReceiptAsync(
        Guid id,
        ConfirmShipmentReceiptData data,
        string operationKey,
        Guid retailerUserId,
        string actorRole,
        CancellationToken cancellationToken)
    {
        if (actorRole != SystemRoles.Retailer)
            throw ShipmentOperationException.Forbidden("Only the order Retailer can confirm a shipment receipt.");
        operationKey = NormalizeOperationKey(operationKey);
        var note = NormalizeNote(data.Note);
        var qrCodes = NormalizeQrCodes(data.QrCodes);
        var lines = NormalizeReceiptLines(data.Lines);
        var requestHash = HashReceipt(id, qrCodes, lines, note);

        var prior = await store.FindByReceiptOperationKeyAsync(operationKey, cancellationToken);
        if (prior is not null)
        {
            if (prior.Shipment.Id != id || prior.Receipt?.Receipt.RequestHash != requestHash)
                throw ShipmentOperationException.Conflict(
                    "Idempotency key was already used for a different shipment receipt.");
            if (!await store.CanViewOrderAsync(prior.Order.Id, retailerUserId, actorRole, cancellationToken))
                throw ShipmentOperationException.Forbidden("You cannot confirm this shipment receipt.");
            return ToResult(prior);
        }

        await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var view = await store.FindByIdAsync(id, true, transactionCancellationToken)
                ?? throw ShipmentOperationException.NotFound("Shipment was not found.");
            if (!await store.CanViewOrderAsync(view.Order.Id, retailerUserId, actorRole, transactionCancellationToken))
                throw ShipmentOperationException.Forbidden("You cannot confirm this shipment receipt.");
            if (view.Shipment.Status != ShipmentStatuses.Delivering)
                throw ShipmentOperationException.Conflict("Only a dispatched shipment can be received.");
            if (view.Receipt is not null)
                throw ShipmentOperationException.Conflict("Shipment receipt was already confirmed.");
            if (view.Shipment.Version != data.ExpectedVersion)
                throw ShipmentOperationException.Conflict(
                    $"Shipment version is {view.Shipment.Version}; refresh and retry.");

            var expected = view.Lines
                .GroupBy(line => (line.OrderLineId, line.BatchId))
                .ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity));
            if (expected.Count != lines.Length || lines.Any(line => !expected.ContainsKey((line.OrderLineId, line.BatchId))))
                throw ShipmentOperationException.Validation(
                    "Receipt must contain every shipment line and batch exactly once.");
            foreach (var line in lines)
            {
                var issued = expected[(line.OrderLineId, line.BatchId)];
                if (line.ReceivedQuantity < 0 || line.ReceivedQuantity > issued ||
                    line.DamagedQuantity < 0 || line.DamagedQuantity > line.ReceivedQuantity)
                    throw ShipmentOperationException.Validation(
                        "Receipt quantities must satisfy 0 <= damaged <= received <= issued.");
            }

            var labels = await store.ListLabelsAsync(id, true, transactionCancellationToken);
            var expectedCodes = labels.Select(item => item.Label.QrCode.Trim().ToUpperInvariant())
                .Order(StringComparer.Ordinal).ToArray();
            if (expectedCodes.Length == 0 || !expectedCodes.SequenceEqual(qrCodes, StringComparer.Ordinal))
                throw ShipmentOperationException.Validation(
                    "Receipt must scan every QR label dispatched for this shipment exactly once.");
            if (labels.Any(item => item.Label.Status != QrLabelStatuses.InTransit))
                throw ShipmentOperationException.Conflict("Every scanned QR label must still be in transit.");
            var expectedByBatch = expected.GroupBy(item => item.Key.BatchId)
                .ToDictionary(group => group.Key, group => group.Sum(item => item.Value));
            var labelByBatch = labels.GroupBy(item => item.BatchId)
                .ToDictionary(group => group.Key, group => group.Sum(item => item.Label.Quantity));
            if (expectedByBatch.Any(item => !labelByBatch.TryGetValue(item.Key, out var quantity) || quantity != item.Value))
                throw ShipmentOperationException.Conflict(
                    "Dispatched QR quantities do not match the shipment line and batch quantities.");

            var now = timeProvider.GetUtcNow();
            var hasException = lines.Any(line =>
                line.ReceivedQuantity < expected[(line.OrderLineId, line.BatchId)] || line.DamagedQuantity > 0);
            var receipt = new ReceiptConfirmation
            {
                Id = Guid.NewGuid(),
                SalesOrderId = view.Order.Id,
                ShipmentId = view.Shipment.Id,
                ConfirmedByRetailerUserId = retailerUserId,
                OperationKey = operationKey,
                RequestHash = requestHash,
                Status = hasException ? ReceiptConfirmationStatuses.Exception : ReceiptConfirmationStatuses.Confirmed,
                ConfirmedAt = now,
                Note = note,
                CreatedAt = now
            };
            store.AddReceipt(receipt);
            foreach (var line in lines)
            {
                var issued = expected[(line.OrderLineId, line.BatchId)];
                var representativeLabel = labels.First(item => item.BatchId == line.BatchId).Label;
                store.AddReceiptLine(new ReceiptConfirmationItem
                {
                    Id = Guid.NewGuid(), ReceiptConfirmationId = receipt.Id,
                    SalesOrderLineId = line.OrderLineId, BatchId = line.BatchId,
                    BatchQrLabelId = representativeLabel.Id, ExpectedQuantity = issued,
                    ReceivedQuantity = line.ReceivedQuantity, DamagedQuantity = line.DamagedQuantity,
                    MissingQuantity = issued - line.ReceivedQuantity, ScannedAt = now
                });
            }
            for (var index = 0; index < labels.Count; index++)
            {
                var label = labels.ElementAt(index).Label;
                store.AddReceiptQrLabel(new ReceiptConfirmationQrLabel
                {
                    Id = Guid.NewGuid(), ReceiptConfirmationId = receipt.Id,
                    BatchQrLabelId = label.Id, ScannedAt = now
                });
                var fromStatus = label.Status;
                label.Status = QrLabelStatuses.Delivered;
                label.Version++;
                label.UpdatedAt = now;
                store.AddLabelEvent(NewLabelEvent(label, retailerUserId,
                    $"shipment-receipt:{operationKey}:{index + 1}", fromStatus, view.Order.OrderCode, now));
            }

            view.Shipment.ReceivedQuantity = lines.Sum(line => line.ReceivedQuantity);
            view.Shipment.ReceivedAt = now;
            view.Shipment.Status = hasException ? ShipmentStatuses.Exception : ShipmentStatuses.Received;
            view.Shipment.ExceptionReason = hasException
                ? "Có số lượng thiếu hoặc hư hỏng khi nhà bán lẻ nhận hàng."
                : null;
            view.Shipment.Version++;
            view.Shipment.UpdatedAt = now;

            var siblings = await store.ListByOrderAsync(view.Order.Id, transactionCancellationToken);
            var allReceived = !hasException && siblings.All(item =>
                item.Id == view.Shipment.Id || item.Status == ShipmentStatuses.Received);
            if (allReceived)
            {
                var order = await store.FindOrderForUpdateAsync(view.Order.Id, transactionCancellationToken)
                    ?? throw ShipmentOperationException.NotFound("Order was not found.");
                var fromStatus = order.Status;
                order.Status = OrderStatuses.Delivered;
                order.Version++;
                order.UpdatedAt = now;
                await paymentPlanService.CreateForDeliveredOrderAsync(
                    order, retailerUserId, now, transactionCancellationToken);
                store.AddOrderHistory(new OrderStatusHistory
                {
                    Id = Guid.NewGuid(), SalesOrderId = order.Id, FromStatus = fromStatus,
                    ToStatus = OrderStatuses.Delivered, ChangedByUserId = retailerUserId,
                    Reason = "Nhà bán lẻ đã nhận đủ mọi phần giao theo kho", ChangedAt = now
                });
            }
            await store.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);

        store.ClearTracking();
        return ToResult(await store.FindByIdAsync(id, false, cancellationToken)
            ?? throw ShipmentOperationException.Conflict("Receipt was committed but could not be reloaded."));
    }

    private static ShipmentResult ToResult(ShipmentView view) => new(
        view.Shipment.Id, view.Order.Id, view.Order.OrderCode,
        view.Warehouse.Id, view.Warehouse.Code, view.Warehouse.Name,
        view.Shipment.RunPickingListId, view.PickingCode,
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
            line.BatchId, line.BatchCode, line.Quantity, line.UnitPrice, line.NetAmount,
            line.ReceivedQuantity, line.DamagedQuantity, line.MissingQuantity)).ToArray(),
        view.Receipt is null ? null : new ShipmentReceiptResult(
            view.Receipt.Receipt.Id, view.Receipt.Receipt.Status, view.Receipt.Receipt.ConfirmedAt,
            view.Receipt.Receipt.Note,
            view.Receipt.Lines.Where(line => line.SalesOrderLineId.HasValue)
                .Select(line => new ShipmentReceiptLineResult(
                    line.SalesOrderLineId!.Value, line.BatchId, line.ExpectedQuantity,
                    line.ReceivedQuantity, line.DamagedQuantity, line.MissingQuantity)).ToArray()));

    private static ConfirmShipmentReceiptLineData[] NormalizeReceiptLines(
        IReadOnlyCollection<ConfirmShipmentReceiptLineData> values)
    {
        if (values.Count == 0)
            throw ShipmentOperationException.Validation("At least one receipt line is required.");
        if (values.Any(line => line.OrderLineId == Guid.Empty || line.BatchId == Guid.Empty))
            throw ShipmentOperationException.Validation("Order line id and batch id are required.");
        var result = values.OrderBy(line => line.OrderLineId).ThenBy(line => line.BatchId).ToArray();
        if (result.Select(line => (line.OrderLineId, line.BatchId)).Distinct().Count() != result.Length)
            throw ShipmentOperationException.Validation("Receipt line and batch pairs cannot be repeated.");
        return result;
    }

    private static string[] NormalizeQrCodes(IReadOnlyCollection<string> values)
    {
        if (values.Count == 0)
            throw ShipmentOperationException.Validation("At least one QR code is required.");
        var result = values.Select(value => string.IsNullOrWhiteSpace(value)
                ? throw ShipmentOperationException.Validation("QR code cannot be empty.")
                : value.Trim().ToUpperInvariant())
            .Order(StringComparer.Ordinal).ToArray();
        if (result.Distinct(StringComparer.Ordinal).Count() != result.Length)
            throw ShipmentOperationException.Validation("QR codes cannot be repeated.");
        return result;
    }

    private static string NormalizeOperationKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw ShipmentOperationException.Validation("Idempotency-Key header is required.");
        value = value.Trim();
        if (value.Length > 80)
            throw ShipmentOperationException.Validation("Idempotency-Key cannot exceed 80 characters.");
        return value;
    }

    private static string? NormalizeNote(string? value)
    {
        if (value?.Trim().Length > 500)
            throw ShipmentOperationException.Validation("Note cannot exceed 500 characters.");
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string HashReceipt(
        Guid shipmentId, IEnumerable<string> qrCodes,
        IEnumerable<ConfirmShipmentReceiptLineData> lines, string? note)
    {
        var lineText = string.Join(';', lines.Select(line => string.Create(CultureInfo.InvariantCulture,
            $"{line.OrderLineId:N}:{line.BatchId:N}:{line.ReceivedQuantity}:{line.DamagedQuantity}")));
        var canonical = $"{shipmentId:N}|{string.Join(',', qrCodes)}|{lineText}|{note}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static QrLabelEvent NewLabelEvent(
        BatchQrLabel label, Guid actorUserId, string operationKey,
        string fromStatus, string orderCode, DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(), BatchQrLabelId = label.Id, WarehouseId = label.CurrentWarehouseId,
            PerformedByUserId = actorUserId, OperationKey = operationKey,
            EventType = QrLabelEventTypes.RetailerReceived, FromStatus = fromStatus,
            ToStatus = QrLabelStatuses.Delivered, Quantity = label.Quantity,
            ReferenceCode = orderCode, Note = "Nhà bán lẻ xác nhận tem theo phần giao", OccurredAt = now
        };

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
