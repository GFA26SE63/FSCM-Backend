using FSCM.Application.Features.AllocationRuns;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.AllocationRuns;

/// <summary>
/// An in-memory run store with real stock: several batches of several products, each with its own
/// expiry and quantity, so a scenario can starve one line and not another.
///
/// Everything it does not implement still throws, inherited from the stub, so a scenario that
/// strays into an unprepared path fails loudly rather than reading an empty list and passing.
/// </summary>
internal sealed class ScenarioStore(Guid retailerId, Guid warehouseId) : StubAllocationRunStore
{
    public List<AllocationScenarioTests.ScenarioBatch> Batches { get; } = [];
    public List<EligibleOrderView> Eligible { get; } = [];
    public List<AllocationRun> Runs { get; } = [];
    public List<AllocationRunOrder> Orders { get; } = [];
    public List<AllocationRunLine> Lines { get; } = [];
    public List<AllocationRunStock> Pool { get; } = [];
    public List<AllocationRunAllocation> Allocations { get; } = [];
    public List<AllocationPlanVersion> PlanVersions { get; } = [];
    public List<AllocationPlanPrice> PlanPrices { get; } = [];
    public List<AllocationLineTrace> LineTraces { get; } = [];
    public List<AllocationTraceCandidate> TraceCandidates { get; } = [];
    public List<AllocationTraceScore> TraceScores { get; } = [];

