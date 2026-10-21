using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.QrLabels;

public sealed class QrLabelService(IQrLabelStore store, IQrCodeRenderer qrCodeRenderer, TimeProvider timeProvider)
{
    /// <summary>
    /// Builds a printable TEM label document for a single QR label. The document embeds a
    /// scannable QR code (encoding the TEM code) plus batch, product, warehouse and expiry
    /// details, ready to send to a label printer (BE Sprint 1 - "In tem TEM + QR").
    /// </summary>
    public async Task<QrLabelPrintDocument> RenderPrintableAsync(
        string qrCode, Guid userId, string role, CancellationToken cancellationToken)
    {
        var view = await FindAccessibleAsync(qrCode, userId, role, cancellationToken);
        var label = BuildPrintLabel(view);
        var html = BuildPrintHtml([label]);
        return new QrLabelPrintDocument("text/html", $"TEM-{view.Label.QrCode}.html", html, [label]);
    }

    /// <summary>
    /// Builds a printable TEM label sheet for every active label of a batch (one per carton),
    /// so a warehouse keeper can print an entire receipt in a single pass.
    /// </summary>
    public async Task<QrLabelPrintDocument> RenderBatchPrintableAsync(
        Guid batchId, Guid userId, string role, CancellationToken cancellationToken)
    {
        var batch = await store.FindBatchAsync(batchId, cancellationToken)
            ?? throw QrLabelOperationException.NotFound("Batch was not found.");
        await EnsureWarehouseAccessAsync(userId, role, batch.WarehouseId, cancellationToken);
        var views = await store.ListAsync(userId, role, null, batchId, cancellationToken);
        if (views.Count == 0)
            throw QrLabelOperationException.NotFound("The batch has no QR labels to print.");
        var labels = views
            .Where(item => item.Label.Status != QrLabelStatuses.Invalidated)
            .Select(BuildPrintLabel)
            .ToArray();
        if (labels.Length == 0)
            throw QrLabelOperationException.NotFound("The batch has no printable QR labels.");
        var html = BuildPrintHtml(labels);
        return new QrLabelPrintDocument("text/html", $"TEM-{batch.BatchCode}.html", html, labels);
    }

    private QrLabelPrintResult BuildPrintLabel(QrLabelView view) => new(
        view.Label.QrCode, view.Batch.BatchCode, view.Product.Sku, view.Product.Name,
        view.Warehouse.Code, view.Warehouse.Name, view.Label.Quantity,
        view.Batch.ExpirationDate, qrCodeRenderer.RenderSvg(view.Label.QrCode));

    private static string BuildPrintHtml(IReadOnlyCollection<QrLabelPrintResult> labels)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("<!DOCTYPE html><html lang=\"vi\"><head><meta charset=\"utf-8\">");
        builder.Append("<title>Tem TEM + QR</title><style>");
        builder.Append("@page{size:70mm 50mm;margin:2mm}");
        builder.Append("body{font-family:Arial,Helvetica,sans-serif;margin:0}");
        builder.Append(".label{width:66mm;height:46mm;padding:2mm;box-sizing:border-box;page-break-after:always;");
        builder.Append("display:flex;gap:3mm;align-items:center;border:1px solid #000}");
        builder.Append(".qr{width:30mm;height:30mm;flex:0 0 auto}.qr svg{width:100%;height:100%}");
        builder.Append(".info{font-size:9pt;line-height:1.3}.code{font-size:12pt;font-weight:bold}");
        builder.Append(".exp{color:#b00}</style></head><body>");
        foreach (var label in labels)
        {
            builder.Append("<div class=\"label\"><div class=\"qr\">")
                .Append(label.QrSvg)
                .Append("</div><div class=\"info\">")
                .Append("<div class=\"code\">").Append(Encode(label.QrCode)).Append("</div>")
                .Append("<div>SKU: ").Append(Encode(label.Sku)).Append("</div>")
                .Append("<div>").Append(Encode(label.ProductName)).Append("</div>")
                .Append("<div>Lô: ").Append(Encode(label.BatchCode)).Append("</div>")
                .Append("<div>Kho: ").Append(Encode(label.WarehouseCode)).Append("</div>")
                .Append("<div>SL: ").Append(label.Quantity.ToString("0.###")).Append(" thùng</div>")
                .Append("<div class=\"exp\">HSD: ").Append(label.ExpirationDate.ToString("dd/MM/yyyy")).Append("</div>")
                .Append("</div></div>");
        }
        builder.Append("</body></html>");
        return builder.ToString();
    }

    private static string Encode(string value) => System.Net.WebUtility.HtmlEncode(value);

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
        if (data.Quantity <= 0 || decimal.Truncate(data.Quantity) != data.Quantity)
            throw QrLabelOperationException.Validation("Label quantity must be a positive whole number of cartons.");
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
        if (data.Quantity <= 0 || decimal.Truncate(data.Quantity) != data.Quantity)
            throw QrLabelOperationException.Validation("Split quantity must be a positive whole number of cartons.");
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
            throw QrLabelOperationException.Conflict("Invalidated labels cannot be inspected.");

        var fromStatus = label.Status;
        var now = timeProvider.GetUtcNow();
        if (data.WarehouseId != label.CurrentWarehouseId)
            throw QrLabelOperationException.Conflict("QR label is not currently located at this warehouse.");
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
            throw QrLabelOperationException.Validation("Action must be Inspect.");
        return action.Trim().ToLowerInvariant() switch
        {
            "inspect" => QrLabelEventTypes.Inspected,
            "issue" or "receive" => throw QrLabelOperationException.Conflict(
                "Generic QR movement is disabled. Use the picking and shipment workflows."),
            _ => throw QrLabelOperationException.Validation("Action must be Inspect.")
        };
    }

    private static string NormalizeCode(string code) => string.IsNullOrWhiteSpace(code)
        ? throw QrLabelOperationException.Validation("QR code is required.") : code.Trim().ToUpperInvariant();
    private static void ValidateOperationKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Trim().Length > 100)
            throw QrLabelOperationException.Validation("Idempotency-Key is required and must not exceed 100 characters.");
    }
}
