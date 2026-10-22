using FSCM.Domain.Allocation;

namespace FSCM.Application.Features.AllocationRuns;

/// <summary>Where a batch sits and how far it is, so the planner can build engine candidates.</summary>
public sealed record PlannerBatch(
    Guid BatchId,
    string BatchCode,
    Guid WarehouseId,
    string WarehouseCode,
    Guid ProductId,
    DateTimeOffset ExpiresAt,
    decimal RemainingQuantity,
    decimal DistanceMeters,
    DateTimeOffset? EstimatedArrivalAt,
    DateOnly? EstimatedArrivalDate);

public sealed record PlannerLine(
    Guid RunLineId,
    Guid ProductId,
    string Sku,
    int LineNumber,
    decimal OrderedQuantity,
    int TotalShelfLifeHours,
    decimal EffectiveMlorRatio);

public sealed record PlannerOrder(
    Guid RunOrderId,
    string OrderCode,
    Guid RetailerId,
    int PriorityRank,
    DateOnly? DeliveryDeadline,
    IReadOnlyList<PlannerLine> Lines);

public sealed record PlannedAllocation(
    Guid RunLineId,
    Guid BatchId,
    Guid WarehouseId,
    decimal Quantity,
    int Sequence,
    DateTimeOffset EstimatedArrivalAt,
    decimal RemainingShelfLifeHours,
    decimal WeightedScore,
    /// <summary>Set when a later order's swap moved this quantity here after it was first chosen.</summary>
    Guid? SwappedFromBatchId);

public sealed record PlannedOrder(
    Guid RunOrderId,
    string OrderCode,
    bool IsFullyAllocated,
    string? ShortageReason,
    IReadOnlyList<PlannedAllocation> Allocations,
    IReadOnlyList<AllocationResult> LineResults,
    IReadOnlyList<SwapAttempt> SwapAttempts,
    string? SwapStopReason);

public sealed record AllocationPlan(
    IReadOnlyList<PlannedOrder> Orders,
    IReadOnlyDictionary<Guid, decimal> RemainingPool)
{
    public IEnumerable<PlannedOrder> Fulfilled => Orders.Where(item => item.IsFullyAllocated);
    public IEnumerable<PlannedOrder> Short => Orders.Where(item => !item.IsFullyAllocated);
}

