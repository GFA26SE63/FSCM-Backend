using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.QrLabels;

public sealed class QrLabelService(IQrLabelStore store, TimeProvider timeProvider)
{
    public async Task<IReadOnlyCollection<QrLabelResult>> ListAsync(
        Guid userId, string role, Guid? warehouseId, Guid? batchId, CancellationToken cancellationToken) =>
        (await store.ListAsync(userId, role, warehouseId, batchId, cancellationToken)).Select(ToResult).ToArray();

    public async Task<QrLabelDetailResult> GetAsync(
        string qrCode, Guid userId, string role, CancellationToken cancellationToken)
    {
        var view = await FindAccessibleAsync(qrCode, userId, role, cancellationToken);
        return await ToDetailAsync(view, cancellationToken);
    }

    public async Task<QrLabelResult> GenerateAsync(
        GenerateQrLabelData data, string operationKey, Guid userId, string role, CancellationToken cancellationToken)
    {
        ValidateOperationKey(operationKey);
        if (data.Quantity <= 0) throw QrLabelOperationException.Validation("Label quantity must be greater than zero.");
        if (data.BlobUrl?.Length > 2000) throw QrLabelOperationException.Validation("Label blob URL is too long.");
        var existing = await FindIdempotentAsync(operationKey, QrLabelEventTypes.Generated, cancellationToken);
        if (existing is not null)
        {
            var existingView = await store.FindByIdAsync(existing.BatchQrLabelId, cancellationToken)
                ?? throw QrLabelOperationException.Conflict("The prior label operation is incomplete.");
            if (existingView.Label.BatchId != data.BatchId || existing.Quantity != data.Quantity ||
                !string.Equals(existingView.Label.BlobUrl, data.BlobUrl?.Trim(), StringComparison.Ordinal))
                throw QrLabelOperationException.Conflict("Operation key was already used with a different label generation request.");
            await EnsureWarehouseAccessAsync(userId, role, existingView.Label.CurrentWarehouseId, cancellationToken);
            return ToResult(existingView);
        }

        var batch = await store.FindBatchAsync(data.BatchId, cancellationToken)
            ?? throw QrLabelOperationException.NotFound("Batch was not found.");
        await EnsureWarehouseAccessAsync(userId, role, batch.WarehouseId, cancellationToken);
        var balance = await store.FindBalanceAsync(batch.Id, cancellationToken)
            ?? throw QrLabelOperationException.Conflict("Batch inventory balance was not found.");
        var activeQuantity = await store.SumActiveQuantityAsync(batch.Id, cancellationToken);
        if (activeQuantity + data.Quantity > balance.PhysicalQuantity)
            throw QrLabelOperationException.Conflict("Label quantity exceeds the unlabelled physical batch quantity.");

        var now = timeProvider.GetUtcNow();
        var id = Guid.NewGuid();
        var label = new BatchQrLabel
        {
            Id = id,
            BatchId = batch.Id,
            CurrentWarehouseId = batch.WarehouseId,
            RootLabelId = id,
            QrCode = await store.NextQrCodeAsync(cancellationToken),
            Quantity = data.Quantity,
            Status = QrLabelStatuses.Available,
            BlobUrl = data.BlobUrl?.Trim(),
            Version = 1,
            GeneratedAt = now,
            UpdatedAt = now
        };
        store.AddLabel(label);
        store.AddEvent(CreateEvent(label, batch.WarehouseId, userId, operationKey,
            QrLabelEventTypes.Generated, string.Empty, label.Status, null, "Sinh tem QR cho lô hàng", now));
        await store.SaveChangesAsync(cancellationToken);
        return ToResult((await store.FindByIdAsync(label.Id, cancellationToken))!);
    }

