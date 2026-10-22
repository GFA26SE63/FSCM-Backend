using FSCM.Domain.Entities;

namespace FSCM.Application.Features.AllocationRuns;

public sealed record AllocationRunView(
    AllocationRun Run,
    IReadOnlyCollection<AllocationRunOrder> Orders,
    IReadOnlyCollection<AllocationRunLine> Lines,
    IReadOnlyCollection<AllocationRunStock> Pool);

/// <summary>An order eligible for a run, with the lines and product facts the snapshot needs.</summary>
public sealed record EligibleOrderView(
    SalesOrder Order,
    IReadOnlyCollection<EligibleOrderLineView> Lines);

public sealed record EligibleOrderLineView(
    SalesOrderLine Line,
    Product Product);

/// <summary>Available stock for one batch at snapshot time, with the version used to detect drift later.</summary>
public sealed record AvailableBatchView(
    Batch Batch,
    Guid WarehouseId,
    decimal AvailableQuantity,
    int InventoryVersion);

public interface IAllocationRunStore
{
    Task<AllocationRun?> FindByBusinessDateAsync(DateOnly businessDate, CancellationToken cancellationToken);
    Task<AllocationRunView?> FindViewAsync(Guid runId, CancellationToken cancellationToken);
    Task<AllocationRunView?> FindViewByBusinessDateAsync(DateOnly businessDate, CancellationToken cancellationToken);

    /// <summary>
    /// Orders reviewed and accepted before the cutoff. Ordering is decided by the service, not here,
    /// so the priority rule stays in one place.
    /// </summary>
    Task<IReadOnlyCollection<EligibleOrderView>> ListEligibleOrdersAsync(
        DateTimeOffset cutoffAt, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<AvailableBatchView>> ListAvailableStockAsync(
        IReadOnlyCollection<Guid> productIds, DateOnly businessDate, CancellationToken cancellationToken);

    /// <summary>
    /// The effective MLOR for a retailer and product. Today this resolves retailer-SKU then the
    /// configured default; once F22 stores the applied ratio on the order line, the run copies that
    /// instead and this becomes a fallback for older orders.
    /// </summary>
    Task<IReadOnlyDictionary<(Guid RetailerId, Guid ProductId), (decimal Ratio, string Source)>> ResolveMlorAsync(
        IReadOnlyCollection<(Guid RetailerId, Guid ProductId)> pairs, CancellationToken cancellationToken);

    Task<AllocationSettings> GetSettingsAsync(CancellationToken cancellationToken);
    Task<string> NextRunCodeAsync(DateOnly businessDate, CancellationToken cancellationToken);

    void AddRun(AllocationRun run);
    void AddOrders(IEnumerable<AllocationRunOrder> orders);
    void AddLines(IEnumerable<AllocationRunLine> lines);
    void AddPool(IEnumerable<AllocationRunStock> pool);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken);
    void ClearTracking();
}