/// <summary>
/// Runs the scoring engine over the whole run: orders in priority order, each line drawing from one
/// shared pool, so an earlier order genuinely takes stock away from a later one.
///
/// When a line falls short, one-level batch swapping is tried before the order is given up: an
/// earlier order may be moved onto stock nobody has claimed so it can hand its batch over.
///
/// An order is all or nothing. A line that still cannot be filled undoes every tentative draw the
/// order already made, including the lines that succeeded, and hands the quantity back to the pool
/// for the orders behind it. Keeping a partly served order would both break "no partial delivery"
/// and quietly starve the orders that follow.
///
/// The holdings ledger is the single source of truth. A swap can move an order that was already
/// planned, so each order's committed allocations are read back from the ledger at the end rather
/// than from what it was first given.
/// </summary>
public sealed class AllocationPlanner(AllocationEngine engine, BatchSwapResolver swapResolver)
{
    public AllocationPlan Plan(
        IReadOnlyList<PlannerOrder> orders,
        IReadOnlyCollection<PlannerBatch> pool,
        int swapAttemptLimit = BatchSwapResolver.DefaultAttemptLimit)
    {
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(pool);

        var remaining = pool.ToDictionary(item => item.BatchId, item => item.RemainingQuantity);
        var holdings = new List<BatchHolding>();
        var requestByLine = new Dictionary<Guid, AllocationRequest>();
        var byProduct = pool.GroupBy(item => item.ProductId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var planned = new List<PlannedOrder>();

        foreach (var order in orders.OrderBy(item => item.PriorityRank))
        {
            var lineResults = new List<AllocationResult>();
            var swapAttempts = new List<SwapAttempt>();
            string? shortageReason = null;
            string? swapStopReason = null;

            foreach (var line in order.Lines.OrderBy(item => item.LineNumber))
            {
                var request = new AllocationRequest(
                    line.Sku, line.OrderedQuantity, line.TotalShelfLifeHours,
                    line.EffectiveMlorRatio, order.DeliveryDeadline);
                requestByLine[line.RunLineId] = request;

                var batches = byProduct.TryGetValue(line.ProductId, out var found) ? found : [];
                var result = engine.Allocate(request, Candidates(batches, remaining));

                if (!result.IsFullyAllocated)
                {
                    var outcome = swapResolver.Resolve(
                        request,
                        order.PriorityRank,
                        batches,
                        holderLine => requestByLine[holderLine],
                        remaining,
                        holdings,
                        result.ShortageQuantity,
                        swapAttemptLimit);
                    swapAttempts.AddRange(outcome.Attempts);
                    swapStopReason = outcome.StopReason;
                    if (outcome.ReleasedQuantity > 0)
                    {
                        result = engine.Allocate(request, Candidates(batches, remaining));
                    }
                }

                lineResults.Add(result);
                if (!result.IsFullyAllocated)
                {
                    shortageReason =
                        $"Dòng {line.LineNumber} ({line.Sku}) thiếu {result.ShortageQuantity:0.###} trên {line.OrderedQuantity:0.###}.";
                    break;
                }

                foreach (var decision in result.Allocations)
                {
                    remaining[decision.BatchId] = remaining.GetValueOrDefault(decision.BatchId) - decision.Quantity;
                    holdings.Add(new BatchHolding(
                        order.RunOrderId, order.OrderCode, order.PriorityRank, line.RunLineId,
                        decision.BatchId, decision.WarehouseId, decision.Quantity, decision.Sequence,
                        decision.EstimatedArrivalAt, decision.RemainingShelfLifeHours,
                        decision.WeightedScore, null));
                }
            }

            if (shortageReason is not null)
            {
                foreach (var holding in holdings.Where(item => item.RunOrderId == order.RunOrderId).ToArray())
                {
                    remaining[holding.BatchId] = remaining.GetValueOrDefault(holding.BatchId) + holding.Quantity;
                    holdings.Remove(holding);
                }

                planned.Add(new PlannedOrder(order.RunOrderId, order.OrderCode, false, shortageReason,
                    [], lineResults, swapAttempts, swapStopReason));
                continue;
            }

            planned.Add(new PlannedOrder(order.RunOrderId, order.OrderCode, true, null,
                [], lineResults, swapAttempts, swapStopReason));
        }

        // Read every fulfilled order's allocations back from the ledger, so an order a later swap
        // moved reports the batch it ended up on rather than the one it first chose.
        var byOrder = holdings.GroupBy(item => item.RunOrderId)
            .ToDictionary(group => group.Key, group => group
                .OrderBy(item => item.Sequence)
                .Select(item => new PlannedAllocation(
                    item.RunLineId, item.BatchId, item.WarehouseId, item.Quantity, item.Sequence,
                    item.EstimatedArrivalAt, item.RemainingShelfLifeHours, item.WeightedScore,
                    item.SwappedFromBatchId))
                .ToArray());

        return new AllocationPlan(
            planned.Select(order => order.IsFullyAllocated && byOrder.TryGetValue(order.RunOrderId, out var rows)
                ? order with { Allocations = rows }
                : order).ToArray(),
            remaining);
    }

    private static BatchCandidate[] Candidates(
        IReadOnlyCollection<PlannerBatch> batches, IReadOnlyDictionary<Guid, decimal> remaining) =>
        batches.Select(batch => new BatchCandidate(
            batch.BatchId,
            batch.BatchCode,
            batch.WarehouseId,
            batch.WarehouseCode,
            batch.ExpiresAt,
            remaining.GetValueOrDefault(batch.BatchId),
            batch.DistanceMeters,
            batch.EstimatedArrivalAt,
            batch.EstimatedArrivalDate)).ToArray();
}
