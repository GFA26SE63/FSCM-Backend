using FSCM.Application.Features.AllocationRuns;
using FSCM.Application.Features.QrLabels;
using FSCM.Application.Features.Shipments;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Picking;

public sealed class PickingService(IRunPickingStore store, TimeProvider timeProvider)
{
    private static readonly HashSet<string> Statuses =
    [RunPickingListStatuses.Issued, RunPickingListStatuses.Picking,
        RunPickingListStatuses.Ready, RunPickingListStatuses.NeedsHandling];
    private static readonly HashSet<string> ExceptionTypes =
    ["Shortage", "Damaged", "Expired", "QrMismatch"];

    public async Task<IReadOnlyCollection<PickingListResult>> ListAsync(Guid actorId, string role,
        Guid? warehouseId, string? status, CancellationToken ct)
    {
        ValidateReadRole(role);
        if (!string.IsNullOrWhiteSpace(status) && !Statuses.Contains(status))
            throw PickingOperationException.Validation("Picking status filter is invalid.");
        return (await store.ListAsync(actorId, role, warehouseId, status, ct)).Select(ToResult).ToArray();
    }

    public async Task<PickingListResult> GetAsync(string code, Guid actorId, string role, CancellationToken ct)
    {
        ValidateReadRole(role);
        return ToResult(await FindAccessibleAsync(code, actorId, role, false, ct));
    }

    public async Task<PickingListResult> ScanAsync(string code, ScanPickingLabelData data,
        string operationKey, Guid actorId, string role, CancellationToken ct)
    {
        EnsureWarehouseKeeper(role);
        code = NormalizeCode(code, "Picking code");
        operationKey = NormalizeOperationKey(operationKey);
        var qrCode = NormalizeCode(data.QrCode, "QR code");
        ValidateQuantity(data.Quantity);
        if (data.Splits is null || data.Splits.Count == 0)
            throw PickingOperationException.Validation("At least one allocation split is required.");
        if (data.Splits.GroupBy(x => x.AllocationRunAllocationId).Any(x => x.Count() > 1))
            throw PickingOperationException.Validation("An allocation can appear only once in a scan.");
        foreach (var split in data.Splits) ValidateQuantity(split.Quantity);
        var requestQuantity = data.Quantity ?? data.Splits.Sum(x => x.Quantity);
        if (data.Splits.Sum(x => x.Quantity) != requestQuantity)
            throw PickingOperationException.Validation("Split quantities must equal the scanned quantity.");

        var prior = await store.FindOperationAsync(operationKey, ct);
        if (prior is not null)
        {
            if (prior.EventType != QrLabelEventTypes.Picked || prior.ReferenceCode != code ||
                prior.Note != ScanNote(qrCode) || prior.Quantity != requestQuantity)
                throw PickingOperationException.Conflict("Operation key was already used for another picking command.");
            return await GetAsync(code, actorId, role, ct);
        }

        await store.ExecuteInTransactionAsync(async txCt =>
        {
            var view = await FindAccessibleAsync(code, actorId, role, true, txCt);
            if (view.List.Status is RunPickingListStatuses.Ready or RunPickingListStatuses.NeedsHandling)
                throw PickingOperationException.Conflict("Closed warehouse picking portions cannot accept scans.");
            if (view.List.AssignedToUserId.HasValue && view.List.AssignedToUserId != actorId)
                throw PickingOperationException.Conflict("Picking list is assigned to another Warehouse Keeper.");
            if (view.List.Version != data.ExpectedPickingVersion)
                throw PickingOperationException.Conflict($"Picking version is {view.List.Version}; refresh and retry.");

            var label = await store.FindLabelForUpdateAsync(qrCode, txCt)
                ?? throw PickingOperationException.NotFound("QR label was not found.");
            if (label.Version != data.ExpectedLabelVersion)
                throw PickingOperationException.Conflict($"QR label version is {label.Version}; refresh and retry.");
            if (label.Status != QrLabelStatuses.Available)
                throw PickingOperationException.Conflict("Only an available QR label can be picked.");
            if (label.CurrentWarehouseId != view.List.WarehouseId)
                throw PickingOperationException.Conflict("QR label belongs to another warehouse.");

            var gather = view.Gathers.SingleOrDefault(x => x.BatchId == label.BatchId)
                ?? throw PickingOperationException.Conflict("QR label batch is not approved on this picking sheet.");
            var requested = data.Splits.ToDictionary(x => x.AllocationRunAllocationId);
            var targets = view.Splits.Where(x => x.RunPickingGatherId == gather.Id &&
                                                 requested.ContainsKey(x.AllocationRunAllocationId)).ToArray();
            if (targets.Length != requested.Count)
                throw PickingOperationException.Conflict("An allocation split is not part of the approved batch gather.");
            var quantity = requestQuantity;
            if (quantity > label.Quantity)
                throw PickingOperationException.Conflict($"QR label contains only {label.Quantity:0.###} cartons.");
            if (quantity > gather.RequiredQuantity - gather.PickedQuantity)
                throw PickingOperationException.Conflict("Scanned quantity exceeds the remaining approved gather quantity.");
            foreach (var split in targets)
                if (requested[split.AllocationRunAllocationId].Quantity > split.Quantity - split.PickedQuantity)
                    throw PickingOperationException.Conflict(
                        $"Allocation {split.AllocationRunAllocationId} does not need that many cartons.");

            var now = timeProvider.GetUtcNow();
            BatchQrLabel pickedLabel;
            if (quantity < label.Quantity)
            {
                label.Quantity -= quantity; label.Version++; label.UpdatedAt = now;
                pickedLabel = new BatchQrLabel
                {
                    Id = Guid.NewGuid(), BatchId = label.BatchId, CurrentWarehouseId = label.CurrentWarehouseId,
                    ParentLabelId = label.Id, RootLabelId = label.RootLabelId ?? label.Id,
                    QrCode = await store.NextQrCodeAsync(txCt), Quantity = quantity,
                    Status = QrLabelStatuses.Picked, Version = 1, GeneratedAt = now, UpdatedAt = now
                };
                store.AddLabel(pickedLabel);
                store.AddLabelEvent(NewEvent(pickedLabel, actorId, $"{operationKey}:split", QrLabelEventTypes.Split,
                    QrLabelStatuses.Available, QrLabelStatuses.Available, label.QrCode,
                    $"Tách {quantity:0.###} thùng để soạn phiếu {code}", now));
            }
            else
            {
                pickedLabel = label; label.Status = QrLabelStatuses.Picked; label.Version++; label.UpdatedAt = now;
            }

            foreach (var split in targets)
                split.PickedQuantity += requested[split.AllocationRunAllocationId].Quantity;
            gather.PickedQuantity += quantity;
            gather.Status = gather.PickedQuantity == gather.RequiredQuantity
                ? RunPickingGatherStatuses.Picked : RunPickingGatherStatuses.Pending;
            view.List.AssignedToUserId ??= actorId;
            view.List.StartedAt ??= now;
            view.List.Status = RunPickingListStatuses.Picking;
            view.List.Version++;
            view.List.UpdatedAt = now;
            store.AddLabelEvent(NewEvent(pickedLabel, actorId, operationKey, QrLabelEventTypes.Picked,
                QrLabelStatuses.Available, QrLabelStatuses.Picked, code, ScanNote(qrCode), now));
            await store.SaveChangesAsync(txCt);
        }, ct);
        store.ClearTracking();
        return await GetAsync(code, actorId, role, ct);
    }

