using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Inventory;

public sealed class InventoryService(IInventoryStore store, TimeProvider timeProvider)
{
    private static readonly TimeZoneInfo VietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    public async Task<GoodsReceiptResult> ReceiveAsync(
        Guid actorUserId, string actorRole, Guid warehouseId, string idempotencyKey,
        IReadOnlyCollection<ReceiveGoodsLineData> lines, CancellationToken cancellationToken)
    {
        idempotencyKey = ValidateIdempotencyKey(idempotencyKey);
        if (lines.Count is < 1 or > 100)
            throw InventoryOperationException.Validation("A goods receipt must contain between 1 and 100 lines.");

        var requestHash = ComputeRequestHash(actorUserId, warehouseId, lines);
        var existing = await store.FindReceiptByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.Receipt.RequestHash != requestHash)
                throw InventoryOperationException.Conflict("Idempotency key was already used for a different goods receipt.");
            return ToReceiptResult(existing);
        }

        if (!await store.CanAccessWarehouseAsync(actorUserId, actorRole, warehouseId, cancellationToken))
            throw InventoryOperationException.Forbidden("Warehouse Keeper can only receive goods into an assigned warehouse.");
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
            Status = GoodsReceiptStatuses.Received,
            ReceivedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        store.AddReceipt(receipt);

        var batches = new Dictionary<(Guid ProductId, DateOnly ExpirationDate), (Batch Batch, InventoryBalance Balance)>();
        foreach (var item in validatedLines)
        {
            var input = item.Input;
            var key = (input.ProductId, input.ExpirationDate);
            if (!batches.TryGetValue(key, out var stock))
            {
                var batch = await store.FindBatchForUpdateAsync(warehouseId, input.ProductId, input.ExpirationDate, cancellationToken);
                InventoryBalance balance;
                if (batch is null)
                {
                    var batchId = Guid.NewGuid();
                    batch = new Batch
                    {
                        Id = batchId,
                        RootBatchId = batchId,
                        WarehouseId = warehouseId,
                        ProductId = input.ProductId,
                        BatchCode = BuildBatchCode(warehouse.Code, item.Product.Sku, input.ExpirationDate),
                        Quantity = 0,
                        ExpirationDate = input.ExpirationDate,
                        ExpiryStatus = CalculateExpiryStatus(input.ExpirationDate, item.Product.NearDateThresholdHours, businessDate),
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
            if (stock.Batch.GoodsReceiptLineId is null) stock.Batch.GoodsReceiptLineId = line.Id;

            stock.Batch.Quantity += input.Quantity;
            stock.Batch.StockStatus = BatchStockStatuses.InStock;
            stock.Batch.Version++;
            stock.Batch.UpdatedAt = now;
            stock.Balance.PhysicalQuantity += input.Quantity;
            stock.Balance.AvailableQuantity = stock.Balance.PhysicalQuantity - stock.Balance.HeldQuantity - stock.Balance.ReservedQuantity;
            EnsureNonNegative(stock.Balance);
            stock.Balance.Version++;
            stock.Balance.UpdatedAt = now;
            store.AddTransaction(new InventoryTransaction
            {
                Id = Guid.NewGuid(),
                BatchId = stock.Batch.Id,
                WarehouseId = warehouseId,
                ProductId = input.ProductId,
                GoodsReceiptId = receipt.Id,
                GoodsReceiptLineId = line.Id,
                CreatedByUserId = actorUserId,
                TransactionType = InventoryTransactionTypes.GoodsReceipt,
                ReferenceCode = receipt.ReceiptCode,
                PhysicalQuantityDelta = input.Quantity,
                PhysicalQuantityAfter = stock.Balance.PhysicalQuantity,
                HeldQuantityAfter = stock.Balance.HeldQuantity,
                ReservedQuantityAfter = stock.Balance.ReservedQuantity,
                AvailableQuantityAfter = stock.Balance.AvailableQuantity,
                Note = "Nhập hàng vào kho",
                OccurredAt = now
            });
        }

        await store.SaveChangesAsync(cancellationToken);
        return ToReceiptResult(await store.FindReceiptAsync(receipt.Id, cancellationToken)
            ?? throw InventoryOperationException.Conflict("Created goods receipt could not be reloaded."));
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
        if (line.ExpirationDate <= businessDate)
            throw InventoryOperationException.Validation("Expired goods or goods expiring today cannot be received.");
        if (line.ExpirationDate <= line.ProductionDate)
            throw InventoryOperationException.Validation("Expiration date must be after the production date.");
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
        if (balance.PhysicalQuantity < 0 || balance.HeldQuantity < 0 || balance.ReservedQuantity < 0 || balance.AvailableQuantity < 0)
            throw InventoryOperationException.Conflict("Inventory quantities cannot become negative.");
    }

    private static string BuildBatchCode(string warehouseCode, string sku, DateOnly expirationDate) =>
        $"{warehouseCode}-{sku}-{expirationDate:ddMMyyyy}";

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
            .Select(item => string.Join('|', item.ProductId, item.ExpirationDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                item.ProductionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                item.Quantity.ToString(CultureInfo.InvariantCulture), item.SourceLotCode?.Trim().ToUpperInvariant()));
        var canonical = $"{userId}|{warehouseId}|{string.Join(';', canonicalLines)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static GoodsReceiptResult ToReceiptResult(GoodsReceiptView view) => new(
        view.Receipt.Id, view.Receipt.ReceiptCode, view.Receipt.WarehouseId, view.WarehouseCode,
        view.WarehouseName, view.Receipt.CreatedByUserId, view.Receipt.Status,
        view.Receipt.ReceivedAt ?? view.Receipt.CreatedAt, view.Receipt.CreatedAt,
        view.Lines.Select(item => new GoodsReceiptLineResult(item.Line.Id, item.Line.ProductId,
            item.Sku, item.ProductName, item.Line.SourceLotCode, item.Line.ReceivedQuantity,
            item.Line.ProductionDate, item.Line.ExpirationDate, item.BatchId, item.BatchCode)).ToArray());

    private static BatchInventoryResult ToInventoryResult(BatchInventoryView view, DateOnly today) => new(
        view.Batch.Id, view.Batch.BatchCode, view.Batch.WarehouseId, view.WarehouseCode, view.WarehouseName,
        view.Batch.ProductId, view.Sku, view.ProductName, view.Batch.ExpirationDate,
        CalculateExpiryStatus(view.Batch.ExpirationDate, view.NearDateThresholdHours, today),
        view.Balance.PhysicalQuantity == 0 ? BatchStockStatuses.Depleted : BatchStockStatuses.InStock,
        view.Balance.PhysicalQuantity, view.Balance.HeldQuantity, view.Balance.ReservedQuantity,
        view.Balance.AvailableQuantity, view.Balance.Version, view.Balance.UpdatedAt);

    private static InventoryHistoryResult ToHistoryResult(InventoryHistoryView view) => new(
        view.Transaction.Id, view.Transaction.BatchId, view.BatchCode, view.WarehouseCode, view.Sku,
        view.Transaction.TransactionType, view.Transaction.ReferenceCode,
        view.Transaction.PhysicalQuantityDelta, view.Transaction.HeldQuantityDelta,
        view.Transaction.ReservedQuantityDelta, view.Transaction.PhysicalQuantityAfter,
        view.Transaction.HeldQuantityAfter, view.Transaction.ReservedQuantityAfter,
        view.Transaction.AvailableQuantityAfter, view.Transaction.Note,
        view.Transaction.CreatedByUserId, view.Transaction.OccurredAt);
}
