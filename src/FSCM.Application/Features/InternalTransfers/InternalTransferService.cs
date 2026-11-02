using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.QrLabels;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.InternalTransfers;

public sealed class InternalTransferService(IInternalTransferStore store, TimeProvider timeProvider)
{
    public async Task<IReadOnlyCollection<InternalTransferSuggestionResult>> GetSuggestionsAsync(
        Guid? destinationWarehouseId, Guid? productId, CancellationToken cancellationToken)
    {
        var threshold = await store.GetRegionalMinimumStockAsync(cancellationToken);
        var positions = await store.ListSuggestionInventoryAsync(cancellationToken);
        var activeRegionalWarehouses = await store.ListActiveRegionalWarehousesAsync(cancellationToken);
        var central = positions.Where(item => item.Warehouse.IsActive && item.Balance.AvailableQuantity > 0 &&
            item.Batch.ExpirationDate >= DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime) &&
            (!productId.HasValue || item.Product.Id == productId.Value)).ToArray();
        var regionalWarehouses = activeRegionalWarehouses
            .Where(item => !destinationWarehouseId.HasValue || item.Id == destinationWarehouseId.Value).ToArray();
        var products = central.Select(item => item.Product).DistinctBy(item => item.Id).ToArray();
        var results = new List<InternalTransferSuggestionResult>();
        foreach (var destination in regionalWarehouses)
            foreach (var product in products)
            {
                var destinationAvailable = positions.Where(item => item.Warehouse.Id == destination.Id &&
                    item.Product.Id == product.Id).Sum(item => item.Balance.AvailableQuantity);
                if (destinationAvailable >= threshold) continue;
                var required = threshold - destinationAvailable;
                var sourceGroup = central.Where(item => item.Product.Id == product.Id)
                    .GroupBy(item => item.Warehouse.Id)
                    .OrderByDescending(group => group.Sum(item => item.Balance.AvailableQuantity))
                    .ThenBy(group => group.First().Warehouse.Code, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (sourceGroup is null) continue;
                var remaining = required;
                var selections = new List<TransferSuggestionBatchResult>();
                foreach (var position in sourceGroup.OrderBy(item => item.Batch.ExpirationDate).ThenBy(item => item.Batch.BatchCode, StringComparer.Ordinal))
                {
                    if (remaining <= 0) break;
                    var quantity = Math.Min(remaining, position.Balance.AvailableQuantity);
                    selections.Add(new(position.Batch.Id, position.Batch.BatchCode, position.Batch.ExpirationDate,
                        position.Balance.AvailableQuantity, quantity));
                    remaining -= quantity;
                }
                var source = sourceGroup.First().Warehouse;
                results.Add(new(source.Id, source.Code, destination.Id, destination.Code, product.Id,
                    product.Sku, product.Name, threshold, destinationAvailable, required - remaining, selections));
            }
        return results.OrderBy(item => item.DestinationWarehouseCode, StringComparer.Ordinal)
            .ThenBy(item => item.Sku, StringComparer.Ordinal).ToArray();
    }

    public async Task<IReadOnlyCollection<InternalTransferResult>> ListAsync(
        Guid userId, string role, Guid? warehouseId, string? status, CancellationToken cancellationToken) =>
        (await store.ListTransfersAsync(userId, role, warehouseId, NormalizeOptional(status), cancellationToken))
        .Select(ToResult).ToArray();

    public async Task<InternalTransferResult> GetAsync(
        Guid id, Guid userId, string role, CancellationToken cancellationToken)
    {
        var view = await store.FindTransferAsync(id, false, cancellationToken)
            ?? throw InternalTransferOperationException.NotFound("Internal transfer was not found.");
        if (!await CanAccessEitherWarehouseAsync(userId, role, view.Transfer, cancellationToken))
            throw InternalTransferOperationException.Forbidden("You cannot access this internal transfer.");
        return ToResult(view);
    }