    public override Task<AllocationSettings> GetSettingsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AllocationSettings(0.5m, 0.3m, 0.2m, 50_000));

    public override Task<AllocationRun?> FindByBusinessDateAsync(
        DateOnly businessDate, CancellationToken cancellationToken) =>
        Task.FromResult(Runs.SingleOrDefault(item => item.BusinessDate == businessDate));

    public override Task<string> NextRunCodeAsync(DateOnly businessDate, CancellationToken cancellationToken) =>
        Task.FromResult($"PB-{businessDate:yyyyMMdd}-{Runs.Count + 1:00}");

    public override Task<AllocationRunView?> FindViewAsync(Guid runId, CancellationToken cancellationToken) =>
        Task.FromResult(ToView(Runs.SingleOrDefault(item => item.Id == runId)));

    public override Task<AllocationRunView?> FindViewByBusinessDateAsync(
        DateOnly businessDate, CancellationToken cancellationToken) =>
        Task.FromResult(ToView(Runs.SingleOrDefault(item => item.BusinessDate == businessDate)));

    public override Task<IReadOnlyCollection<EligibleOrderView>> ListEligibleOrdersAsync(
        DateTimeOffset cutoffAt, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<EligibleOrderView>>(
            Eligible.Where(item => item.Order.ServerReceivedAt < cutoffAt)
                .OrderBy(item => item.Order.ServerReceivedAt)
                .ThenBy(item => item.Order.OrderCode, StringComparer.Ordinal)
                .ToArray());

    public override Task<IReadOnlyCollection<AvailableBatchView>> ListAvailableStockAsync(
        IReadOnlyCollection<Guid> productIds, DateOnly businessDate, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<AvailableBatchView>>(Batches
            .Where(batch => productIds.Contains(batch.ProductId))
            .Select(batch => new AvailableBatchView(
                new Batch
                {
                    Id = batch.Id,
                    ProductId = batch.ProductId,
                    WarehouseId = batch.WarehouseId,
                    BatchCode = batch.Code,
                    ExpirationDate = DateOnly.FromDateTime(batch.ExpiresAt.UtcDateTime)
                },
                batch.WarehouseId, batch.Quantity, 1))
            .ToArray());

    public override Task<IReadOnlyDictionary<(Guid RetailerId, Guid ProductId), (decimal Ratio, string Source)>>
        ResolveMlorAsync(
            IReadOnlyCollection<(Guid RetailerId, Guid ProductId)> pairs, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<(Guid, Guid), (decimal, string)>>(
            pairs.ToDictionary(pair => pair, _ => (0.5m, MlorSources.RetailerProduct)));

    public override Task<IReadOnlyCollection<PlannerBatch>> ListPlannerBatchesAsync(
        Guid runId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<PlannerBatch>>(Pool
            .Where(item => item.AllocationRunId == runId && item.RemainingQuantity > 0)
            .Join(Batches, item => item.BatchId, batch => batch.Id, (item, batch) => new PlannerBatch(
                batch.Id, batch.Code, batch.WarehouseId, "WH-01", batch.ProductId,
                batch.ExpiresAt, item.RemainingQuantity))
            .ToArray());

    public override Task<IReadOnlyDictionary<(Guid WarehouseId, Guid RetailerId), decimal>> ListDistancesAsync(
        IReadOnlyCollection<Guid> warehouseIds, IReadOnlyCollection<Guid> retailerIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<(Guid, Guid), decimal>>(
            warehouseIds.SelectMany(warehouse => retailerIds.Select(retailer => (warehouse, retailer)))
                .ToDictionary(pair => pair, _ => 5_000m));

    public override Task<IReadOnlyDictionary<Guid, Guid>> ListProductCategoriesAsync(
        IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, Guid>>(
            productIds.ToDictionary(item => item, _ => Guid.NewGuid()));

    public override Task<IReadOnlyDictionary<Guid, DateTimeOffset>> ListBatchExpiryAsync(
        IReadOnlyCollection<Guid> batchIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, DateTimeOffset>>(Batches
            .Where(batch => batchIds.Contains(batch.Id))
            .ToDictionary(batch => batch.Id, batch => batch.ExpiresAt));

    public override Task<int> MaxPlanVersionNumberAsync(Guid runId, CancellationToken cancellationToken) =>
        Task.FromResult(PlanVersions.Where(item => item.AllocationRunId == runId)
            .Select(item => item.VersionNumber).DefaultIfEmpty(0).Max());

    public override Task<AllocationPlanVersion?> FindLivePlanVersionAsync(
        Guid runId, CancellationToken cancellationToken) =>
        Task.FromResult(PlanVersions.SingleOrDefault(item =>
            item.AllocationRunId == runId && item.Status == AllocationPlanVersionStatuses.AwaitingApproval));

    public override void AddRun(AllocationRun run) => Runs.Add(run);
    public override void AddOrders(IEnumerable<AllocationRunOrder> orders) => Orders.AddRange(orders);
    public override void AddLines(IEnumerable<AllocationRunLine> lines) => Lines.AddRange(lines);
    public override void AddPool(IEnumerable<AllocationRunStock> pool) => Pool.AddRange(pool);
    public override void AddAllocations(IEnumerable<AllocationRunAllocation> allocations) =>
        Allocations.AddRange(allocations);
    public override void AddPlanVersion(AllocationPlanVersion version) => PlanVersions.Add(version);
    public override void AddPlanPrices(IEnumerable<AllocationPlanPrice> prices) => PlanPrices.AddRange(prices);
    public override void AddLineTraces(IEnumerable<AllocationLineTrace> traces) => LineTraces.AddRange(traces);
    public override void AddTraceCandidates(IEnumerable<AllocationTraceCandidate> candidates) =>
        TraceCandidates.AddRange(candidates);
    public override void AddTraceScores(IEnumerable<AllocationTraceScore> scores) => TraceScores.AddRange(scores);

    private AllocationRunView? ToView(AllocationRun? run) => run is null
        ? null
        : new AllocationRunView(
            run,
            Orders.Where(item => item.AllocationRunId == run.Id).ToArray(),
            Lines.Where(line => Orders.Any(order =>
                order.AllocationRunId == run.Id && order.Id == line.AllocationRunOrderId)).ToArray(),
            Pool.Where(item => item.AllocationRunId == run.Id).ToArray());
}
