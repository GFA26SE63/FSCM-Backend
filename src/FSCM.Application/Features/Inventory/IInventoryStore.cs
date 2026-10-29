using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Inventory;

public sealed record GoodsReceiptView(
    GoodsReceipt Receipt, string WarehouseCode, string WarehouseName,
    IReadOnlyCollection<GoodsReceiptLineView> Lines);
public sealed record GoodsReceiptLineView(
    GoodsReceiptLine Line, string Sku, string ProductName, Guid? BatchId, string? BatchCode);
public sealed record GoodsReceiptApprovalLine(GoodsReceiptLine Line, Product Product);
public sealed record GoodsReceiptApprovalView(
    GoodsReceipt Receipt, Warehouse Warehouse, IReadOnlyCollection<GoodsReceiptApprovalLine> Lines);
public sealed record BatchInventoryView(
    Batch Batch, InventoryBalance Balance, string WarehouseCode, string WarehouseName,
    string Sku, string ProductName, int NearDateThresholdHours,
    decimal ActiveLockedQuantity, bool HasLockRequiringResolution);
public sealed record InventoryHistoryView(
    InventoryTransaction Transaction, string BatchCode, string WarehouseCode, string Sku);

public interface IInventoryStore
{
    Task<bool> CanAccessWarehouseAsync(Guid userId, string role, Guid warehouseId, CancellationToken cancellationToken);
    Task<Warehouse?> FindWarehouseAsync(Guid warehouseId, CancellationToken cancellationToken);
    Task<Product?> FindProductAsync(Guid productId, CancellationToken cancellationToken);
    Task<GoodsReceiptView?> FindReceiptByIdempotencyKeyAsync(string key, CancellationToken cancellationToken);
    Task<GoodsReceiptView?> FindReceiptAsync(Guid receiptId, CancellationToken cancellationToken);
    Task<GoodsReceiptApprovalView?> FindReceiptForApprovalAsync(Guid receiptId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<GoodsReceiptView>> ListReceiptsAsync(Guid actorUserId, string actorRole, Guid? warehouseId, CancellationToken cancellationToken);
    Task<Batch?> FindBatchForUpdateAsync(Guid warehouseId, Guid productId, DateOnly productionDate,
        DateOnly expirationDate, CancellationToken cancellationToken);
    Task<InventoryBalance?> FindBalanceForUpdateAsync(Guid batchId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<BatchInventoryView>> ListInventoryAsync(Guid actorUserId, string actorRole, Guid? warehouseId, Guid? productId, CancellationToken cancellationToken);
    Task<BatchInventoryView?> FindBatchInventoryAsync(Guid batchId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<InventoryHistoryView>> ListHistoryAsync(Guid actorUserId, string actorRole, Guid? warehouseId, Guid? batchId, CancellationToken cancellationToken);
    Task<ExpirySweepRun?> FindLatestExpirySweepRunAsync(CancellationToken cancellationToken);
    Task<int> CountOutboxMessagesAsync(string eventType, string status, CancellationToken cancellationToken);
    Task<string> NextReceiptCodeAsync(DateOnly businessDate, CancellationToken cancellationToken);
    void AddReceipt(GoodsReceipt receipt);
    void AddReceiptLine(GoodsReceiptLine line);
    void AddBatch(Batch batch);
    void AddBalance(InventoryBalance balance);
    void AddTransaction(InventoryTransaction transaction);
    void ClearTracking();
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