    public async Task<PickingListResult> CompleteAsync(string code, CompleteRunPickingData data,
        Guid actorId, string role, CancellationToken ct)
    {
        EnsureWarehouseKeeper(role);
        code = NormalizeCode(code, "Picking code");
        await store.ExecuteInTransactionAsync(async txCt =>
        {
            var view = await FindAccessibleAsync(code, actorId, role, true, txCt);
            if (view.List.AssignedToUserId.HasValue && view.List.AssignedToUserId != actorId)
                throw PickingOperationException.Conflict("Picking list is assigned to another Warehouse Keeper.");
            var exceptionType = NormalizeOptional(data.ExceptionType);
            var note = NormalizeOptional(data.Note);
            if (view.List.Status == RunPickingListStatuses.Ready) return;
            if (view.List.Status == RunPickingListStatuses.NeedsHandling)
            {
                if (view.List.ExceptionType == exceptionType && view.List.ExceptionNote == note) return;
                throw PickingOperationException.Conflict("Picking exception was already recorded.");
            }
            if (view.List.Version != data.ExpectedPickingVersion)
                throw PickingOperationException.Conflict($"Picking version is {view.List.Version}; refresh and retry.");
            var now = timeProvider.GetUtcNow();
            if (exceptionType is null)
            {
                if (view.Gathers.Any(x => x.PickedQuantity != x.RequiredQuantity) ||
                    view.Splits.Any(x => x.PickedQuantity != x.Quantity))
                    throw PickingOperationException.Conflict("Every approved gather and allocation split must be fully picked.");
                view.List.Status = RunPickingListStatuses.Ready;
                await CreateShipmentsAsync(view, now, txCt);
            }
            else
            {
                if (!ExceptionTypes.Contains(exceptionType))
                    throw PickingOperationException.Validation("Picking exception type is invalid.");
                if (note is null) throw PickingOperationException.Validation("A picking exception note is required.");
                if (note.Length > 500)
                    throw PickingOperationException.Validation("Picking exception note must not exceed 500 characters.");
                view.List.Status = RunPickingListStatuses.NeedsHandling;
                view.List.ExceptionType = exceptionType;
                view.List.ExceptionNote = note;
            }
            view.List.CompletedAt = now;
            view.List.CompletedByUserId = actorId;
            view.List.Version++;
            view.List.UpdatedAt = now;
            await store.SaveChangesAsync(txCt);
        }, ct);
        store.ClearTracking();
        return await GetAsync(code, actorId, role, ct);
    }

