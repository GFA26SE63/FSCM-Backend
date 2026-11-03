using FSCM.Application.Features.Orders;
using FSCM.Application.Features.QrLabels;
using FSCM.Application.Features.Shipments;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Picking;

public sealed record CurrentPickingItemResult(
    Guid Id, Guid AllocationId, string Sku, string ProductName, Guid BatchId, string BatchCode,
    decimal RequestedQuantity, decimal PickedQuantity, decimal RemainingQuantity, string Status);

public sealed record CurrentPickingResult(
    Guid Id, string PickingCode, Guid OrderId, string OrderCode, Guid WarehouseId,
    string WarehouseCode, Guid? AssignedToUserId, string Status, int Version,
    DateTimeOffset? StartedAt, DateTimeOffset? ConfirmedAt,
    IReadOnlyCollection<CurrentPickingItemResult> Items,
    IReadOnlyCollection<PickingScanResult> Scans);

/// <summary>Picking workflow backed by the consolidated PICKING_LIST/PICKING_ITEM tables.</summary>
public sealed class CurrentPickingService(IPickingStore store, TimeProvider timeProvider)
{
    private static readonly HashSet<string> Statuses =
        [PickingListStatuses.Pending, PickingListStatuses.InProgress,
            PickingListStatuses.Completed, PickingListStatuses.Exception];

    public async Task<IReadOnlyCollection<CurrentPickingResult>> ListAsync(
        Guid actorId, string role, Guid? warehouseId, string? status, CancellationToken ct)
    {
        ValidateReadRole(role);
        if (!string.IsNullOrWhiteSpace(status) && !Statuses.Contains(status))
            throw PickingOperationException.Validation("Picking status filter is invalid.");
        return (await store.ListAsync(actorId, role, warehouseId, status, ct)).Select(ToResult).ToArray();
    }

    public async Task<CurrentPickingResult> GetAsync(
        string code, Guid actorId, string role, CancellationToken ct) =>
        ToResult(await FindAccessibleAsync(code, actorId, role, false, ct));

