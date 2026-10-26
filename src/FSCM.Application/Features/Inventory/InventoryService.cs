using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Inventory;

public sealed class InventoryService(IInventoryStore store, TimeProvider timeProvider)
{
    private static readonly TimeZoneInfo VietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    public async Task<GoodsReceiptResult> CreateReceiptAsync(
        Guid actorUserId, string actorRole, Guid warehouseId, string idempotencyKey,
        IReadOnlyCollection<ReceiveGoodsLineData> lines, CancellationToken cancellationToken)
    {
        idempotencyKey = ValidateIdempotencyKey(idempotencyKey);
        if (lines.Count is < 1 or > 100)
            throw InventoryOperationException.Validation("A goods receipt must contain between 1 and 100 lines.");

        if (!await store.CanAccessWarehouseAsync(actorUserId, actorRole, warehouseId, cancellationToken))
            throw InventoryOperationException.Forbidden("User cannot create a goods receipt for this warehouse.");

        var requestHash = ComputeRequestHash(actorUserId, warehouseId, lines);
        var existing = await store.FindReceiptByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.Receipt.RequestHash != requestHash)
                throw InventoryOperationException.Conflict("Idempotency key was already used for a different goods receipt.");
            return ToReceiptResult(existing);
        }

        var warehouse = await store.FindWarehouseAsync(warehouseId, cancellationToken)
            ?? throw InventoryOperationException.NotFound("Warehouse was not found.");
        if (!warehouse.IsActive) throw InventoryOperationException.Conflict("Warehouse is inactive.");

        var now = timeProvider.GetUtcNow();
        var businessDate = GetVietnamDate(now);
        var validatedLines = new List<(ReceiveGoodsLineData Input, Product Product)>();
        foreach (var line in lines)
        {
            ValidateLine(line, businessDate);
            var product = await store.FindProductAsync(line.ProductId, cancellationToken)
                ?? throw InventoryOperationException.NotFound($"Product '{line.ProductId}' was not found.");
            if (product.Status != "Published")
                throw InventoryOperationException.Conflict($"Product '{product.Sku}' is not published.");
            validatedLines.Add((line with { SourceLotCode = CleanLotCode(line.SourceLotCode) }, product));
        }

        var receipt = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            ReceiptCode = await store.NextReceiptCodeAsync(businessDate, cancellationToken),
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
            WarehouseId = warehouseId,
            CreatedByUserId = actorUserId,
            Status = GoodsReceiptStatuses.Pending,
            Version = 0,
            CreatedAt = now,
            UpdatedAt = now
        };
        store.AddReceipt(receipt);

        foreach (var item in validatedLines)
        {
            var input = item.Input;
            var line = new GoodsReceiptLine
            {
                Id = Guid.NewGuid(),
                GoodsReceiptId = receipt.Id,
                ProductId = input.ProductId,
                SourceLotCode = input.SourceLotCode,
                ReceivedQuantity = input.Quantity,
                ProductionDate = input.ProductionDate,
                ExpirationDate = input.ExpirationDate,
                CreatedAt = now
            };
            store.AddReceiptLine(line);
        }

        try
        {
            await store.SaveChangesAsync(cancellationToken);
        }
        catch (InventoryOperationException exception) when (exception.Code == "conflict")
        {
            store.ClearTracking();
            var concurrentlyCreated = await store.FindReceiptByIdempotencyKeyAsync(
                idempotencyKey, cancellationToken);
            if (concurrentlyCreated is not null && concurrentlyCreated.Receipt.RequestHash == requestHash)
                return ToReceiptResult(concurrentlyCreated);
            throw;
        }
        return ToReceiptResult(await store.FindReceiptAsync(receipt.Id, cancellationToken)
            ?? throw InventoryOperationException.Conflict("Created goods receipt could not be reloaded."));
    }

    public async Task<GoodsReceiptResult> ApproveReceiptAsync(
        Guid receiptId, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        var approval = await store.FindReceiptForApprovalAsync(receiptId, cancellationToken)
            ?? throw InventoryOperationException.NotFound("Goods receipt was not found.");
        if (!await store.CanAccessWarehouseAsync(
                actorUserId, actorRole, approval.Receipt.WarehouseId, cancellationToken))
            throw InventoryOperationException.Forbidden("User cannot approve this goods receipt.");
        if (approval.Receipt.Status == GoodsReceiptStatuses.Received)
            return ToReceiptResult(await store.FindReceiptAsync(receiptId, cancellationToken)
                ?? throw InventoryOperationException.Conflict("Approved goods receipt could not be reloaded."));
        if (approval.Receipt.Status != GoodsReceiptStatuses.Pending)
            throw InventoryOperationException.Conflict("Only a pending goods receipt can be approved.");
        if (!approval.Warehouse.IsActive)
            throw InventoryOperationException.Conflict("Warehouse is inactive.");

        var now = timeProvider.GetUtcNow();
        var businessDate = GetVietnamDate(now);
        var batches = new Dictionary<(Guid ProductId, DateOnly ProductionDate, DateOnly ExpirationDate),
            (Batch Batch, InventoryBalance Balance)>();
        foreach (var item in approval.Lines)
        {
            var input = new ReceiveGoodsLineData(item.Line.ProductId, item.Line.SourceLotCode,
                item.Line.ReceivedQuantity, item.Line.ProductionDate, item.Line.ExpirationDate);
            ValidateLine(input, businessDate);
            if (item.Product.Status != "Published")
                throw InventoryOperationException.Conflict($"Product '{item.Product.Sku}' is not published.");

            var key = (item.Line.ProductId, item.Line.ProductionDate, item.Line.ExpirationDate);
            if (!batches.TryGetValue(key, out var stock))
            {
                var batch = await store.FindBatchForUpdateAsync(approval.Receipt.WarehouseId,
                    item.Line.ProductId, item.Line.ProductionDate, item.Line.ExpirationDate, cancellationToken);
                InventoryBalance balance;
                if (batch is null)
                {
                    var batchId = Guid.NewGuid();
                    batch = new Batch
                    {
                        Id = batchId,
                        RootBatchId = batchId,
                        WarehouseId = approval.Receipt.WarehouseId,
                        ProductId = item.Line.ProductId,
                        BatchCode = BuildBatchCode(approval.Warehouse.Code, item.Product.Sku,
                            item.Line.ProductionDate, item.Line.ExpirationDate),
                        Quantity = 0,
                        ProductionDate = item.Line.ProductionDate,
                        ExpirationDate = item.Line.ExpirationDate,
                        ExpiryStatus = CalculateExpiryStatus(item.Line.ExpirationDate,
                            item.Product.NearDateThresholdHours, businessDate),
                        StockStatus = BatchStockStatuses.InStock,
                        Version = 0,
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    balance = new InventoryBalance
                    {
                        Id = Guid.NewGuid(),
                        BatchId = batchId,
                        Version = 0,
                        UpdatedAt = now
                    };
                    store.AddBatch(batch);
                    store.AddBalance(balance);
                }
                else
                {
                    balance = await store.FindBalanceForUpdateAsync(batch.Id, cancellationToken)
                        ?? throw InventoryOperationException.Conflict("Batch inventory balance is missing.");
                }
                stock = (batch, balance);
                batches.Add(key, stock);
            }

            if (stock.Batch.GoodsReceiptLineId is null) stock.Batch.GoodsReceiptLineId = item.Line.Id;
            stock.Batch.Quantity += item.Line.ReceivedQuantity;
            stock.Batch.StockStatus = BatchStockStatuses.InStock;
            stock.Batch.Version++;
            stock.Batch.UpdatedAt = now;
            stock.Balance.PhysicalQuantity += item.Line.ReceivedQuantity;
            stock.Balance.AvailableQuantity = stock.Balance.PhysicalQuantity - stock.Balance.ReservedQuantity;
            EnsureNonNegative(stock.Balance);
            stock.Balance.Version++;
            stock.Balance.UpdatedAt = now;
            store.AddTransaction(new InventoryTransaction
            {
                Id = Guid.NewGuid(),
                BatchId = stock.Batch.Id,
                WarehouseId = approval.Receipt.WarehouseId,
                ProductId = item.Line.ProductId,
                GoodsReceiptId = approval.Receipt.Id,
                GoodsReceiptLineId = item.Line.Id,
                CreatedByUserId = actorUserId,
                TransactionType = InventoryTransactionTypes.GoodsReceipt,
                SourceType = InventoryTransactionSourceTypes.GoodsReceipt,
                SourceId = approval.Receipt.Id,
                ReferenceCode = approval.Receipt.ReceiptCode,
                PhysicalQuantityDelta = item.Line.ReceivedQuantity,
                PhysicalQuantityAfter = stock.Balance.PhysicalQuantity,
                ReservedQuantityAfter = stock.Balance.ReservedQuantity,
                AvailableQuantityAfter = stock.Balance.AvailableQuantity,
                Note = "Duyệt phiếu nhập kho",
                OccurredAt = now
            });
        }

        approval.Receipt.Status = GoodsReceiptStatuses.Received;
        approval.Receipt.ApprovedByUserId = actorUserId;
        approval.Receipt.ApprovedAt = now;
        approval.Receipt.ReceivedAt = now;
        approval.Receipt.Version++;
        approval.Receipt.UpdatedAt = now;
        try
        {
            await store.SaveChangesAsync(cancellationToken);
        }
        catch (InventoryOperationException exception) when (exception.Code == "conflict")
        {
            store.ClearTracking();
            var concurrentlyApproved = await store.FindReceiptAsync(receiptId, cancellationToken);
            if (concurrentlyApproved?.Receipt.Status == GoodsReceiptStatuses.Received)
                return ToReceiptResult(concurrentlyApproved);
            throw;
        }

        return ToReceiptResult(await store.FindReceiptAsync(receiptId, cancellationToken)
            ?? throw InventoryOperationException.Conflict("Approved goods receipt could not be reloaded."));
    }

    public async Task<IReadOnlyCollection<GoodsReceiptResult>> ListReceiptsAsync(
        Guid actorUserId, string actorRole, Guid? warehouseId, CancellationToken cancellationToken) =>
        (await store.ListReceiptsAsync(actorUserId, actorRole, warehouseId, cancellationToken))
        .Select(ToReceiptResult).ToArray();

    public async Task<GoodsReceiptResult> GetReceiptAsync(
        Guid receiptId, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        var view = await store.FindReceiptAsync(receiptId, cancellationToken)
            ?? throw InventoryOperationException.NotFound("Goods receipt was not found.");
        await EnsureWarehouseAccessAsync(actorUserId, actorRole, view.Receipt.WarehouseId, cancellationToken);
        return ToReceiptResult(view);
    }

    public async Task<IReadOnlyCollection<BatchInventoryResult>> ListInventoryAsync(
        Guid actorUserId, string actorRole, Guid? warehouseId, Guid? productId, bool includeExpired,
        CancellationToken cancellationToken)
    {
        var today = GetVietnamDate(timeProvider.GetUtcNow());
        var results = (await store.ListInventoryAsync(actorUserId, actorRole, warehouseId, productId, cancellationToken))
            .Select(item => ToInventoryResult(item, today));
        if (!includeExpired) results = results.Where(item => item.ExpiryStatus != BatchExpiryStatuses.Expired);
        return results.OrderBy(item => item.ExpirationDate).ThenBy(item => item.BatchCode).ToArray();
    }

    public async Task<BatchInventoryResult> GetBatchAsync(
        Guid batchId, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        var view = await store.FindBatchInventoryAsync(batchId, cancellationToken)
            ?? throw InventoryOperationException.NotFound("Batch was not found.");
        await EnsureWarehouseAccessAsync(actorUserId, actorRole, view.Batch.WarehouseId, cancellationToken);
        return ToInventoryResult(view, GetVietnamDate(timeProvider.GetUtcNow()));
    }

    public async Task<IReadOnlyCollection<InventoryHistoryResult>> ListHistoryAsync(
        Guid actorUserId, string actorRole, Guid? warehouseId, Guid? batchId, CancellationToken cancellationToken) =>
        (await store.ListHistoryAsync(actorUserId, actorRole, warehouseId, batchId, cancellationToken))
        .Select(ToHistoryResult).ToArray();

    public async Task<InventoryExpiryHealthResult> GetExpiryHealthAsync(
        bool schedulerEnabled, string scheduleTimeZone, string scheduleLocalTime,
        CancellationToken cancellationToken)
    {
        var latest = await store.FindLatestExpirySweepRunAsync(cancellationToken);
        var pending = await store.CountOutboxMessagesAsync(
            InventoryOutboxEventTypes.BatchExpired, OutboxMessageStatuses.Pending, cancellationToken);
        var failed = await store.CountOutboxMessagesAsync(
            InventoryOutboxEventTypes.BatchExpired, OutboxMessageStatuses.Failed, cancellationToken);
        return new InventoryExpiryHealthResult(
            scheduleTimeZone, scheduleLocalTime, schedulerEnabled,
            latest is null ? null : ToExpirySweepResult(latest), pending, failed);
    }

    private async Task EnsureWarehouseAccessAsync(Guid userId, string role, Guid warehouseId, CancellationToken cancellationToken)
    {
        if (!await store.CanAccessWarehouseAsync(userId, role, warehouseId, cancellationToken))
            throw InventoryOperationException.Forbidden("User cannot access this warehouse.");
    }

    private static void ValidateLine(ReceiveGoodsLineData line, DateOnly businessDate)
    {
        if (line.ProductId == Guid.Empty) throw InventoryOperationException.Validation("Product identifier is required.");
        if (line.Quantity <= 0 || decimal.Truncate(line.Quantity) != line.Quantity)
            throw InventoryOperationException.Validation("Received quantity must be a positive whole number of cartons.");
        if (line.ProductionDate > businessDate)
            throw InventoryOperationException.Validation("Production date cannot be in the future.");
        if (line.ProductionDate >= line.ExpirationDate)
            throw InventoryOperationException.Validation("Production date must be before expiration date.");
        if (line.ExpirationDate <= businessDate)
            throw InventoryOperationException.Validation("Expired goods or goods expiring today cannot be received.");
    }

    private static string ValidateIdempotencyKey(string value)
    {
        value = value.Trim();
        if (value.Length is < 8 or > 150)
            throw InventoryOperationException.Validation("Idempotency-Key must contain 8 to 150 characters.");
        return value;
    }

    private static string? CleanLotCode(string? value)
    {
        value = string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
        if (value?.Length > 80) throw InventoryOperationException.Validation("Source lot code cannot exceed 80 characters.");
        return value;
    }

    private static void EnsureNonNegative(InventoryBalance balance)
    {
        if (balance.PhysicalQuantity < 0 || balance.ReservedQuantity < 0 || balance.AvailableQuantity < 0)
            throw InventoryOperationException.Conflict("Inventory quantities cannot become negative.");
    }

    private static string BuildBatchCode(
        string warehouseCode, string sku, DateOnly productionDate, DateOnly expirationDate) =>
        $"{warehouseCode}-{sku}-{productionDate:ddMMyyyy}-{expirationDate:ddMMyyyy}";

    private static string CalculateExpiryStatus(DateOnly expirationDate, int nearDateThresholdHours, DateOnly today)
    {
        if (expirationDate < today) return BatchExpiryStatuses.Expired;
        var nearDate = today.AddDays((int)Math.Ceiling(nearDateThresholdHours / 24m));
        return expirationDate <= nearDate ? BatchExpiryStatuses.NearExpiry : BatchExpiryStatuses.Valid;
    }

    private static DateOnly GetVietnamDate(DateTimeOffset utcNow) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, VietnamTimeZone).DateTime);

    private static string ComputeRequestHash(Guid userId, Guid warehouseId, IEnumerable<ReceiveGoodsLineData> lines)
    {
        var canonicalLines = lines.OrderBy(item => item.ProductId).ThenBy(item => item.ExpirationDate)
            .ThenBy(item => item.SourceLotCode, StringComparer.Ordinal)
            .Select(item => string.Join('|', item.ProductId,
                item.ProductionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                item.ExpirationDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                item.Quantity.ToString(CultureInfo.InvariantCulture), item.SourceLotCode?.Trim().ToUpperInvariant()));
        var canonical = $"{userId}|{warehouseId}|{string.Join(';', canonicalLines)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static GoodsReceiptResult ToReceiptResult(GoodsReceiptView view) => new(
        view.Receipt.Id, view.Receipt.ReceiptCode, view.Receipt.WarehouseId, view.WarehouseCode,
        view.WarehouseName, view.Receipt.CreatedByUserId, view.Receipt.ApprovedByUserId,
        view.Receipt.Status, view.Receipt.ReceivedAt, view.Receipt.ApprovedAt,
        view.Receipt.Version, view.Receipt.CreatedAt,
        view.Lines.Select(item => new GoodsReceiptLineResult(item.Line.Id, item.Line.ProductId,
            item.Sku, item.ProductName, item.Line.SourceLotCode, item.Line.ReceivedQuantity,
            item.Line.ProductionDate, item.Line.ExpirationDate, item.BatchId, item.BatchCode)).ToArray());

    private static BatchInventoryResult ToInventoryResult(BatchInventoryView view, DateOnly today)
    {
        var expiryStatus = CalculateExpiryStatus(view.Batch.ExpirationDate, view.NearDateThresholdHours, today);
        // F41: issuable = on-hand − reserved − active locked, and nothing issuable once expired.
        // Lock only withholds the not-yet-reserved portion, so cap it at the free (unreserved) stock.
        var freeUnreserved = Math.Max(0, view.Balance.PhysicalQuantity - view.Balance.ReservedQuantity);
        var lockedAgainstFree = Math.Min(view.ActiveLockedQuantity, freeUnreserved);
        var available = expiryStatus == BatchExpiryStatuses.Expired ? 0 : Math.Max(0, freeUnreserved - lockedAgainstFree);
        return new BatchInventoryResult(
            view.Batch.Id, view.Batch.BatchCode, view.Batch.WarehouseId, view.WarehouseCode, view.WarehouseName,
            view.Batch.ProductId, view.Sku, view.ProductName, view.Batch.ExpirationDate, expiryStatus,
            view.Balance.PhysicalQuantity == 0 ? BatchStockStatuses.Depleted : BatchStockStatuses.InStock,
            view.Balance.PhysicalQuantity, view.Balance.ReservedQuantity, view.ActiveLockedQuantity,
            available, view.HasLockRequiringResolution, view.Balance.Version, view.Balance.UpdatedAt);
    }

    private static InventoryHistoryResult ToHistoryResult(InventoryHistoryView view) => new(
        view.Transaction.Id, view.Transaction.BatchId, view.BatchCode, view.WarehouseCode, view.Sku,
        view.Transaction.TransactionType, view.Transaction.SourceType, view.Transaction.SourceId,
        view.Transaction.ReferenceCode, view.Transaction.PhysicalQuantityDelta,
        view.Transaction.ReservedQuantityDelta, view.Transaction.PhysicalQuantityAfter,
        view.Transaction.ReservedQuantityAfter,
        view.Transaction.AvailableQuantityAfter, view.Transaction.Note,
        view.Transaction.CreatedByUserId, view.Transaction.OccurredAt);

    private static ExpirySweepRunResult ToExpirySweepResult(ExpirySweepRun run) => new(
        run.Id, run.BusinessDate, run.Status, run.BatchesScanned, run.StatusesUpdated,
        run.BatchesExpired, run.Error, run.StartedAt, run.CompletedAt);
}
