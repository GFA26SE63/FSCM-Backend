using FSCM.Domain.Entities;

namespace FSCM.Application.Features.InternalTransfers;

public sealed record TransferInventoryView(Batch Batch, InventoryBalance Balance, Warehouse Warehouse, Product Product);
public sealed record TransferItemView(
    WarehouseTransferItem Item, Batch SourceBatch, Product Product, string? DestinationBatchCode,
    IReadOnlyCollection<TransferLabelView> Labels);
public sealed record TransferLabelView(WarehouseTransferLabel Link, BatchQrLabel Label);
public sealed record TransferView(
    WarehouseTransfer Transfer, Warehouse SourceWarehouse, Warehouse DestinationWarehouse,
    IReadOnlyCollection<TransferItemView> Items, IReadOnlyCollection<WarehouseTransferEvent> Events);

public interface IInternalTransferStore
{
    Task<bool> CanAccessWarehouseAsync(Guid userId, string role, Guid warehouseId, CancellationToken cancellationToken);
    Task<Warehouse?> FindWarehouseAsync(Guid id, CancellationToken cancellationToken);
    Task<TransferInventoryView?> FindInventoryForUpdateAsync(Guid batchId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<TransferInventoryView>> ListSuggestionInventoryAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<Warehouse>> ListActiveRegionalWarehousesAsync(CancellationToken cancellationToken);
    Task<int> GetRegionalMinimumStockAsync(CancellationToken cancellationToken);
    Task<WarehouseTransferEvent?> FindEventAsync(string operationKey, CancellationToken cancellationToken);
    Task<TransferView?> FindTransferAsync(Guid transferId, bool tracking, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<TransferView>> ListTransfersAsync(Guid userId, string role, Guid? warehouseId, string? status, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<BatchQrLabel>> FindLabelsForUpdateAsync(IReadOnlyCollection<string> qrCodes, CancellationToken cancellationToken);
    Task<Batch?> FindDestinationBatchForUpdateAsync(Guid warehouseId, Guid productId, DateOnly expirationDate, CancellationToken cancellationToken);
    Task<InventoryBalance?> FindBalanceForUpdateAsync(Guid batchId, CancellationToken cancellationToken);
    Task<string> NextTransferCodeAsync(DateOnly businessDate, CancellationToken cancellationToken);
    void AddTransfer(WarehouseTransfer transfer);
    void AddTransferItem(WarehouseTransferItem item);
    void AddTransferEvent(WarehouseTransferEvent transferEvent);
    void AddTransferLabel(WarehouseTransferLabel link);
    void AddQrLabelEvent(QrLabelEvent labelEvent);
    void AddBatch(Batch batch);
    void AddBalance(InventoryBalance balance);
    void AddInventoryTransaction(InventoryTransaction transaction);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