    public async Task<CurrentPickingResult> ScanAsync(
        string code, ScanPickingLabelData data, string operationKey,
        Guid actorId, string role, CancellationToken ct)
    {
        EnsureWarehouseKeeper(role);
        code = Required(code, "Picking code");
        operationKey = Required(operationKey, "Idempotency-Key");
        var qrCode = Required(data.QrCode, "QR code");
        var prior = await store.FindOperationAsync(operationKey, ct);
        if (prior is not null) return await GetAsync(code, actorId, role, ct);

        await store.ExecuteInTransactionAsync(async token =>
        {
            var view = await FindAccessibleAsync(code, actorId, role, true, token);
            if (view.List.Status is PickingListStatuses.Completed or PickingListStatuses.Exception)
                throw PickingOperationException.Conflict("Closed picking list cannot accept scans.");
            if (view.List.AssignedToUserId.HasValue && view.List.AssignedToUserId != actorId)
                throw PickingOperationException.Conflict("Picking list is assigned to another Warehouse Keeper.");
            if (view.List.Version != data.ExpectedPickingVersion)
                throw PickingOperationException.Conflict(
                    $"Picking version is {view.List.Version}; refresh and retry.");

            var label = await store.FindLabelForUpdateAsync(qrCode, token)
                ?? throw PickingOperationException.NotFound("QR label was not found.");
            if (label.Version != data.ExpectedLabelVersion)
                throw PickingOperationException.Conflict(
                    $"QR label version is {label.Version}; refresh and retry.");
            if (label.Status != QrLabelStatuses.Available || label.CurrentWarehouseId != view.List.WarehouseId)
                throw PickingOperationException.Conflict(
                    "QR label must be available in the picking warehouse.");

            var requested = data.Splits?.ToDictionary(item => item.AllocationRunAllocationId,
                item => item.Quantity) ?? [];
            var candidates = view.Items.Where(item => item.Item.SourceBatchId == label.BatchId &&
                item.Item.PickedQuantity < item.Item.RequestedQuantity).ToArray();
            if (candidates.Length == 0)
                throw PickingOperationException.Conflict("QR label batch is not required by this picking list.");
            var targets = requested.Count == 0
                ? candidates
                : candidates.Where(item => requested.ContainsKey(item.Allocation.Id)).ToArray();
            if (requested.Count > 0 && targets.Length != requested.Count)
                throw PickingOperationException.Conflict("Allocation is not part of this picking list and batch.");

            var quantity = data.Quantity ?? (requested.Count == 0
                ? targets.Sum(item => item.Item.RequestedQuantity - item.Item.PickedQuantity)
                : requested.Values.Sum());
            if (quantity <= 0 || quantity > label.Quantity)
                throw PickingOperationException.Conflict("Scanned quantity is invalid for the QR label.");
            var remaining = quantity;
            foreach (var target in targets)
            {
                var needed = target.Item.RequestedQuantity - target.Item.PickedQuantity;
                var take = requested.Count == 0 ? Math.Min(needed, remaining) : requested[target.Allocation.Id];
                if (take <= 0 || take > needed)
                    throw PickingOperationException.Conflict("Scanned quantity exceeds the allocation remainder.");
                target.Item.PickedQuantity += take;
                target.Item.ScannedAt = timeProvider.GetUtcNow();
                target.Item.Status = target.Item.PickedQuantity == target.Item.RequestedQuantity
                    ? PickingItemStatuses.Completed : PickingItemStatuses.InProgress;
                if (target.Item.Status == PickingItemStatuses.Completed)
                    target.Item.CompletedAt = target.Item.ScannedAt;
                remaining -= take;
            }
            if (remaining != 0)
                throw PickingOperationException.Conflict("Scanned quantity was not fully assigned to picking items.");

            var now = timeProvider.GetUtcNow();
            BatchQrLabel pickedLabel;
            if (quantity < label.Quantity)
            {
                label.Quantity -= quantity;
                label.Version++;
                label.UpdatedAt = now;
                pickedLabel = new BatchQrLabel
                {
                    Id = Guid.NewGuid(), BatchId = label.BatchId,
                    CurrentWarehouseId = label.CurrentWarehouseId,
                    GoodsReceiptId = label.GoodsReceiptId,
                    ParentLabelId = label.Id, RootLabelId = label.RootLabelId ?? label.Id,
                    QrCode = await store.NextQrCodeAsync(token), Quantity = quantity,
                    Status = QrLabelStatuses.Picked, Version = 1,
                    GeneratedAt = now, UpdatedAt = now
                };
                store.AddLabel(pickedLabel);
                store.AddLabelEvent(new QrLabelEvent
                {
                    Id = Guid.NewGuid(), BatchQrLabelId = pickedLabel.Id,
                    WarehouseId = view.List.WarehouseId, PerformedByUserId = actorId,
                    OperationKey = $"{operationKey}:split", EventType = QrLabelEventTypes.Split,
                    FromStatus = QrLabelStatuses.Available, ToStatus = QrLabelStatuses.Available,
                    Quantity = quantity, ReferenceCode = label.QrCode,
                    Note = $"Split {quantity:0.###} cartons for {view.List.PickingCode}",
                    OccurredAt = now
                });
            }
            else
            {
                pickedLabel = label;
                label.Status = QrLabelStatuses.Picked;
                label.Version++;
                label.UpdatedAt = now;
            }
            view.List.AssignedToUserId ??= actorId;
            view.List.StartedAt ??= now;
            view.List.Status = PickingListStatuses.InProgress;
            view.List.Version++;
            view.List.UpdatedAt = now;
            if (view.Order.Status == OrderStatuses.ConvertedToPo)
            {
                view.Order.Status = OrderStatuses.Picking;
                view.Order.Version++;
                view.Order.UpdatedAt = now;
                store.AddOrderHistory(new OrderStatusHistory
                {
                    Id = Guid.NewGuid(), SalesOrderId = view.Order.Id,
                    FromStatus = OrderStatuses.ConvertedToPo, ToStatus = OrderStatuses.Picking,
                    ChangedByUserId = actorId, Reason = "Warehouse started picking", ChangedAt = now
                });
            }
            store.AddLabelEvent(new QrLabelEvent
            {
                Id = Guid.NewGuid(), BatchQrLabelId = pickedLabel.Id, WarehouseId = view.List.WarehouseId,
                PerformedByUserId = actorId, OperationKey = operationKey,
                EventType = QrLabelEventTypes.Picked, FromStatus = QrLabelStatuses.Available,
                ToStatus = QrLabelStatuses.Picked, Quantity = quantity,
                ReferenceCode = view.List.PickingCode, Note = "Picked for consolidated picking list",
                OccurredAt = now
            });
            await store.SaveChangesAsync(token);
        }, ct);
        store.ClearTracking();
        return await GetAsync(code, actorId, role, ct);
    }

