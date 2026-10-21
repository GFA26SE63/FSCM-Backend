using System.Security.Cryptography;
using System.Text;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Payments;
using FSCM.Application.Features.QrLabels;
using FSCM.Application.Features.Inventory;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Deliveries;

public sealed class DeliveryService(
    IDeliveryStore store,
    IPaymentPlanService paymentPlanService,
    IPaymentStore paymentStore,
    IInventoryLifecycleService inventoryLifecycleService,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyCollection<DriverResult>> ListDriversAsync(
        string actorRole, bool includeInactive, CancellationToken cancellationToken)
    {
        EnsureOperator(actorRole);
        return (await store.ListDriversAsync(includeInactive, cancellationToken))
            .Select(ToResult).ToArray();
    }

    public async Task<IReadOnlyCollection<VehicleResult>> ListVehiclesAsync(
        string actorRole, bool includeInactive, CancellationToken cancellationToken)
    {
        EnsureOperator(actorRole);
        return (await store.ListVehiclesAsync(includeInactive, cancellationToken))
            .Select(ToResult).ToArray();
    }

    public async Task<IReadOnlyCollection<DeliveryResult>> ListAsync(
        Guid actorUserId, string actorRole, string? status, CancellationToken cancellationToken)
    {
        ValidateReadRole(actorRole);
        if (!string.IsNullOrWhiteSpace(status) &&
            status is not (DeliveryStatuses.Delivering or DeliveryStatuses.Delivered))
            throw DeliveryOperationException.Validation("Delivery status filter is invalid.");
        return (await store.ListAsync(actorUserId, actorRole, status, cancellationToken))
            .Select(ToResult).ToArray();
    }

    public async Task<DeliveryResult> GetByOrderAsync(
        Guid orderId, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        ValidateReadRole(actorRole);
        var view = await store.FindByOrderAsync(orderId, false, cancellationToken)
            ?? throw DeliveryOperationException.NotFound("Delivery was not found.");
        if (!await store.CanViewAsync(view, actorUserId, actorRole, cancellationToken))
            throw DeliveryOperationException.Forbidden("You cannot view this delivery.");
        return ToResult(view);
    }

    public async Task<DeliveryResult> DispatchAsync(
        Guid orderId,
        DispatchOrderData data,
        string operationKey,
        Guid operatorUserId,
        string actorRole,
        CancellationToken cancellationToken)
    {
        EnsureOperator(actorRole);
        operationKey = NormalizeOperationKey(operationKey);
        ValidateNote(data.Note);
        if (data.DriverId == Guid.Empty || data.VehicleId == Guid.Empty)
            throw DeliveryOperationException.Validation("Driver and vehicle are required.");

        var prior = await store.FindByOperationKeyAsync(operationKey, cancellationToken);
        if (prior is not null)
        {
            if (prior.Order.Id != orderId || prior.Delivery.DriverId != data.DriverId ||
                prior.Delivery.VehicleId != data.VehicleId ||
                !string.Equals(prior.Delivery.Note, NormalizeNote(data.Note), StringComparison.Ordinal))
                throw DeliveryOperationException.Conflict(
                    "Operation key was already used for a different dispatch request.");
            return ToResult(prior);
        }

        await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var order = await store.FindOrderAsync(orderId, true, transactionCancellationToken)
                ?? throw DeliveryOperationException.NotFound("Order was not found.");
            if (order.Order.Status != OrderStatuses.Issued)
                throw DeliveryOperationException.Conflict("Only an issued order can be dispatched.");
            EnsureVersion(order.Order.Version, data.ExpectedOrderVersion, "Order");
            if (order.Order.PaymentMethod == OrderPaymentMethods.Prepaid)
            {
                var payment = await paymentStore.FindByOrderAsync(
                        orderId, true, transactionCancellationToken)
                    ?? throw DeliveryOperationException.Conflict(
                        "Prepaid order does not have a final-approved payment ledger.");
                if (payment.Payment.Status != PaymentStatuses.Paid ||
                    payment.Payment.ConfirmedAmount != payment.Payment.Amount)
                {
                    throw DeliveryOperationException.Conflict(
                        "Prepaid order must be confirmed as fully paid before dispatch.");
                }
            }
            if (await store.FindByOrderAsync(orderId, true, transactionCancellationToken) is not null)
                throw DeliveryOperationException.Conflict("Order already has a delivery.");

            var driver = await store.FindDriverAsync(data.DriverId, transactionCancellationToken)
                ?? throw DeliveryOperationException.NotFound("Driver was not found.");
            var vehicle = await store.FindVehicleAsync(data.VehicleId, transactionCancellationToken)
                ?? throw DeliveryOperationException.NotFound("Vehicle was not found.");
            if (!driver.IsActive || !vehicle.IsActive)
                throw DeliveryOperationException.Conflict("Driver and vehicle must both be active.");
            if (driver.WarehouseId != vehicle.WarehouseId)
                throw DeliveryOperationException.Conflict("Driver and vehicle must belong to the same warehouse.");

            var labels = await store.ListOrderLabelsAsync(orderId, true, transactionCancellationToken);
            if (labels.Count == 0 || labels.Any(item => item.Label.Status != QrLabelStatuses.Picked))
                throw DeliveryOperationException.Conflict(
                    "Every picked QR label must be ready before dispatch.");
            if (!labels.Any(item => item.Label.CurrentWarehouseId == vehicle.WarehouseId))
                throw DeliveryOperationException.Conflict(
                    "Driver and vehicle warehouse must participate in the order allocation.");
            var totalQuantity = labels.Sum(item => item.Label.Quantity);
            if (vehicle.Capacity < totalQuantity)
                throw DeliveryOperationException.Conflict(
                    $"Vehicle capacity is {vehicle.Capacity:0.###} cartons but the order requires {totalQuantity:0.###}.");

            var inventoryIssues = await store.ListOrderInventoryIssuesAsync(orderId, transactionCancellationToken);
            if (inventoryIssues.Count == 0)
                throw DeliveryOperationException.Conflict("Order has no reserved allocations to issue.");
            foreach (var batchGroup in inventoryIssues.GroupBy(item => item.BatchId))
            {
                var expectedVersion = batchGroup.First().InventoryVersion;
                foreach (var issue in batchGroup)
                {
                    var issued = await inventoryLifecycleService.IssueAsync(
                        issue.AllocationId, operatorUserId, expectedVersion, transactionCancellationToken);
                    expectedVersion = issued.Version;
                }
            }

            var now = timeProvider.GetUtcNow();
            var delivery = new Delivery
            {
                Id = Guid.NewGuid(),
                SalesOrderId = orderId,
                DriverId = driver.Id,
                VehicleId = vehicle.Id,
                OperationKey = operationKey,
                Status = DeliveryStatuses.Delivering,
                DriverCodeSnapshot = driver.Code,
                DriverNameSnapshot = driver.FullName,
                DriverPhoneSnapshot = driver.Phone,
                DriverLicenseSnapshot = driver.LicenseNumber,
                PlateNumberSnapshot = vehicle.PlateNumber,
                VehicleTypeSnapshot = vehicle.VehicleType,
                Version = 0,
                AssignedAt = now,
                DispatchedAt = now,
                Note = NormalizeNote(data.Note),
                CreatedAt = now,
                UpdatedAt = now
            };
            store.AddDelivery(delivery);
            for (var index = 0; index < labels.Count; index++)
            {
                var label = labels.ElementAt(index).Label;
                var fromStatus = label.Status;
                label.Status = QrLabelStatuses.InTransit;
                label.Version++;
                label.UpdatedAt = now;
                store.AddLabelEvent(NewLabelEvent(label, operatorUserId,
                    $"dispatch:{operationKey}:{index + 1}", QrLabelEventTypes.Dispatched,
                    fromStatus, label.Status, order.Order.OrderCode,
                    "Bàn giao tem cho đơn vị vận chuyển", now));
            }

            TransitionOrder(order.Order, OrderStatuses.Delivering, now);
            store.AddOrderHistory(NewHistory(orderId, OrderStatuses.Issued, OrderStatuses.Delivering,
                operatorUserId, $"Giao cho {driver.FullName} - xe {vehicle.PlateNumber}", now));
            await store.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);

        store.ClearTracking();
        return ToResult(await store.FindByOrderAsync(orderId, false, cancellationToken)
            ?? throw DeliveryOperationException.Conflict("Dispatch was committed but could not be reloaded."));
    }

    public async Task<DeliveryResult> ConfirmReceiptAsync(
        Guid orderId,
        ConfirmRetailerReceiptData data,
        string operationKey,
        Guid retailerUserId,
        string actorRole,
        CancellationToken cancellationToken)
    {
        if (actorRole != SystemRoles.Retailer)
            throw DeliveryOperationException.Forbidden("Only the order Retailer can confirm receipt.");
        operationKey = NormalizeOperationKey(operationKey);
        ValidateNote(data.Note);
        var qrCodes = NormalizeQrCodes(data.QrCodes);
        var requestHash = HashReceipt(orderId, qrCodes, NormalizeNote(data.Note));

        var prior = await store.FindByReceiptOperationKeyAsync(operationKey, cancellationToken);
        if (prior is not null)
        {
            if (prior.Order.Id != orderId || prior.Receipt?.RequestHash != requestHash)
                throw DeliveryOperationException.Conflict(
                    "Operation key was already used for a different receipt request.");
            if (!await store.CanViewAsync(prior, retailerUserId, actorRole, cancellationToken))
                throw DeliveryOperationException.Forbidden("You cannot confirm receipt for this order.");
            return ToResult(prior);
        }

        await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var order = await store.FindOrderAsync(orderId, true, transactionCancellationToken)
                ?? throw DeliveryOperationException.NotFound("Order was not found.");
            if (order.Retailer.UserAccountId != retailerUserId)
                throw DeliveryOperationException.Forbidden("Only the order Retailer can confirm receipt.");
            if (order.Order.Status != OrderStatuses.Delivering)
                throw DeliveryOperationException.Conflict("Only a delivering order can be received.");
            EnsureVersion(order.Order.Version, data.ExpectedOrderVersion, "Order");

            var deliveryView = await store.FindByOrderAsync(orderId, true, transactionCancellationToken)
                ?? throw DeliveryOperationException.Conflict("Order delivery was not found.");
            if (deliveryView.Receipt is not null)
                throw DeliveryOperationException.Conflict("Order receipt was already confirmed.");
            var labels = await store.ListOrderLabelsAsync(orderId, true, transactionCancellationToken);
            var requiredCodes = labels.Select(item => item.QrCode).Order(StringComparer.Ordinal).ToArray();
            if (!requiredCodes.SequenceEqual(qrCodes, StringComparer.Ordinal))
                throw DeliveryOperationException.Conflict(
                    "Receipt must scan every dispatched QR label exactly once.");
            if (labels.Any(item => item.Label.Status != QrLabelStatuses.InTransit))
                throw DeliveryOperationException.Conflict("Every QR label must be in transit before receipt.");

            var now = timeProvider.GetUtcNow();
            var receipt = new ReceiptConfirmation
            {
                Id = Guid.NewGuid(),
                SalesOrderId = orderId,
                ConfirmedByRetailerUserId = retailerUserId,
                OperationKey = operationKey,
                RequestHash = requestHash,
                Status = ReceiptConfirmationStatuses.Confirmed,
                ConfirmedAt = now,
                Note = NormalizeNote(data.Note),
                CreatedAt = now
            };
            store.AddReceipt(receipt);
            for (var index = 0; index < labels.Count; index++)
            {
                var labelView = labels.ElementAt(index);
                var label = labelView.Label;
                var fromStatus = label.Status;
                label.Status = QrLabelStatuses.Delivered;
                label.Version++;
                label.UpdatedAt = now;
                store.AddReceiptItem(new ReceiptConfirmationItem
                {
                    Id = Guid.NewGuid(),
                    ReceiptConfirmationId = receipt.Id,
                    BatchId = labelView.BatchId,
                    BatchQrLabelId = label.Id,
                    ExpectedQuantity = label.Quantity,
                    ReceivedQuantity = label.Quantity,
                    ScannedAt = now
                });
                store.AddLabelEvent(NewLabelEvent(label, retailerUserId,
                    $"receipt:{operationKey}:{index + 1}", QrLabelEventTypes.RetailerReceived,
                    fromStatus, label.Status, order.Order.OrderCode,
                    "Nhà bán lẻ xác nhận đã nhận tem", now));
            }

            deliveryView.Delivery.Status = DeliveryStatuses.Delivered;
            deliveryView.Delivery.DeliveredAt = now;
            deliveryView.Delivery.Version++;
            deliveryView.Delivery.UpdatedAt = now;
            TransitionOrder(order.Order, OrderStatuses.Delivered, now);
            await paymentPlanService.CreateForDeliveredOrderAsync(
                order.Order, retailerUserId, now, transactionCancellationToken);
            store.AddOrderHistory(NewHistory(orderId, OrderStatuses.Delivering, OrderStatuses.Delivered,
                retailerUserId, "Nhà bán lẻ quét đủ tem và xác nhận nhận hàng", now));
            await store.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);

        store.ClearTracking();
        return ToResult(await store.FindByOrderAsync(orderId, false, cancellationToken)
            ?? throw DeliveryOperationException.Conflict("Receipt was committed but could not be reloaded."));
    }

    private static DriverResult ToResult(Driver item) =>
        new(item.Id, item.WarehouseId, item.Code, item.FullName, item.Phone, item.LicenseNumber, item.IsActive);

    private static VehicleResult ToResult(Vehicle item) =>
        new(item.Id, item.WarehouseId, item.PlateNumber, item.VehicleType, item.Capacity, item.IsActive);

    private static DeliveryResult ToResult(DeliveryView view) => new(
        view.Delivery.Id, view.Order.Id, view.Order.OrderCode, view.Retailer.Id,
        view.Order.RetailerCodeSnapshot, view.Order.RetailerNameSnapshot,
        view.Order.Status, view.Order.Version, view.Delivery.DriverId,
        view.Delivery.DriverCodeSnapshot, view.Delivery.DriverNameSnapshot, view.Delivery.DriverPhoneSnapshot,
        view.Delivery.DriverLicenseSnapshot, view.Delivery.VehicleId,
        view.Delivery.PlateNumberSnapshot, view.Delivery.VehicleTypeSnapshot,
        view.Delivery.Status, view.Delivery.Version, view.Delivery.AssignedAt,
        view.Delivery.DispatchedAt, view.Delivery.DeliveredAt, view.Delivery.Note,
        view.Receipt is null ? null : new ReceiptConfirmationResult(
            view.Receipt.Id, view.Receipt.ConfirmedByRetailerUserId, view.Receipt.Status,
            view.Receipt.ConfirmedAt, view.Receipt.Note,
            view.ReceiptItems.Select(item => new ReceiptItemResult(
                item.Item.Id, item.Item.BatchId, item.Item.BatchQrLabelId, item.QrCode,
                item.Item.ExpectedQuantity, item.Item.ReceivedQuantity, item.Item.ScannedAt)).ToArray()));

    private static void EnsureOperator(string actorRole)
    {
        if (actorRole is not (SystemRoles.Admin or SystemRoles.Operator))
            throw DeliveryOperationException.Forbidden("Only Admin or Operator can dispatch orders or view delivery master data.");
    }

    private static void ValidateReadRole(string role)
    {
        if (role is not (SystemRoles.Admin or SystemRoles.Operator or SystemRoles.SalesManager or
            SystemRoles.Sales or SystemRoles.WarehouseKeeper or SystemRoles.Retailer))
            throw DeliveryOperationException.Forbidden("Role cannot view deliveries.");
    }

    private static string NormalizeOperationKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw DeliveryOperationException.Validation("Idempotency-Key header is required.");
        value = value.Trim();
        if (value.Length > 60)
            throw DeliveryOperationException.Validation("Idempotency-Key cannot exceed 60 characters.");
        return value;
    }

    private static string[] NormalizeQrCodes(IReadOnlyCollection<string> values)
    {
        if (values.Count == 0)
            throw DeliveryOperationException.Validation("At least one QR code is required.");
        var result = values.Select(value => string.IsNullOrWhiteSpace(value)
                ? throw DeliveryOperationException.Validation("QR code cannot be empty.")
                : value.Trim().ToUpperInvariant())
            .Order(StringComparer.Ordinal).ToArray();
        if (result.Distinct(StringComparer.Ordinal).Count() != result.Length)
            throw DeliveryOperationException.Validation("QR codes cannot be repeated.");
        return result;
    }

    private static string HashReceipt(Guid orderId, IEnumerable<string> qrCodes, string? note)
    {
        var canonical = $"{orderId:N}|{string.Join(',', qrCodes)}|{note}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static void EnsureVersion(int current, int expected, string entity)
    {
        if (current != expected)
            throw DeliveryOperationException.Conflict(
                $"{entity} version is {current}; refresh and retry.");
    }

    private static void ValidateNote(string? note)
    {
        if (note?.Trim().Length > 500)
            throw DeliveryOperationException.Validation("Note cannot exceed 500 characters.");
    }

    private static string? NormalizeNote(string? note) =>
        string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    private static void TransitionOrder(SalesOrder order, string status, DateTimeOffset now)
    {
        order.Status = status;
        order.Version++;
        order.UpdatedAt = now;
    }

    private static OrderStatusHistory NewHistory(
        Guid orderId, string from, string to, Guid actorUserId, string reason, DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            SalesOrderId = orderId,
            FromStatus = from,
            ToStatus = to,
            ChangedByUserId = actorUserId,
            Reason = reason,
            ChangedAt = now
        };

    private static QrLabelEvent NewLabelEvent(
        BatchQrLabel label,
        Guid actorUserId,
        string operationKey,
        string eventType,
        string from,
        string to,
        string reference,
        string note,
        DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            BatchQrLabelId = label.Id,
            WarehouseId = label.CurrentWarehouseId,
            PerformedByUserId = actorUserId,
            OperationKey = operationKey,
            EventType = eventType,
            FromStatus = from,
            ToStatus = to,
            Quantity = label.Quantity,
            ReferenceCode = reference,
            Note = note,
            OccurredAt = now
        };
}
