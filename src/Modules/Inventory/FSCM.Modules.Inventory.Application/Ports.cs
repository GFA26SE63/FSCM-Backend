using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Inventory.Domain.Batches;
using FSCM.Modules.Inventory.Domain.Disposals;
using FSCM.Modules.Inventory.Domain.Receipts;
using FSCM.Modules.Inventory.Domain.Rotation;
using FSCM.Modules.Inventory.Domain.Transfers;
using FSCM.Modules.Inventory.Domain.Warehouses;

namespace FSCM.Modules.Inventory.Application;

public interface IInventoryUnitOfWork : IUnitOfWork, IEventOutbox
{
    /// <summary>Forget tracked entities after a failed multi-step operation (a half-applied hold must not be saved by the next call).</summary>
    void ClearTracked();
}

public interface IBatchRepository
{
    Task<Batch?> GetAsync(string code, CancellationToken ct);
    Task<IReadOnlyList<Batch>> BySkuAsync(string sku, CancellationToken ct);
    Task<IReadOnlyList<Batch>> AllActiveAsync(CancellationToken ct);
    Task<IReadOnlyList<Batch>> SearchAsync(string? warehouse, string? sku, ExpiryState? state, string? search, CancellationToken ct);
    void Add(Batch b);
}
public interface IWarehouseRepository
{
    Task<IReadOnlyList<Warehouse>> AllAsync(CancellationToken ct);
    Task<Warehouse?> GetAsync(string code, CancellationToken ct);
    void Add(Warehouse w);
}
public interface ITransferRepository
{
    void Add(Transfer t);
    Task<Transfer?> GetAsync(string code, CancellationToken ct);
    Task<Transfer?> ByLabelAsync(string labelCode, CancellationToken ct);
    Task<IReadOnlyList<Transfer>> RecentAsync(int take, CancellationToken ct);
}
public interface IRotationRepository
{
    Task<IReadOnlyList<RotationProposal>> ByRunAsync(DateOnly runDate, CancellationToken ct);
    Task<RotationProposal?> GetAsync(Guid id, CancellationToken ct);
    void AddRange(IEnumerable<RotationProposal> items);
    Task ExpireOpenAsync(DateOnly beforeRun, CancellationToken ct);
}
public sealed record HoldRow(string BatchCode, int Quantity, bool Reserved);
public interface IStockHoldRepository
{
    Task AddAsync(string orderCode, string batchCode, int qty, CancellationToken ct);
    Task<IReadOnlyList<HoldRow>> ByOrderAsync(string orderCode, CancellationToken ct);
    Task MarkReservedAsync(string orderCode, CancellationToken ct);
    /// <summary>Remove the order's holds; with <paramref name="batchCodes"/> only those batches (one picking list of a multi-warehouse order).</summary>
    Task RemoveAsync(string orderCode, IReadOnlyCollection<string>? batchCodes, CancellationToken ct);
}
public interface ILabelRepository
{
    Task<Label?> GetAsync(string code, CancellationToken ct);
    Task<IReadOnlyList<Label>> ByBatchAsync(string batchCode, CancellationToken ct);
    void Add(Label l);
}
public interface IGoodsReceiptRepository
{
    Task<GoodsReceipt?> GetAsync(string code, CancellationToken ct);
    Task<IReadOnlyList<GoodsReceipt>> SearchAsync(string? warehouse, ReceiptStatus? status, CancellationToken ct);
    void Add(GoodsReceipt r);
}
public interface IDisposalRepository
{
    Task<IReadOnlyList<Disposal>> RecentAsync(string? warehouse, int take, CancellationToken ct);
    void Add(Disposal d);
}
