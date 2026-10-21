using FSCM.Application.Features.Orders;
using FSCM.Application.Features.QrLabels;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Picking;

public sealed class PickingService(
    IPickingStore store,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyCollection<PickingListResult>> ListAsync(
        Guid actorUserId,
        string actorRole,
        Guid? warehouseId,
        string? status,
        CancellationToken cancellationToken)
    {
        ValidateReadRole(actorRole);
        if (!string.IsNullOrWhiteSpace(status) && !PickingStatusSet.Contains(status))
            throw PickingOperationException.Validation("Picking status filter is invalid.");
        var lists = await store.ListAsync(actorUserId, actorRole, warehouseId, status, cancellationToken);
        return lists.Select(ToResult).ToArray();
    }

    public async Task<PickingListResult> GetAsync(
        string pickingCode,
        Guid actorUserId,
        string actorRole,
        CancellationToken cancellationToken)
    {
        ValidateReadRole(actorRole);
        var view = await FindAccessibleAsync(pickingCode, actorUserId, actorRole, false, cancellationToken);
        return ToResult(view);
    }

    public async Task<PickingListResult> ScanAsync(
        string pickingCode,
        ScanPickingLabelData data,
        string operationKey,
        Guid warehouseKeeperUserId,
        string actorRole,
        CancellationToken cancellationToken)
    {
        if (actorRole != SystemRoles.WarehouseKeeper)
            throw PickingOperationException.Forbidden("Only Warehouse Keeper can scan order picking labels.");
        operationKey = NormalizeOperationKey(operationKey);
        pickingCode = NormalizeCode(pickingCode, "Picking code");
        var qrCode = NormalizeCode(data.QrCode, "QR code");
        ValidateQuantity(data.Quantity);

        var priorOperation = await store.FindOperationAsync(operationKey, cancellationToken);
        if (priorOperation is not null)
        {
            if (priorOperation.EventType != QrLabelEventTypes.Picked ||
                !string.Equals(priorOperation.ReferenceCode, pickingCode, StringComparison.Ordinal) ||
                !string.Equals(priorOperation.Note, ScanNote(qrCode), StringComparison.Ordinal) ||
                data.Quantity.HasValue && priorOperation.Quantity != data.Quantity.Value)
            {
                throw PickingOperationException.Conflict(
                    "Operation key was already used for a different picking scan.");
            }

            return await GetAsync(
                pickingCode, warehouseKeeperUserId, actorRole, cancellationToken);
        }

        await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var view = await FindAccessibleAsync(
                pickingCode, warehouseKeeperUserId, actorRole, true, transactionCancellationToken);
            if (view.List.Status is PickingListStatuses.Completed or PickingListStatuses.Cancelled)
                throw PickingOperationException.Conflict("Closed picking lists cannot accept more scans.");
            if (view.Order.Status is not (OrderStatuses.Approved or OrderStatuses.Picking))
                throw PickingOperationException.Conflict("Order must be approved or in picking before scanning.");
            if (view.List.AssignedToUserId.HasValue && view.List.AssignedToUserId != warehouseKeeperUserId)
                throw PickingOperationException.Conflict("Picking list is assigned to another Warehouse Keeper.");
            if (view.List.Version != data.ExpectedPickingVersion)
                throw PickingOperationException.Conflict(
                    $"Picking version is {view.List.Version}; refresh and retry.");

            var label = await store.FindLabelForUpdateAsync(qrCode, transactionCancellationToken)
                ?? throw PickingOperationException.NotFound("QR label was not found.");
            if (label.Version != data.ExpectedLabelVersion)
                throw PickingOperationException.Conflict(
                    $"QR label version is {label.Version}; refresh and retry.");
            if (label.Status != QrLabelStatuses.Available)
                throw PickingOperationException.Conflict("Only an available QR label can be picked.");
            if (label.CurrentWarehouseId != view.List.WarehouseId)
                throw PickingOperationException.Conflict("QR label belongs to a different warehouse.");

            var item = view.Items.SingleOrDefault(candidate =>
                candidate.Item.SourceBatchId == label.BatchId &&
                candidate.Item.PickedQuantity < candidate.Item.RequestedQuantity)
                ?? throw PickingOperationException.Conflict(
                    "QR label batch is not required by this picking list or is already complete.");
            if (item.Batch.ProductId != item.OrderLine.ProductId ||
                item.Allocation.BatchId != label.BatchId ||
                item.Allocation.WarehouseId != view.List.WarehouseId)
            {
                throw PickingOperationException.Conflict(
                    "QR label does not match the allocated SKU, batch, and warehouse.");
            }

            var remaining = item.Item.RequestedQuantity - item.Item.PickedQuantity;
            var quantity = data.Quantity ?? Math.Min(label.Quantity, remaining);
            ValidateQuantity(quantity);
            if (quantity > remaining)
                throw PickingOperationException.Conflict($"Picking item only needs {remaining:0.###} more cartons.");
            if (quantity > label.Quantity)
                throw PickingOperationException.Conflict($"QR label only contains {label.Quantity:0.###} cartons.");

            var now = timeProvider.GetUtcNow();
            BatchQrLabel pickedLabel;
            if (quantity < label.Quantity)
            {
                var originalStatus = label.Status;
                label.Quantity -= quantity;
                label.Version++;
                label.UpdatedAt = now;
                var childId = Guid.NewGuid();
                pickedLabel = new BatchQrLabel
                {
                    Id = childId,
                    BatchId = label.BatchId,
                    CurrentWarehouseId = label.CurrentWarehouseId,
                    ParentLabelId = label.Id,
                    RootLabelId = label.RootLabelId ?? label.Id,
                    QrCode = await store.NextQrCodeAsync(transactionCancellationToken),
                    Quantity = quantity,
                    Status = QrLabelStatuses.Picked,
                    Version = 1,
                    GeneratedAt = now,
                    UpdatedAt = now
                };
                store.AddLabel(pickedLabel);
                store.AddLabelEvent(NewLabelEvent(
                    pickedLabel, warehouseKeeperUserId, $"{operationKey}:split", QrLabelEventTypes.Split,
                    originalStatus, QrLabelStatuses.Available, label.QrCode,
                    $"Tách {quantity:0.###} thùng từ tem {label.QrCode} để soạn hàng", now));
            }
            else
            {
                pickedLabel = label;
                pickedLabel.Status = QrLabelStatuses.Picked;
                pickedLabel.Version++;
                pickedLabel.UpdatedAt = now;
            }

            store.AddLabelEvent(NewLabelEvent(
                pickedLabel, warehouseKeeperUserId, operationKey, QrLabelEventTypes.Picked,
                QrLabelStatuses.Available, QrLabelStatuses.Picked, view.List.PickingCode,
                ScanNote(qrCode), now));
            item.Item.PickedQuantity += quantity;
            item.Item.StockOutBatchId = item.Item.SourceBatchId;
            item.Item.ScannedAt ??= now;
            item.Item.Status = item.Item.PickedQuantity == item.Item.RequestedQuantity
                ? PickingItemStatuses.Completed
                : PickingItemStatuses.InProgress;
            if (item.Item.Status == PickingItemStatuses.Completed) item.Item.CompletedAt = now;

            view.List.AssignedToUserId ??= warehouseKeeperUserId;
            view.List.StartedAt ??= now;
            view.List.Status = view.Items.All(candidate =>
                candidate.Item.PickedQuantity == candidate.Item.RequestedQuantity)
                ? PickingListStatuses.Completed
                : PickingListStatuses.InProgress;
            if (view.List.Status == PickingListStatuses.Completed)
            {
                view.List.ConfirmedAt = now;
                view.List.ConfirmedByUserId = warehouseKeeperUserId;
            }
            view.List.Version++;
            view.List.UpdatedAt = now;

            if (view.Order.Status == OrderStatuses.Approved)
            {
                TransitionOrder(view.Order, OrderStatuses.Picking, now);
                store.AddOrderHistory(NewOrderHistory(
                    view.Order.Id, OrderStatuses.Approved, OrderStatuses.Picking,
                    warehouseKeeperUserId, "Bắt đầu soạn hàng", now));
            }

            if (view.List.Status == PickingListStatuses.Completed)
            {
                var orderLists = await store.ListByOrderAsync(view.Order.Id, transactionCancellationToken);
                if (orderLists.All(candidate => candidate.Status == PickingListStatuses.Completed))
                {
                    TransitionOrder(view.Order, OrderStatuses.Issued, now);
                    store.AddOrderHistory(NewOrderHistory(
                        view.Order.Id, OrderStatuses.Picking, OrderStatuses.Issued,
                        warehouseKeeperUserId, "Hoàn tất soạn hàng; tồn kho chỉ xuất khi dispatch", now));
                }
            }

            await store.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);

        store.ClearTracking();
        return await GetAsync(pickingCode, warehouseKeeperUserId, actorRole, cancellationToken);
    }

    private async Task<PickingView> FindAccessibleAsync(
        string pickingCode,
        Guid actorUserId,
        string actorRole,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var view = await store.FindAsync(
            NormalizeCode(pickingCode, "Picking code"), forUpdate, cancellationToken)
            ?? throw PickingOperationException.NotFound("Picking list was not found.");
        if (!await store.CanAccessWarehouseAsync(
                actorUserId, actorRole, view.List.WarehouseId, cancellationToken))
        {
            throw PickingOperationException.Forbidden("User is not assigned to this picking warehouse.");
        }
        return view;
    }

    private static PickingListResult ToResult(PickingView view) => new(
        view.List.Id,
        view.List.PickingCode,
        view.Order.Id,
        view.Order.OrderCode,
        view.Warehouse.Id,
        view.Warehouse.Code,
        view.Warehouse.Name,
        view.List.AssignedToUserId,
        view.List.Status,
        view.List.Version,
        view.List.StartedAt,
        view.List.ConfirmedAt,
        view.List.ConfirmedByUserId,
        view.List.CreatedAt,
        view.List.UpdatedAt,
        view.Items.OrderBy(item => item.OrderLine.LineNumber)
            .ThenBy(item => item.Allocation.AllocationSequence)
            .Select(item => new PickingItemResult(
                item.Item.Id,
                item.Item.AllocationId,
                item.OrderLine.ProductId,
                item.OrderLine.SkuSnapshot,
                item.OrderLine.ProductNameSnapshot,
                item.Batch.Id,
                item.Batch.BatchCode,
                item.Item.RequestedQuantity,
                item.Item.PickedQuantity,
                item.Item.RequestedQuantity - item.Item.PickedQuantity,
                item.Item.Status,
                item.Item.ScannedAt,
                item.Item.CompletedAt)).ToArray(),
        view.Scans.Select(scan => new PickingScanResult(
            scan.Event.OperationKey,
            scan.QrCode,
            scan.Event.Quantity,
            scan.Event.PerformedByUserId,
            scan.Event.OccurredAt)).ToArray());

    private static QrLabelEvent NewLabelEvent(
        BatchQrLabel label,
        Guid actorUserId,
        string operationKey,
        string eventType,
        string fromStatus,
        string toStatus,
        string? referenceCode,
        string? note,
        DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            BatchQrLabelId = label.Id,
            WarehouseId = label.CurrentWarehouseId,
            PerformedByUserId = actorUserId,
            OperationKey = operationKey,
            EventType = eventType,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Quantity = label.Quantity,
            ReferenceCode = referenceCode,
            Note = note,
            OccurredAt = now
        };

    private static OrderStatusHistory NewOrderHistory(
        Guid orderId,
        string fromStatus,
        string toStatus,
        Guid actorUserId,
        string reason,
        DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            SalesOrderId = orderId,
            ChangedByUserId = actorUserId,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Reason = reason,
            ChangedAt = now
        };

    private static void TransitionOrder(SalesOrder order, string status, DateTimeOffset now)
    {
        order.Status = status;
        order.Version++;
        order.UpdatedAt = now;
    }

    private static void ValidateReadRole(string role)
    {
        if (role is not (SystemRoles.Admin or SystemRoles.Operator or SystemRoles.WarehouseKeeper))
            throw PickingOperationException.Forbidden("Role cannot read picking lists.");
    }

    private static void ValidateQuantity(decimal? quantity)
    {
        if (!quantity.HasValue) return;
        if (quantity <= 0 || decimal.Truncate(quantity.Value) != quantity.Value)
            throw PickingOperationException.Validation("Picking quantity must be a positive whole number of cartons.");
    }

    private static string NormalizeCode(string value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw PickingOperationException.Validation($"{field} is required.")
            : value.Trim().ToUpperInvariant();

    private static string NormalizeOperationKey(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        if (value.Length is < 1 or > 90)
            throw PickingOperationException.Validation(
                "Idempotency-Key is required and must not exceed 90 characters.");
        return value;
    }

    private static string ScanNote(string qrCode) => $"Quét tem {qrCode} để soạn đơn";

    private static readonly HashSet<string> PickingStatusSet =
    [
        PickingListStatuses.Open,
        PickingListStatuses.InProgress,
        PickingListStatuses.Completed,
        PickingListStatuses.Cancelled
    ];
}