    public async Task<QrLabelResult> SplitAsync(
        string qrCode, SplitQrLabelData data, string operationKey, Guid userId, string role,
        CancellationToken cancellationToken)
    {
        ValidateOperationKey(operationKey);
        var existing = await FindIdempotentAsync(operationKey, QrLabelEventTypes.Split, cancellationToken);
        if (existing is not null)
        {
            var view = await store.FindByIdAsync(existing.BatchQrLabelId, cancellationToken)
                ?? throw QrLabelOperationException.Conflict("The prior split operation is incomplete.");
            if (!string.Equals(existing.ReferenceCode, NormalizeCode(qrCode), StringComparison.Ordinal) ||
                existing.Quantity != data.Quantity)
                throw QrLabelOperationException.Conflict("Operation key was already used with a different split request.");
            await EnsureWarehouseAccessAsync(userId, role, view.Label.CurrentWarehouseId, cancellationToken);
            return ToResult(view);
        }
        if (data.Quantity <= 0) throw QrLabelOperationException.Validation("Split quantity must be greater than zero.");
        var parent = await store.FindForUpdateAsync(NormalizeCode(qrCode), cancellationToken)
            ?? throw QrLabelOperationException.NotFound("QR label was not found.");
        await EnsureWarehouseAccessAsync(userId, role, parent.CurrentWarehouseId, cancellationToken);
        if (parent.Status != QrLabelStatuses.Available)
            throw QrLabelOperationException.Conflict("Only an available label can be split.");
        if (parent.Version != data.ExpectedVersion)
            throw QrLabelOperationException.Conflict("QR label was changed by another operation. Refresh and retry.");
        if (data.Quantity >= parent.Quantity)
            throw QrLabelOperationException.Validation("Split quantity must be less than the parent label quantity.");

        var now = timeProvider.GetUtcNow();
        parent.Quantity -= data.Quantity;
        parent.Version++;
        parent.UpdatedAt = now;
        var childId = Guid.NewGuid();
        var child = new BatchQrLabel
        {
            Id = childId,
            BatchId = parent.BatchId,
            CurrentWarehouseId = parent.CurrentWarehouseId,
            ParentLabelId = parent.Id,
            RootLabelId = parent.RootLabelId ?? parent.Id,
            QrCode = await store.NextQrCodeAsync(cancellationToken),
            Quantity = data.Quantity,
            Status = QrLabelStatuses.Available,
            Version = 1,
            GeneratedAt = now,
            UpdatedAt = now
        };
        store.AddLabel(child);
        store.AddEvent(CreateEvent(child, child.CurrentWarehouseId, userId, operationKey,
            QrLabelEventTypes.Split, parent.Status, child.Status, parent.QrCode,
            $"Tách {data.Quantity:0.###} đơn vị từ tem cha {parent.QrCode}", now));
        await store.SaveChangesAsync(cancellationToken);
        return ToResult((await store.FindByIdAsync(child.Id, cancellationToken))!);
    }

