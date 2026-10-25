using FSCM.Domain.Entities;

namespace FSCM.Application.Features.AllocationRuns;

public sealed record OrderPriceLineView(AllocationRunLine Line);

public sealed record OrderPricePartView(AllocationPlanPrice Price, string BatchCode);

/// <summary>
/// An order's priced allocation: the plan version it belongs to, its lines, and the price of every
/// part. Read from the newest version the order appears in, so a superseded proposal is not served
/// once a later one exists.
/// </summary>
public sealed record OrderPriceView(
    string OrderCode,
    Guid AllocationRunId,
    string RunCode,
    AllocationPlanVersion PlanVersion,
    IReadOnlyCollection<OrderPriceLineView> Lines,
    IReadOnlyCollection<OrderPricePartView> Prices);

/// <summary>
/// One line's working, as stored. The line itself comes along because the MLOR ratio and its source
/// were frozen on it by the run; copying them into the trace would create a second version of the
/// same fact that could disagree with the first.
/// </summary>
public sealed record AllocationLineTraceView(
    AllocationLineTrace Trace,
    AllocationRunLine Line,
    IReadOnlyCollection<AllocationTraceCandidate> Candidates,
    IReadOnlyCollection<AllocationTraceScore> Scores);

public sealed record AllocationOrderTraceView(
    string OrderCode,
    Guid AllocationRunId,
    string RunCode,
    AllocationPlanVersion PlanVersion,
    IReadOnlyCollection<AllocationLineTraceView> Lines);

public sealed record RunPickingListView(
    RunPickingList List,
    IReadOnlyCollection<RunPickingGather> Gathers,
    IReadOnlyCollection<RunPickingSplit> Splits);

/// <summary>
/// One allocated part with the codes a reader needs. The identifiers alone are no use on a screen
/// where an Admin has to recognise the batch they are approving.
/// </summary>
public sealed record AllocationPlanAllocationView(
    AllocationRunAllocation Allocation,
    string BatchCode,
    string WarehouseCode);

public sealed record AllocationPlanVersionView(
    AllocationPlanVersion PlanVersion,
    string RunCode,
    IReadOnlyCollection<AllocationPlanPrice> Prices,
    IReadOnlyCollection<AllocationPlanAllocationView> Allocations);

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

    /// <summary>The same view, tracked, for the step that writes the plan back onto it.</summary>
    Task<AllocationRunView?> FindViewForUpdateAsync(Guid runId, CancellationToken cancellationToken);
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

    /// <summary>The pool as planner input: batch and warehouse codes joined to what is left.</summary>
    Task<IReadOnlyCollection<PlannerBatch>> ListPlannerBatchesAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>
    /// Straight-line distance per warehouse-retailer pair from the cached matrix. A pair with no
    /// usable row is simply absent, and the planner treats it as unreachable.
    /// </summary>
    Task<IReadOnlyDictionary<(Guid WarehouseId, Guid RetailerId), decimal>> ListDistancesAsync(
        IReadOnlyCollection<Guid> warehouseIds,
        IReadOnlyCollection<Guid> retailerIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, Guid>> ListProductCategoriesAsync(
        IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, DateTimeOffset>> ListBatchExpiryAsync(
        IReadOnlyCollection<Guid> batchIds, CancellationToken cancellationToken);

    void AddAllocations(IEnumerable<AllocationRunAllocation> allocations);
    void AddPlanVersion(AllocationPlanVersion version);
    void AddPlanPrices(IEnumerable<AllocationPlanPrice> prices);

    Task<int> MaxPlanVersionNumberAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>The version still awaiting a decision, if any. At most one exists per run.</summary>
    Task<AllocationPlanVersion?> FindLivePlanVersionAsync(Guid runId, CancellationToken cancellationToken);

    Task<AllocationPlanVersion?> FindPlanVersionForUpdateAsync(Guid versionId, CancellationToken cancellationToken);
    Task<AllocationPlanVersionView?> FindPlanVersionViewAsync(Guid versionId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<AllocationPlanVersionView>> ListPlanVersionsAsync(
        Guid runId, CancellationToken cancellationToken);
    Task<AllocationRun?> FindRunForUpdateAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>
    /// The allocations of a plan version joined to live batch and balance rows, tracked so the hold
    /// can be written, and flagged when one already holds stock.
    /// </summary>
    Task<IReadOnlyCollection<ReservableAllocation>> ListReservableAsync(
        Guid planVersionId, CancellationToken cancellationToken);

    Task<AllocationPlanVersion?> FindApprovedPlanVersionAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>
    /// Allocations of an approved version joined to the order, line and price record a picking
    /// sheet has to name.
    /// </summary>
    Task<IReadOnlyCollection<PickableAllocation>> ListPickableAsync(
        Guid planVersionId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<RunPickingListView>> ListRunPickingListsAsync(
        Guid runId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<RunPickingListView>> ListRunPickingListsForWarehouseAsync(
        Guid warehouseId, CancellationToken cancellationToken);
    Task<RunPickingListView?> FindRunPickingListAsync(Guid pickingListId, CancellationToken cancellationToken);

    Task<OrderPriceView?> FindOrderPriceAsync(Guid salesOrderId, CancellationToken cancellationToken);
    Task<AllocationOrderTraceView?> FindOrderTraceAsync(Guid salesOrderId, CancellationToken cancellationToken);
    Task<Guid?> FindOrderIdForLineAsync(Guid salesOrderLineId, CancellationToken cancellationToken);

    void AddLineTraces(IEnumerable<AllocationLineTrace> traces);
    void AddTraceCandidates(IEnumerable<AllocationTraceCandidate> candidates);
    void AddTraceScores(IEnumerable<AllocationTraceScore> scores);

    void AddRunPickingLists(IEnumerable<RunPickingList> lists);
    void AddRunPickingGathers(IEnumerable<RunPickingGather> gathers);
    void AddRunPickingSplits(IEnumerable<RunPickingSplit> splits);

    void AddReservation(Reservation reservation);
    void AddInventoryTransaction(InventoryTransaction transaction);

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
