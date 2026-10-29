namespace FSCM.Application.Features.AllocationRuns;

/// <summary>One batch as a hard filter saw it, and what stopped it if anything did.</summary>
public sealed record TraceCandidateResult(
    Guid BatchId,
    string BatchCode,
    Guid WarehouseId,
    string WarehouseCode,
    string Stage,
    bool Passed,
    string Reason,
    DateTimeOffset? EstimatedArrivalAt,
    decimal? RemainingShelfLifeHours,
    decimal? RequiredShelfLifeHours);

public sealed record TraceScoreResult(
    int Iteration,
    int Rank,
    Guid BatchId,
    string BatchCode,
    Guid WarehouseId,
    string WarehouseCode,
    decimal RemainingDemand,
    decimal ShelfLifeScore,
    decimal DistanceScore,
    decimal CoverageScore,
    decimal WeightedScore,
    bool Selected,
    decimal SelectedQuantity);

public sealed record TraceWeightsResult(
    decimal ShelfLife,
    decimal Distance,
    decimal Coverage,
    decimal MaximumDistanceMeters);

public sealed record TraceLineResult(
    Guid SalesOrderLineId,
    Guid AllocationRunLineId,
    string Sku,
    int LineNumber,
    string Outcome,
    decimal OrderedQuantity,
    decimal DrawnQuantity,
    decimal ShortageQuantity,
    /// <summary>RetailerProduct, RetailerCategory or ProductDefault, as the run resolved it.</summary>
    string MlorSource,
    decimal EffectiveMlorRatio,
    int TotalShelfLifeHours,
    decimal RequiredShelfLifeHours,
    TraceWeightsResult Weights,
    string? Note,
    IReadOnlyCollection<TraceCandidateResult> Candidates,
    IReadOnlyCollection<TraceScoreResult> Scores);

public sealed record OrderTraceDetailResult(
    Guid SalesOrderId,
    string OrderCode,
    Guid AllocationRunId,
    string RunCode,
    Guid AllocationPlanVersionId,
    int PlanVersionNumber,
    string PlanVersionStatus,
    bool IsApproved,
    IReadOnlyCollection<TraceLineResult> Lines);

/// <summary>
/// Reads back why the run chose what it chose, for one order.
///
/// It answers "why not that batch" as much as "why this one", so rejected candidates and losing
/// scores come back too: a score on its own says nothing without the scores it beat, and the
/// batches that were ruled out are exactly what a retailer asking about a shortage wants to see.
///
/// It carries no price. Batches are settled first and priced afterwards (F89), and the reduction a
/// batch later received is a separate question with a separate record (F98) keyed the same way.
/// Mixing the two here would invite a reader to think a discount helped a batch get chosen, which
/// is the one thing the scoring deliberately does not allow.
/// </summary>
public sealed class AllocationTraceService(IAllocationRunStore store)
{
    public async Task<OrderTraceDetailResult> GetByOrderAsync(
        Guid salesOrderId, CancellationToken cancellationToken)
    {
        var view = await store.FindOrderTraceAsync(salesOrderId, cancellationToken)
            ?? throw AllocationRunOperationException.NotFound(
                "This order has no allocation trace; it was never put through a run.");

        var lines = view.Lines
            .OrderBy(line => line.Line.LineNumber)
            .Select(line => new TraceLineResult(
                line.Line.SalesOrderLineId,
                line.Line.Id,
                line.Line.SkuSnapshot,
                line.Line.LineNumber,
                line.Trace.Outcome,
                line.Trace.OrderedQuantity,
                line.Trace.DrawnQuantity,
                line.Trace.ShortageQuantity,
                // Read from the line the run froze, not copied into the trace: one fact, one place.
                line.Line.MlorSource,
                line.Line.EffectiveMlorRatio,
                line.Line.TotalShelfLifeHours,
                line.Trace.RequiredShelfLifeHours,
                new TraceWeightsResult(
                    line.Trace.ShelfLifeWeight,
                    line.Trace.DistanceWeight,
                    line.Trace.CoverageWeight,
                    line.Trace.MaximumDistanceMeters),
                line.Trace.Note,
                line.Candidates
                    .OrderBy(item => StageOrder(item.Stage))
                    .ThenBy(item => item.BatchCodeSnapshot, StringComparer.Ordinal)
                    .Select(item => new TraceCandidateResult(
                        item.BatchId,
                        item.BatchCodeSnapshot,
                        item.WarehouseId,
                        item.WarehouseCodeSnapshot,
                        item.Stage,
                        item.Passed,
                        item.Reason,
                        item.EstimatedArrivalAt,
                        item.RemainingShelfLifeHours,
                        item.RequiredShelfLifeHours))
                    .ToArray(),
                line.Scores
                    .OrderBy(item => item.Iteration)
                    .ThenBy(item => item.Rank)
                    .Select(item => new TraceScoreResult(
                        item.Iteration,
                        item.Rank,
                        item.BatchId,
                        item.BatchCodeSnapshot,
                        item.WarehouseId,
                        item.WarehouseCodeSnapshot,
                        item.RemainingDemand,
                        item.ShelfLifeScore,
                        item.DistanceScore,
                        item.CoverageScore,
                        item.WeightedScore,
                        item.Selected,
                        item.SelectedQuantity))
                    .ToArray()))
            .ToArray();

        return new OrderTraceDetailResult(
            salesOrderId,
            view.OrderCode,
            view.AllocationRunId,
            view.RunCode,
            view.PlanVersion.Id,
            view.PlanVersion.VersionNumber,
            view.PlanVersion.Status,
            view.PlanVersion.Status == AllocationPlanVersionStatuses.Approved,
            lines);
    }

    /// <summary>
    /// Pool, then Distance, then Delivery: the order the filters were actually applied in, so a
    /// reader follows a batch's progress down the list rather than alphabetically.
    /// </summary>
    private static int StageOrder(string stage) => stage switch
    {
        nameof(Domain.Allocation.AllocationFilterStage.Pool) => 0,
        nameof(Domain.Allocation.AllocationFilterStage.Distance) => 1,
        nameof(Domain.Allocation.AllocationFilterStage.Delivery) => 2,
        _ => 3
    };
}