    public async Task<InternalTransferResult> CreateAsync(
        CreateInternalTransferData data, string operationKey, Guid userId, string role,
        CancellationToken cancellationToken)
    {
        ValidateOperationKey(operationKey);
        var replay = await ReplayAsync(operationKey, InternalTransferActions.Created, null, cancellationToken);
        if (replay is not null)
        {
            var replayLines = replay.Items.OrderBy(item => item.Item.SourceBatchId)
                .Select(item => (item.Item.SourceBatchId, item.Item.RequestedQuantity, item.Item.Note)).ToArray();
            var requestLines = data.Lines.OrderBy(item => item.SourceBatchId)
                .Select(item => (item.SourceBatchId, item.Quantity, Note: NormalizeOptional(item.Note))).ToArray();
            if (replay.Transfer.SourceWarehouseId != data.SourceWarehouseId ||
                replay.Transfer.DestinationWarehouseId != data.DestinationWarehouseId ||
                !replayLines.SequenceEqual(requestLines) || replay.Transfer.Note != NormalizeOptional(data.Note))
                throw InternalTransferOperationException.Conflict("Operation key was already used with a different transfer request.");
            return ToResult(replay);
        }
        if (data.SourceWarehouseId == data.DestinationWarehouseId)
            throw InternalTransferOperationException.Validation("Source and destination warehouses must be different.");
        if (data.Lines.Count == 0)
            throw InternalTransferOperationException.Validation("At least one transfer line is required.");
        if (data.Lines.GroupBy(item => item.SourceBatchId).Any(group => group.Count() > 1))
            throw InternalTransferOperationException.Validation("A source batch may appear only once in a transfer.");
        var source = await store.FindWarehouseAsync(data.SourceWarehouseId, cancellationToken)
            ?? throw InternalTransferOperationException.NotFound("Source warehouse was not found.");
        var destination = await store.FindWarehouseAsync(data.DestinationWarehouseId, cancellationToken)
            ?? throw InternalTransferOperationException.NotFound("Destination warehouse was not found.");
        if (!source.IsActive || !destination.IsActive)
            throw InternalTransferOperationException.Validation("Both warehouses must be active.");

        var inventories = new List<(CreateInternalTransferLineData Line, TransferInventoryView Inventory)>();
        foreach (var line in data.Lines)
        {
            if (line.Quantity <= 0) throw InternalTransferOperationException.Validation("Transfer quantities must be greater than zero.");
            var inventory = await store.FindInventoryForUpdateAsync(line.SourceBatchId, cancellationToken)
                ?? throw InternalTransferOperationException.NotFound("A source batch was not found.");
            if (inventory.Batch.WarehouseId != source.Id)
                throw InternalTransferOperationException.Validation("Every source batch must belong to the source warehouse.");
            if (inventory.Batch.ExpirationDate < DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime))
                throw InternalTransferOperationException.Conflict("Expired batches cannot be transferred.");
            if (inventory.Balance.AvailableQuantity < line.Quantity)
                throw InternalTransferOperationException.Conflict("Requested transfer quantity exceeds available source inventory.");
            inventories.Add((line, inventory));
        }
        await ValidateFefoSelectionAsync(source.Id, inventories, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var transfer = new WarehouseTransfer
        {
            Id = Guid.NewGuid(),
            TransferCode = await store.NextTransferCodeAsync(DateOnly.FromDateTime(now.UtcDateTime), cancellationToken),
            SourceWarehouseId = source.Id,
            DestinationWarehouseId = destination.Id,
            CreatedByUserId = userId,
            Status = InternalTransferStatuses.Requested,
            Note = NormalizeOptional(data.Note),
            Version = 1,
            RequestedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        store.AddTransfer(transfer);
        foreach (var entry in inventories)
            store.AddTransferItem(new WarehouseTransferItem
            {
                Id = Guid.NewGuid(),
                WarehouseTransferId = transfer.Id,
                SourceBatchId = entry.Inventory.Batch.Id,
                RequestedQuantity = entry.Line.Quantity,
                Status = InternalTransferStatuses.Requested,
                Note = NormalizeOptional(entry.Line.Note),
                CreatedAt = now,
                UpdatedAt = now
            });
        store.AddTransferEvent(CreateEvent(transfer, userId, operationKey, InternalTransferActions.Created,
            string.Empty, transfer.Status, "Tạo phiếu điều chuyển nội bộ", now));
        await store.SaveChangesAsync(cancellationToken);
        return ToResult((await store.FindTransferAsync(transfer.Id, false, cancellationToken))!);
    }

    public Task<InternalTransferResult> ApproveAsync(Guid id, int expectedVersion, string operationKey,
        Guid userId, CancellationToken cancellationToken) => ChangeDecisionAsync(id, expectedVersion, operationKey,
            userId, true, null, cancellationToken);

    public Task<InternalTransferResult> RejectAsync(Guid id, int expectedVersion, string operationKey,
        Guid userId, string reason, CancellationToken cancellationToken) => ChangeDecisionAsync(id,
            expectedVersion, operationKey, userId, false, reason, cancellationToken);

    public async Task<InternalTransferResult> DispatchAsync(
        Guid id, DispatchInternalTransferData data, string operationKey, Guid userId, string role,
        CancellationToken cancellationToken)
    {
        ValidateOperationKey(operationKey);
        var replay = await ReplayAsync(operationKey, InternalTransferActions.Dispatched, id, cancellationToken);
        if (replay is not null)
        {
            var replayLines = replay.Items.OrderBy(item => item.Item.Id).Select(item =>
                (item.Item.Id, Codes: item.Labels.Select(label => label.Label.QrCode).Order(StringComparer.Ordinal).ToArray())).ToArray();
            var requestLines = data.Lines.OrderBy(item => item.TransferItemId).Select(item =>
                (item.TransferItemId, Codes: item.QrCodes.Select(NormalizeQrCode).Order(StringComparer.Ordinal).ToArray())).ToArray();
            if (replayLines.Length != requestLines.Length || replayLines.Where((line, index) =>
                    line.Id != requestLines[index].TransferItemId || !line.Codes.SequenceEqual(requestLines[index].Codes)).Any())
                throw InternalTransferOperationException.Conflict("Operation key was already used with a different dispatch request.");
            await EnsureWarehouseAccessAsync(userId, role, replay.Transfer.SourceWarehouseId, cancellationToken);
            return ToResult(replay);
        }
        var view = await GetForUpdateAsync(id, data.ExpectedVersion, InternalTransferStatuses.Approved, cancellationToken);
        await EnsureWarehouseAccessAsync(userId, role, view.Transfer.SourceWarehouseId, cancellationToken);
        if (data.Lines.Count != view.Items.Count || data.Lines.Select(item => item.TransferItemId).Distinct().Count() != view.Items.Count)
            throw InternalTransferOperationException.Validation("Dispatch must scan every transfer item exactly once.");
        var allCodes = data.Lines.SelectMany(item => item.QrCodes).Select(NormalizeQrCode).ToArray();
        if (allCodes.Length == 0 || allCodes.Distinct(StringComparer.Ordinal).Count() != allCodes.Length)
            throw InternalTransferOperationException.Validation("QR codes are required and cannot be duplicated.");
        var labels = await store.FindLabelsForUpdateAsync(allCodes, cancellationToken);
        if (labels.Count != allCodes.Length)
            throw InternalTransferOperationException.NotFound("One or more QR labels were not found.");

        var now = timeProvider.GetUtcNow();
        foreach (var itemView in view.Items)
        {
            var request = data.Lines.SingleOrDefault(item => item.TransferItemId == itemView.Item.Id)
                ?? throw InternalTransferOperationException.Validation("Dispatch line does not match the transfer.");
            var itemLabels = request.QrCodes.Select(code => labels.Single(item => item.QrCode == NormalizeQrCode(code))).ToArray();
            if (itemLabels.Any(label => label.BatchId != itemView.Item.SourceBatchId ||
                label.CurrentWarehouseId != view.Transfer.SourceWarehouseId || label.Status != QrLabelStatuses.Available))
                throw InternalTransferOperationException.Conflict("Every scanned label must be available on the transfer source batch.");
            if (itemLabels.Sum(label => label.Quantity) != itemView.Item.RequestedQuantity)
                throw InternalTransferOperationException.Conflict("Scanned label quantity must equal the requested transfer quantity.");
            var inventory = await store.FindInventoryForUpdateAsync(itemView.Item.SourceBatchId, cancellationToken)
                ?? throw InternalTransferOperationException.NotFound("Source inventory was not found.");
            if (inventory.Balance.AvailableQuantity < itemView.Item.RequestedQuantity)
                throw InternalTransferOperationException.Conflict("Source inventory is no longer sufficient for dispatch.");
            ApplySourceDispatch(inventory, itemView.Item.RequestedQuantity, now);
            itemView.Item.DispatchedQuantity = itemView.Item.RequestedQuantity;
            itemView.Item.Status = InternalTransferStatuses.InTransit;
            itemView.Item.UpdatedAt = now;
            store.AddInventoryTransaction(CreateInventoryTransaction(inventory, userId, view.Transfer.Id, view.Transfer.TransferCode,
                -itemView.Item.RequestedQuantity, InventoryTransactionTypes.TransferDispatch,
                "Xuất kho điều chuyển nội bộ", now));
            foreach (var label in itemLabels)
            {
                label.Status = QrLabelStatuses.InTransit;
                label.Version++;
                label.UpdatedAt = now;
                store.AddTransferLabel(new WarehouseTransferLabel
                {
                    Id = Guid.NewGuid(),
                    WarehouseTransferItemId = itemView.Item.Id,
                    BatchQrLabelId = label.Id,
                    Quantity = label.Quantity,
                    DispatchedAt = now
                });
                store.AddQrLabelEvent(CreateQrEvent(label, view.Transfer.SourceWarehouseId, userId,
                    $"{operationKey}:{label.QrCode}", QrLabelEventTypes.Issued,
                    QrLabelStatuses.Available, QrLabelStatuses.InTransit, view.Transfer.TransferCode,
                    "Quét xuất kho điều chuyển nội bộ", now));
            }
        }
        var from = view.Transfer.Status;
        view.Transfer.Status = InternalTransferStatuses.InTransit;
        view.Transfer.DispatchedAt = now;
        view.Transfer.UpdatedAt = now;
        view.Transfer.Version++;
        store.AddTransferEvent(CreateEvent(view.Transfer, userId, operationKey, InternalTransferActions.Dispatched,
            from, view.Transfer.Status, "Xuất toàn bộ hàng điều chuyển khỏi kho nguồn", now));
        await store.SaveChangesAsync(cancellationToken);
        return ToResult((await store.FindTransferAsync(id, false, cancellationToken))!);
    }

    public async Task<InternalTransferResult> ReceiveAsync(
        Guid id, ReceiveInternalTransferData data, string operationKey, Guid userId, string role,
        CancellationToken cancellationToken)
    {
        ValidateOperationKey(operationKey);
        var replay = await ReplayAsync(operationKey, InternalTransferActions.Received, id, cancellationToken);
        if (replay is not null)
        {
            var replayCodes = replay.Items.SelectMany(item => item.Labels).Select(item => item.Label.QrCode)
                .Order(StringComparer.Ordinal).ToArray();
            var requestCodes = data.QrCodes.Select(NormalizeQrCode).Order(StringComparer.Ordinal).ToArray();
            if (!replayCodes.SequenceEqual(requestCodes))
                throw InternalTransferOperationException.Conflict("Operation key was already used with a different receipt request.");
            await EnsureWarehouseAccessAsync(userId, role, replay.Transfer.DestinationWarehouseId, cancellationToken);
            return ToResult(replay);
        }
        var view = await GetForUpdateAsync(id, data.ExpectedVersion, InternalTransferStatuses.InTransit, cancellationToken);
        await EnsureWarehouseAccessAsync(userId, role, view.Transfer.DestinationWarehouseId, cancellationToken);
        var requestedCodes = data.QrCodes.Select(NormalizeQrCode).ToArray();
        var expectedCodes = view.Items.SelectMany(item => item.Labels).Select(item => item.Label.QrCode).ToArray();
        if (requestedCodes.Length != expectedCodes.Length || requestedCodes.Distinct(StringComparer.Ordinal).Count() != requestedCodes.Length ||
            !requestedCodes.Order(StringComparer.Ordinal).SequenceEqual(expectedCodes.Order(StringComparer.Ordinal)))
            throw InternalTransferOperationException.Validation("Receipt must scan every dispatched QR label exactly once.");

        var now = timeProvider.GetUtcNow();
        foreach (var itemView in view.Items)
        {
            var destinationBatch = await store.FindDestinationBatchForUpdateAsync(
                view.Transfer.DestinationWarehouseId, itemView.SourceBatch.ProductId,
                itemView.SourceBatch.ExpirationDate, cancellationToken);
            if (destinationBatch is null)
            {
                var idBatch = Guid.NewGuid();
                destinationBatch = new Batch
                {
                    Id = idBatch,
                    ParentBatchId = itemView.SourceBatch.Id,
                    RootBatchId = itemView.SourceBatch.RootBatchId ?? itemView.SourceBatch.Id,
                    WarehouseId = view.Transfer.DestinationWarehouseId,
                    ProductId = itemView.SourceBatch.ProductId,
                    BatchCode = $"{view.DestinationWarehouse.Code}-{itemView.Product.Sku}-{itemView.SourceBatch.ExpirationDate:ddMMyyyy}",
                    ExpirationDate = itemView.SourceBatch.ExpirationDate,
                    ExpiryStatus = itemView.SourceBatch.ExpiryStatus,
                    StockStatus = BatchStockStatuses.InStock,
                    Version = 1,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                store.AddBatch(destinationBatch);
            }
            var balance = await store.FindBalanceForUpdateAsync(destinationBatch.Id, cancellationToken);
            if (balance is null)
            {
                balance = new InventoryBalance { Id = Guid.NewGuid(), BatchId = destinationBatch.Id, Version = 1 };
                store.AddBalance(balance);
            }
            destinationBatch.Quantity += itemView.Item.DispatchedQuantity;
            destinationBatch.StockStatus = BatchStockStatuses.InStock;
            destinationBatch.Version++;
            destinationBatch.UpdatedAt = now;
            balance.PhysicalQuantity += itemView.Item.DispatchedQuantity;
            balance.AvailableQuantity = balance.PhysicalQuantity - balance.ReservedQuantity;
            balance.Version++;
            balance.UpdatedAt = now;
            itemView.Item.DestinationBatchId = destinationBatch.Id;
            itemView.Item.ReceivedQuantity = itemView.Item.DispatchedQuantity;
            itemView.Item.Status = InternalTransferStatuses.Received;
            itemView.Item.UpdatedAt = now;
            store.AddInventoryTransaction(CreateInventoryTransaction(destinationBatch, balance, userId, view.Transfer.Id,
                view.Transfer.TransferCode, itemView.Item.ReceivedQuantity, InventoryTransactionTypes.TransferReceipt,
                "Nhận kho điều chuyển nội bộ", now));
            foreach (var binding in itemView.Labels)
            {
                var label = binding.Label;
                if (label.Status != QrLabelStatuses.InTransit)
                    throw InternalTransferOperationException.Conflict("A dispatched label is no longer in transit.");
                label.BatchId = destinationBatch.Id;
                label.CurrentWarehouseId = view.Transfer.DestinationWarehouseId;
                label.Status = QrLabelStatuses.Available;
                label.Version++;
                label.UpdatedAt = now;
                binding.Link.ReceivedAt = now;
                store.AddQrLabelEvent(CreateQrEvent(label, view.Transfer.DestinationWarehouseId, userId,
                    $"{operationKey}:{label.QrCode}", QrLabelEventTypes.Received,
                    QrLabelStatuses.InTransit, QrLabelStatuses.Available, view.Transfer.TransferCode,
                    "Quét nhận kho điều chuyển nội bộ", now));
            }
        }
        var from = view.Transfer.Status;
        view.Transfer.Status = InternalTransferStatuses.Received;
        view.Transfer.ReceivedAt = now;
        view.Transfer.UpdatedAt = now;
        view.Transfer.Version++;
        store.AddTransferEvent(CreateEvent(view.Transfer, userId, operationKey, InternalTransferActions.Received,
            from, view.Transfer.Status, "Kho đích đã nhận đủ hàng điều chuyển", now));
        await store.SaveChangesAsync(cancellationToken);
        return ToResult((await store.FindTransferAsync(id, false, cancellationToken))!);
    }

    private async Task<InternalTransferResult> ChangeDecisionAsync(Guid id, int expectedVersion,
        string operationKey, Guid userId, bool approve, string? reason, CancellationToken cancellationToken)
    {
        ValidateOperationKey(operationKey);
        var action = approve ? InternalTransferActions.Approved : InternalTransferActions.Rejected;
        var replay = await ReplayAsync(operationKey, action, id, cancellationToken);
        if (replay is not null)
        {
            if (!approve && replay.Transfer.RejectionReason != NormalizeOptional(reason))
                throw InternalTransferOperationException.Conflict("Operation key was already used with a different rejection request.");
            return ToResult(replay);
        }
        if (!approve && string.IsNullOrWhiteSpace(reason))
            throw InternalTransferOperationException.Validation("Rejection reason is required.");
        var view = await GetForUpdateAsync(id, expectedVersion, InternalTransferStatuses.Requested, cancellationToken);
        if (approve)
            foreach (var item in view.Items)
            {
                var inventory = await store.FindInventoryForUpdateAsync(item.Item.SourceBatchId, cancellationToken)
                    ?? throw InternalTransferOperationException.NotFound("Source inventory was not found.");
                if (inventory.Balance.AvailableQuantity < item.Item.RequestedQuantity)
                    throw InternalTransferOperationException.Conflict("Source inventory is no longer sufficient for approval.");
                item.Item.Status = InternalTransferStatuses.Approved;
                item.Item.UpdatedAt = timeProvider.GetUtcNow();
            }
        var now = timeProvider.GetUtcNow();
        var from = view.Transfer.Status;
        view.Transfer.Status = approve ? InternalTransferStatuses.Approved : InternalTransferStatuses.Rejected;
        view.Transfer.ApprovedByUserId = approve ? userId : null;
        view.Transfer.ApprovedAt = approve ? now : null;
        view.Transfer.RejectionReason = approve ? null : reason!.Trim();
        view.Transfer.Version++;
        view.Transfer.UpdatedAt = now;
        if (!approve)
            foreach (var item in view.Items) { item.Item.Status = InternalTransferStatuses.Rejected; item.Item.UpdatedAt = now; }
        store.AddTransferEvent(CreateEvent(view.Transfer, userId, operationKey, action, from,
            view.Transfer.Status, approve ? "Phê duyệt phiếu điều chuyển" : reason!.Trim(), now));
        await store.SaveChangesAsync(cancellationToken);
        return ToResult((await store.FindTransferAsync(id, false, cancellationToken))!);
    }

    private async Task<TransferView> GetForUpdateAsync(Guid id, int version, string status, CancellationToken cancellationToken)
    {
        var view = await store.FindTransferAsync(id, true, cancellationToken)
            ?? throw InternalTransferOperationException.NotFound("Internal transfer was not found.");
        if (view.Transfer.Version != version)
            throw InternalTransferOperationException.Conflict("Internal transfer was changed by another operation. Refresh and retry.");
        if (view.Transfer.Status != status)
            throw InternalTransferOperationException.Conflict($"Internal transfer must be in {status} status.");
        return view;
    }

    private async Task ValidateFefoSelectionAsync(Guid sourceWarehouseId,
        IReadOnlyCollection<(CreateInternalTransferLineData Line, TransferInventoryView Inventory)> requested,
        CancellationToken cancellationToken)
    {
        var positions = await store.ListSuggestionInventoryAsync(cancellationToken);
        foreach (var productGroup in requested.GroupBy(item => item.Inventory.Batch.ProductId))
        {
            var requestedByBatch = productGroup.ToDictionary(item => item.Inventory.Batch.Id, item => item.Line.Quantity);
            var remaining = productGroup.Sum(item => item.Line.Quantity);
            foreach (var position in positions.Where(item => item.Warehouse.Id == sourceWarehouseId &&
                         item.Product.Id == productGroup.Key && item.Balance.AvailableQuantity > 0 &&
                         item.Batch.ExpirationDate >= DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime))
                     .OrderBy(item => item.Batch.ExpirationDate).ThenBy(item => item.Batch.BatchCode, StringComparer.Ordinal))
            {
                if (remaining <= 0) break;
                var expected = Math.Min(remaining, position.Balance.AvailableQuantity);
                if (!requestedByBatch.TryGetValue(position.Batch.Id, out var actual) || actual != expected)
                    throw InternalTransferOperationException.Conflict("Transfer lines must select available source batches in FEFO order.");
                remaining -= expected;
            }
            if (remaining > 0)
                throw InternalTransferOperationException.Conflict("Source inventory is insufficient for FEFO transfer selection.");
        }
    }

    private async Task<TransferView?> ReplayAsync(string key, string action, Guid? transferId, CancellationToken cancellationToken)
    {
        var existing = await store.FindEventAsync(key.Trim(), cancellationToken);
        if (existing is null) return null;
        if (existing.Action != action || transferId.HasValue && existing.WarehouseTransferId != transferId.Value)
            throw InternalTransferOperationException.Conflict("Operation key was already used for a different internal transfer action.");
        return await store.FindTransferAsync(existing.WarehouseTransferId, false, cancellationToken)
            ?? throw InternalTransferOperationException.Conflict("The prior internal transfer operation is incomplete.");
    }

    private async Task EnsureWarehouseAccessAsync(Guid userId, string role, Guid warehouseId, CancellationToken cancellationToken)
    {
        if (!await store.CanAccessWarehouseAsync(userId, role, warehouseId, cancellationToken))
            throw InternalTransferOperationException.Forbidden("You are not assigned to this warehouse.");
    }

    private async Task<bool> CanAccessEitherWarehouseAsync(Guid userId, string role, WarehouseTransfer transfer, CancellationToken cancellationToken) =>
        await store.CanAccessWarehouseAsync(userId, role, transfer.SourceWarehouseId, cancellationToken) ||
        await store.CanAccessWarehouseAsync(userId, role, transfer.DestinationWarehouseId, cancellationToken);

    private static void ApplySourceDispatch(TransferInventoryView inventory, decimal quantity, DateTimeOffset now)
    {
        inventory.Batch.Quantity -= quantity;
        inventory.Batch.StockStatus = inventory.Batch.Quantity == 0 ? BatchStockStatuses.Depleted : BatchStockStatuses.InStock;
        inventory.Batch.Version++;
        inventory.Batch.UpdatedAt = now;
        inventory.Balance.PhysicalQuantity -= quantity;
        inventory.Balance.AvailableQuantity = inventory.Balance.PhysicalQuantity - inventory.Balance.ReservedQuantity;
        inventory.Balance.Version++;
        inventory.Balance.UpdatedAt = now;
    }

    private static InventoryTransaction CreateInventoryTransaction(TransferInventoryView inventory, Guid userId,
        Guid sourceId, string reference, decimal delta, string type, string note, DateTimeOffset now) =>
        CreateInventoryTransaction(inventory.Batch, inventory.Balance, userId, sourceId, reference, delta, type, note, now);

    private static InventoryTransaction CreateInventoryTransaction(Batch batch, InventoryBalance balance, Guid userId,
        Guid sourceId, string reference, decimal delta, string type, string note, DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            BatchId = batch.Id,
            WarehouseId = batch.WarehouseId,
            ProductId = batch.ProductId,
            CreatedByUserId = userId,
            TransactionType = type,
            SourceType = InventoryTransactionSourceTypes.InternalTransfer,
            SourceId = sourceId,
            ReferenceCode = reference,
            PhysicalQuantityDelta = delta,
            PhysicalQuantityAfter = balance.PhysicalQuantity,
            ReservedQuantityAfter = balance.ReservedQuantity,
            AvailableQuantityAfter = balance.AvailableQuantity,
            Note = note,
            OccurredAt = now
        };

    private static WarehouseTransferEvent CreateEvent(WarehouseTransfer transfer, Guid userId, string key,
        string action, string from, string to, string? note, DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            WarehouseTransferId = transfer.Id,
            PerformedByUserId = userId,
            OperationKey = key.Trim(),
            Action = action,
            FromStatus = from,
            ToStatus = to,
            Note = note,
            OccurredAt = now
        };

    private static QrLabelEvent CreateQrEvent(BatchQrLabel label, Guid warehouseId, Guid userId,
        string key, string type, string from, string to, string reference, string note, DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            BatchQrLabelId = label.Id,
            WarehouseId = warehouseId,
            PerformedByUserId = userId,
            OperationKey = key,
            EventType = type,
            FromStatus = from,
            ToStatus = to,
            Quantity = label.Quantity,
            ReferenceCode = reference,
            Note = note,
            OccurredAt = now
        };

    private static InternalTransferResult ToResult(TransferView view) => new(
        view.Transfer.Id, view.Transfer.TransferCode, view.Transfer.SourceWarehouseId, view.SourceWarehouse.Code,
        view.SourceWarehouse.Name, view.Transfer.DestinationWarehouseId, view.DestinationWarehouse.Code,
        view.DestinationWarehouse.Name, view.Transfer.CreatedByUserId, view.Transfer.ApprovedByUserId,
        view.Transfer.Status, view.Transfer.Note, view.Transfer.RejectionReason, view.Transfer.Version,
        view.Transfer.RequestedAt, view.Transfer.ApprovedAt, view.Transfer.DispatchedAt, view.Transfer.ReceivedAt,
        view.Items.Select(item => new InternalTransferItemResult(item.Item.Id, item.Item.SourceBatchId,
            item.SourceBatch.BatchCode, item.Item.DestinationBatchId, item.DestinationBatchCode,
            item.Product.Id, item.Product.Sku, item.Product.Name, item.SourceBatch.ExpirationDate,
            item.Item.RequestedQuantity, item.Item.DispatchedQuantity, item.Item.ReceivedQuantity,
            item.Item.Status, item.Item.Note, item.Labels.Select(label => new InternalTransferLabelResult(
                label.Label.Id, label.Label.QrCode, label.Link.Quantity, label.Link.DispatchedAt,
                label.Link.ReceivedAt)).ToArray())).ToArray(),
        view.Events.Select(item => new InternalTransferEventResult(item.Id, item.OperationKey, item.Action,
            item.FromStatus, item.ToStatus, item.PerformedByUserId, item.Note, item.OccurredAt)).ToArray());

    private static string NormalizeQrCode(string code) => string.IsNullOrWhiteSpace(code)
        ? throw InternalTransferOperationException.Validation("QR code is required.") : code.Trim().ToUpperInvariant();
    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void ValidateOperationKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Trim().Length > 70)
            throw InternalTransferOperationException.Validation("Idempotency-Key is required and must not exceed 70 characters.");
    }
}