    private async Task<RunPickingWorkflowView> FindAccessibleAsync(string code, Guid actorId,
        string role, bool update, CancellationToken ct)
    {
        var view = await store.FindAsync(NormalizeCode(code, "Picking code"), update, ct)
            ?? throw PickingOperationException.NotFound("Picking list was not found.");
        if (!await store.CanAccessWarehouseAsync(actorId, role, view.List.WarehouseId, ct))
            throw PickingOperationException.Forbidden("User is not assigned to this picking warehouse.");
        return view;
    }

    // F92: once a warehouse's run picking portion is Ready, create one shipment part per distinct
    // order in it, keyed on (order, warehouse). A run picking list spans several orders, so a Ready
    // list yields one shipment per order; creation is idempotent (skips an order that already has a
    // shipment for this warehouse).
    private async Task CreateShipmentsAsync(RunPickingWorkflowView view, DateTimeOffset now, CancellationToken ct)
    {
        var perOrder = view.Splits
            .GroupBy(split => split.SalesOrderId)
            .Select(group => new { OrderId = group.Key, Quantity = group.Sum(split => split.PickedQuantity) });
        foreach (var order in perOrder)
        {
            if (await store.ShipmentExistsAsync(order.OrderId, view.List.WarehouseId, ct)) continue;
            store.AddShipment(new Shipment
            {
                Id = Guid.NewGuid(),
                SalesOrderId = order.OrderId,
                WarehouseId = view.List.WarehouseId,
                RunPickingListId = view.List.Id,
                ShipmentCode = await store.NextShipmentCodeAsync(ct),
                Status = ShipmentStatuses.Ready,
                TotalQuantity = order.Quantity,
                ReceivedQuantity = 0,
                ReadyAt = now,
                Version = 0,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
    }

    private static PickingListResult ToResult(RunPickingWorkflowView view) => new(
        view.List.Id, view.List.PickingCode, view.List.AllocationRunId, view.List.AllocationPlanVersionId,
        view.List.WarehouseId, view.List.AssignedToUserId, view.List.Status, view.List.Version,
        view.List.IssuedAt, view.List.StartedAt, view.List.CompletedAt, view.List.CompletedByUserId,
        view.List.ExceptionType, view.List.ExceptionNote,
        view.Gathers.OrderBy(x => x.BatchCodeSnapshot).Select(g => new RunPickingGatherWorkflowResult(
            g.Id, g.ProductId, g.SkuSnapshot, g.BatchId, g.BatchCodeSnapshot, g.RequiredQuantity,
            g.PickedQuantity, g.RequiredQuantity - g.PickedQuantity, g.Status,
            view.Splits.Where(x => x.RunPickingGatherId == g.Id).OrderBy(x => x.OrderCodeSnapshot)
                .Select(x => new RunPickingSplitWorkflowResult(x.Id, x.AllocationRunAllocationId,
                    x.AllocationPlanPriceId, x.SalesOrderId, x.SalesOrderLineId, x.OrderCodeSnapshot,
                    x.Quantity, x.PickedQuantity, x.Quantity - x.PickedQuantity)).ToArray())).ToArray(),
        view.Scans.Select(x => new PickingScanResult(x.Event.OperationKey, x.QrCode,
            x.Event.Quantity, x.Event.PerformedByUserId, x.Event.OccurredAt)).ToArray());

    private static QrLabelEvent NewEvent(BatchQrLabel label, Guid actorId, string key, string type,
        string from, string to, string reference, string note, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), BatchQrLabelId = label.Id, WarehouseId = label.CurrentWarehouseId,
        PerformedByUserId = actorId, OperationKey = key, EventType = type, FromStatus = from,
        ToStatus = to, Quantity = label.Quantity, ReferenceCode = reference, Note = note, OccurredAt = now
    };
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
    private static void ValidateQuantity(decimal? quantity)
    {
        if (!quantity.HasValue || quantity <= 0 || decimal.Truncate(quantity.Value) != quantity.Value)
            throw PickingOperationException.Validation("Picking quantity must be a positive whole number of cartons.");
    }
    private static string NormalizeCode(string value, string field) => string.IsNullOrWhiteSpace(value)
        ? throw PickingOperationException.Validation($"{field} is required.") : value.Trim().ToUpperInvariant();
    private static string NormalizeOperationKey(string value)
    {
        value = value?.Trim() ?? string.Empty;
        if (value.Length is < 1 or > 90)
            throw PickingOperationException.Validation("Idempotency-Key is required and must not exceed 90 characters.");
        return value;
    }
    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string ScanNote(string qrCode) => $"Quét tem {qrCode} để soạn gom";
}
