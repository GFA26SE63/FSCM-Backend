using FSCM.Application.Features.AllocationRuns;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.AllocationRuns;

/// <summary>
/// A store that supports nothing until a test overrides it. Every unused member throws, so a test
/// that touches a path it did not set up fails loudly instead of quietly reading an empty list.
/// </summary>
internal abstract class StubAllocationRunStore : IAllocationRunStore
{
    private static Exception NotSetUp([System.Runtime.CompilerServices.CallerMemberName] string member = "") =>
        new NotSupportedException($"{member} is not set up for this test.");

    public virtual Task<AllocationRun?> FindByBusinessDateAsync(DateOnly businessDate, CancellationToken cancellationToken) => throw NotSetUp();
    public virtual Task<AllocationRunView?> FindViewAsync(Guid runId, CancellationToken cancellationToken) => throw NotSetUp();
    public virtual Task<AllocationRunView?> FindViewForUpdateAsync(Guid runId, CancellationToken cancellationToken) => FindViewAsync(runId, cancellationToken);
    public virtual Task<AllocationRunView?> FindViewByBusinessDateAsync(DateOnly businessDate, CancellationToken cancellationToken) => throw NotSetUp();
    public virtual Task<IReadOnlyCollection<EligibleOrderView>> ListEligibleOrdersAsync(DateTimeOffset cutoffAt, CancellationToken cancellationToken) => throw NotSetUp();
    public virtual Task<IReadOnlyCollection<AvailableBatchView>> ListAvailableStockAsync(IReadOnlyCollection<Guid> productIds, DateOnly businessDate, CancellationToken cancellationToken) => throw NotSetUp();
    public virtual Task<IReadOnlyDictionary<(Guid RetailerId, Guid ProductId), (decimal Ratio, string Source)>> ResolveMlorAsync(IReadOnlyCollection<(Guid RetailerId, Guid ProductId)> pairs, CancellationToken cancellationToken) => throw NotSetUp();
    public virtual Task<IReadOnlyCollection<PlannerBatch>> ListPlannerBatchesAsync(Guid runId, CancellationToken cancellationToken) => throw NotSetUp();
    public virtual Task<IReadOnlyDictionary<(Guid WarehouseId, Guid RetailerId), decimal>> ListDistancesAsync(IReadOnlyCollection<Guid> warehouseIds, IReadOnlyCollection<Guid> retailerIds, CancellationToken cancellationToken) => throw NotSetUp();
    public virtual Task<IReadOnlyDictionary<Guid, Guid>> ListProductCategoriesAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken) => throw NotSetUp();
    public virtual Task<IReadOnlyDictionary<Guid, DateTimeOffset>> ListBatchExpiryAsync(IReadOnlyCollection<Guid> batchIds, CancellationToken cancellationToken) => throw NotSetUp();
    public virtual void AddAllocations(IEnumerable<AllocationRunAllocation> allocations) => throw NotSetUp();
    public virtual void AddPlanVersion(AllocationPlanVersion version) => throw NotSetUp();
    public virtual void AddPlanPrices(IEnumerable<AllocationPlanPrice> prices) => throw NotSetUp();
    public virtual Task<int> MaxPlanVersionNumberAsync(Guid runId, CancellationToken cancellationToken) => throw NotSetUp();
    public virtual Task<AllocationPlanVersion?> FindLivePlanVersionAsync(Guid runId, CancellationToken cancellationToken) => throw NotSetUp();
    public virtual Task<AllocationPlanVersion?> FindPlanVersionForUpdateAsync(Guid versionId, CancellationToken cancellationToken) => throw NotSetUp();
    public virtual Task<AllocationPlanVersionView?> FindPlanVersionViewAsync(Guid versionId, CancellationToken cancellationToken) => throw NotSetUp();
    public virtual Task<IReadOnlyCollection<AllocationPlanVersionView>> ListPlanVersionsAsync(Guid runId, CancellationToken cancellationToken) => throw NotSetUp();
    public virtual Task<AllocationRun?> FindRunForUpdateAsync(Guid runId, CancellationToken cancellationToken) => throw NotSetUp();
    public virtual Task<IReadOnlyCollection<ReservableAllocation>> ListReservableAsync(Guid planVersionId, CancellationToken cancellationToken) => throw NotSetUp();
    public virtual Task<AllocationPlanVersion?> FindApprovedPlanVersionAsync(Guid runId, CancellationToken cancellationToken) => throw NotSetUp();
    public virtual Task<IReadOnlyCollection<PickableAllocation>> ListPickableAsync(Guid planVersionId, CancellationToken cancellationToken) => throw NotSetUp();
    public virtual Task<IReadOnlyCollection<RunPickingListView>> ListRunPickingListsAsync(Guid runId, CancellationToken cancellationToken) => throw NotSetUp();
    public virtual Task<IReadOnlyCollection<RunPickingListView>> ListRunPickingListsForWarehouseAsync(Guid warehouseId, CancellationToken cancellationToken) => throw NotSetUp();
    public virtual Task<RunPickingListView?> FindRunPickingListAsync(Guid pickingListId, CancellationToken cancellationToken) => throw NotSetUp();
    public virtual Task<OrderRunResultView?> FindOrderRunResultAsync(Guid salesOrderId, CancellationToken cancellationToken) => throw NotSetUp();
    public virtual Task<OrderPriceView?> FindOrderPriceAsync(Guid salesOrderId, CancellationToken cancellationToken) => throw NotSetUp();
    public virtual Task<AllocationOrderTraceView?> FindOrderTraceAsync(Guid salesOrderId, CancellationToken cancellationToken) => throw NotSetUp();
    public virtual void AddLineTraces(IEnumerable<AllocationLineTrace> traces) => throw NotSetUp();
    public virtual void AddTraceCandidates(IEnumerable<AllocationTraceCandidate> candidates) => throw NotSetUp();
    public virtual void AddTraceScores(IEnumerable<AllocationTraceScore> scores) => throw NotSetUp();
    public virtual Task<Guid?> FindOrderIdForLineAsync(Guid salesOrderLineId, CancellationToken cancellationToken) => throw NotSetUp();
    public virtual void AddRunPickingLists(IEnumerable<RunPickingList> lists) => throw NotSetUp();
    public virtual void AddRunPickingGathers(IEnumerable<RunPickingGather> gathers) => throw NotSetUp();
    public virtual void AddRunPickingSplits(IEnumerable<RunPickingSplit> splits) => throw NotSetUp();
    public virtual void AddReservation(Reservation reservation) => throw NotSetUp();
    public virtual void AddInventoryTransaction(InventoryTransaction transaction) => throw NotSetUp();
    public virtual void AddAuditLog(AuditLog auditLog) => throw NotSetUp();
    public virtual Task<AllocationSettings> GetSettingsAsync(CancellationToken cancellationToken) => throw NotSetUp();
    public virtual Task<string> NextRunCodeAsync(DateOnly businessDate, CancellationToken cancellationToken) => throw NotSetUp();
    public virtual void AddRun(AllocationRun run) => throw NotSetUp();
    public virtual void AddOrders(IEnumerable<AllocationRunOrder> orders) => throw NotSetUp();
    public virtual void AddLines(IEnumerable<AllocationRunLine> lines) => throw NotSetUp();
    public virtual void AddPool(IEnumerable<AllocationRunStock> pool) => throw NotSetUp();
    public virtual Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public virtual Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken) => action(cancellationToken);
    public virtual void ClearTracking() { }
}