    public async Task<CurrentPickingResult> CompleteAsync(
        string code, CompleteRunPickingData data, Guid actorId, string role, CancellationToken ct)
    {
        EnsureWarehouseKeeper(role);
        code = Required(code, "Picking code");
        await store.ExecuteInTransactionAsync(async token =>
        {
            var view = await FindAccessibleAsync(code, actorId, role, true, token);
            if (view.List.Status == PickingListStatuses.Completed) return;
            if (view.List.Version != data.ExpectedPickingVersion)
                throw PickingOperationException.Conflict(
                    $"Picking version is {view.List.Version}; refresh and retry.");
            var now = timeProvider.GetUtcNow();
            if (!string.IsNullOrWhiteSpace(data.ExceptionType))
            {
                view.List.Status = PickingListStatuses.Exception;
            }
            else
            {
                if (view.Items.Any(item => item.Item.PickedQuantity != item.Item.RequestedQuantity))
                    throw PickingOperationException.Conflict("Every picking item must be completed.");
                view.List.Status = PickingListStatuses.Completed;
                if (!await store.ShipmentExistsAsync(view.Order.Id, view.List.WarehouseId, token))
                {
                    store.AddShipment(new Shipment
                    {
                        Id = Guid.NewGuid(), SalesOrderId = view.Order.Id,
                        WarehouseId = view.List.WarehouseId, PickingListId = view.List.Id,
                        ShipmentCode = await store.NextShipmentCodeAsync(token),
                        Status = ShipmentStatuses.Ready,
                        TotalQuantity = view.Items.Sum(item => item.Item.PickedQuantity),
                        ReceivedQuantity = 0, ReadyAt = now, Version = 1,
                        CreatedAt = now, UpdatedAt = now
                    });
                }
            }
            view.List.ConfirmedAt = now;
            view.List.ConfirmedByUserId = actorId;
            view.List.Version++;
            view.List.UpdatedAt = now;
            await store.SaveChangesAsync(token);
        }, ct);
        store.ClearTracking();
        return await GetAsync(code, actorId, role, ct);
    }

    private async Task<PickingView> FindAccessibleAsync(
        string code, Guid actorId, string role, bool update, CancellationToken ct)
    {
        ValidateReadRole(role);
        var view = await store.FindAsync(Required(code, "Picking code"), update, ct)
            ?? throw PickingOperationException.NotFound("Picking list was not found.");
        if (!await store.CanAccessWarehouseAsync(actorId, role, view.List.WarehouseId, ct))
            throw PickingOperationException.Forbidden("User is not assigned to this warehouse.");
        return view;
    }

    private static CurrentPickingResult ToResult(PickingView view) => new(
        view.List.Id, view.List.PickingCode, view.Order.Id, view.Order.OrderCode,
        view.Warehouse.Id, view.Warehouse.Code, view.List.AssignedToUserId, view.List.Status,
        view.List.Version, view.List.StartedAt, view.List.ConfirmedAt,
        view.Items.Select(item => new CurrentPickingItemResult(
            item.Item.Id, item.Allocation.Id, item.OrderLine.SkuSnapshot,
            item.OrderLine.ProductNameSnapshot, item.Batch.Id, item.Batch.BatchCode,
            item.Item.RequestedQuantity, item.Item.PickedQuantity,
            item.Item.RequestedQuantity - item.Item.PickedQuantity, item.Item.Status)).ToArray(),
        view.Scans.Select(item => new PickingScanResult(
            item.Event.OperationKey, item.QrCode, item.Event.Quantity,
            item.Event.PerformedByUserId, item.Event.OccurredAt)).ToArray());

    private static string Required(string? value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw PickingOperationException.Validation($"{name} is required.")
            : value.Trim();
    private static void ValidateReadRole(string role)
    {
        if (role is not (SystemRoles.Admin or SystemRoles.Operator or SystemRoles.WarehouseKeeper))
            throw PickingOperationException.Forbidden("Role cannot read picking lists.");
    }
    private static void EnsureWarehouseKeeper(string role)
    {
        if (role != SystemRoles.WarehouseKeeper)
            throw PickingOperationException.Forbidden("Only Warehouse Keeper can change picking work.");
    }
}