    public async Task<QrLabelResult> ScanAsync(
        string qrCode, ScanQrLabelData data, string operationKey, Guid userId, string role,
        CancellationToken cancellationToken)
    {
        ValidateOperationKey(operationKey);
        var eventType = NormalizeAction(data.Action);
        var existing = await FindIdempotentAsync(operationKey, eventType, cancellationToken);
        if (existing is not null)
        {
            var view = await store.FindByIdAsync(existing.BatchQrLabelId, cancellationToken)
                ?? throw QrLabelOperationException.Conflict("The prior scan operation is incomplete.");
            if (!string.Equals(view.Label.QrCode, NormalizeCode(qrCode), StringComparison.Ordinal) ||
                existing.WarehouseId != data.WarehouseId ||
                !string.Equals(existing.ReferenceCode, data.ReferenceCode?.Trim(), StringComparison.OrdinalIgnoreCase) ||
                eventType == QrLabelEventTypes.Received && view.Label.BatchId != data.DestinationBatchId)
                throw QrLabelOperationException.Conflict("Operation key was already used with a different scan request.");
            await EnsureWarehouseAccessAsync(userId, role, existing.WarehouseId, cancellationToken);
            return ToResult(view);
        }

        var label = await store.FindForUpdateAsync(NormalizeCode(qrCode), cancellationToken)
            ?? throw QrLabelOperationException.NotFound("QR label was not found.");
        await EnsureWarehouseAccessAsync(userId, role, data.WarehouseId, cancellationToken);
        if (label.Version != data.ExpectedVersion)
            throw QrLabelOperationException.Conflict("QR label was changed by another operation. Refresh and retry.");
        if (label.Status == QrLabelStatuses.Invalidated)
            throw QrLabelOperationException.Conflict("Invalidated labels cannot be scanned for movement.");

        var fromStatus = label.Status;
        var now = timeProvider.GetUtcNow();
        switch (eventType)
        {
            case QrLabelEventTypes.Inspected:
                if (data.WarehouseId != label.CurrentWarehouseId)
                    throw QrLabelOperationException.Conflict("QR label is not currently located at this warehouse.");
                break;
            case QrLabelEventTypes.Issued:
                RequireReference(data.ReferenceCode);
                if (label.Status != QrLabelStatuses.Available || data.WarehouseId != label.CurrentWarehouseId)
                    throw QrLabelOperationException.Conflict("Only an available label at its current warehouse can be issued.");
                label.Status = QrLabelStatuses.InTransit;
                break;
            case QrLabelEventTypes.Received:
                RequireReference(data.ReferenceCode);
                if (label.Status != QrLabelStatuses.InTransit)
                    throw QrLabelOperationException.Conflict("Only an in-transit label can be received.");
                var latest = await store.FindLatestEventAsync(label.Id, cancellationToken);
                if (latest?.EventType != QrLabelEventTypes.Issued ||
                    !string.Equals(latest.ReferenceCode, data.ReferenceCode?.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw QrLabelOperationException.Conflict("Receipt reference must match the latest issue reference.");
                if (!data.DestinationBatchId.HasValue)
                    throw QrLabelOperationException.Validation("Destination batch is required when receiving a label.");
                var sourceBatch = await store.FindBatchAsync(label.BatchId, cancellationToken)
                    ?? throw QrLabelOperationException.Conflict("Source batch was not found.");
                var destinationBatch = await store.FindBatchAsync(data.DestinationBatchId.Value, cancellationToken)
                    ?? throw QrLabelOperationException.NotFound("Destination batch was not found.");
                if (destinationBatch.WarehouseId != data.WarehouseId || destinationBatch.ProductId != sourceBatch.ProductId)
                    throw QrLabelOperationException.Conflict("Destination batch must belong to the receiving warehouse and the same product.");
                label.BatchId = destinationBatch.Id;
                label.CurrentWarehouseId = data.WarehouseId;
                label.Status = QrLabelStatuses.Available;
                break;
        }
        label.Version++;
        label.UpdatedAt = now;
        store.AddEvent(CreateEvent(label, data.WarehouseId, userId, operationKey, eventType,
            fromStatus, label.Status, data.ReferenceCode?.Trim(), data.Note?.Trim(), now));
        await store.SaveChangesAsync(cancellationToken);
        return ToResult((await store.FindByIdAsync(label.Id, cancellationToken))!);
    }

    private async Task<QrLabelView> FindAccessibleAsync(string code, Guid userId, string role, CancellationToken cancellationToken)
    {
        var view = await store.FindByCodeAsync(NormalizeCode(code), cancellationToken)
            ?? throw QrLabelOperationException.NotFound("QR label was not found.");
        await EnsureWarehouseAccessAsync(userId, role, view.Label.CurrentWarehouseId, cancellationToken);
        return view;
    }

    private async Task<QrLabelDetailResult> ToDetailAsync(QrLabelView view, CancellationToken cancellationToken)
    {
        var rootId = view.Label.RootLabelId ?? view.Label.Id;
        var lineage = await store.ListLineageAsync(rootId, cancellationToken);
        var events = await store.ListEventsAsync(rootId, cancellationToken);
        return new(ToResult(view), lineage.Select(ToResult).ToArray(), events.Select(item => new QrLabelEventResult(
            item.Event.Id, item.Event.OperationKey, item.Event.EventType, item.Event.WarehouseId,
            item.WarehouseCode, item.Event.FromStatus, item.Event.ToStatus, item.Event.Quantity,
            item.Event.ReferenceCode, item.Event.Note, item.Event.PerformedByUserId, item.Event.OccurredAt)).ToArray());
    }

    private async Task<QrLabelEvent?> FindIdempotentAsync(string key, string eventType, CancellationToken cancellationToken)
    {
        var existing = await store.FindEventAsync(key.Trim(), cancellationToken);
        if (existing is not null && existing.EventType != eventType)
            throw QrLabelOperationException.Conflict("Operation key was already used for a different QR label action.");
        return existing;
    }

    private async Task EnsureWarehouseAccessAsync(Guid userId, string role, Guid warehouseId, CancellationToken cancellationToken)
    {
        if (!await store.CanAccessWarehouseAsync(userId, role, warehouseId, cancellationToken))
            throw QrLabelOperationException.Forbidden("You are not assigned to this warehouse.");
    }

    private static QrLabelEvent CreateEvent(BatchQrLabel label, Guid warehouseId, Guid userId,
        string key, string type, string from, string to, string? reference, string? note, DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            BatchQrLabelId = label.Id,
            WarehouseId = warehouseId,
            PerformedByUserId = userId,
            OperationKey = key.Trim(),
            EventType = type,
            FromStatus = from,
            ToStatus = to,
            Quantity = label.Quantity,
            ReferenceCode = reference,
            Note = note,
            OccurredAt = now
        };

    private static QrLabelResult ToResult(QrLabelView view) => new(
        view.Label.Id, view.Label.QrCode, view.Label.QrCode, view.Label.BatchId, view.Batch.BatchCode,
        view.Label.CurrentWarehouseId, view.Warehouse.Code, view.Batch.ProductId, view.Product.Sku,
        view.Label.Quantity, view.Label.Status, view.Label.ParentLabelId, view.ParentQrCode,
        view.Label.RootLabelId ?? view.Label.Id, view.RootQrCode, view.Label.BlobUrl,
        view.Label.Version, view.Label.GeneratedAt, view.Label.UpdatedAt, view.Label.InvalidatedAt);

    private static string NormalizeAction(string action)
    {
        if (string.IsNullOrWhiteSpace(action))
            throw QrLabelOperationException.Validation("Action must be Inspect, Issue, or Receive.");
        return action.Trim().ToLowerInvariant() switch
        {
            "inspect" => QrLabelEventTypes.Inspected,
            "issue" => QrLabelEventTypes.Issued,
            "receive" => QrLabelEventTypes.Received,
            _ => throw QrLabelOperationException.Validation("Action must be Inspect, Issue, or Receive.")
        };
    }

    private static string NormalizeCode(string code) => string.IsNullOrWhiteSpace(code)
        ? throw QrLabelOperationException.Validation("QR code is required.") : code.Trim().ToUpperInvariant();
    private static void ValidateOperationKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Trim().Length > 100)
            throw QrLabelOperationException.Validation("Idempotency-Key is required and must not exceed 100 characters.");
    }
    private static void RequireReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference) || reference.Trim().Length > 100)
            throw QrLabelOperationException.Validation("A reference code of at most 100 characters is required.");
    }
}
