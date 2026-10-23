using FSCM.Domain.Allocation;

namespace FSCM.Application.Features.AllocationRuns;

/// <summary>
/// A batch in the run pool. How far it is and when it would arrive depends on the retailer, so
/// that is resolved per order through <see cref="PlannerRoute"/>.
/// </summary>
public sealed record PlannerBatch(
    Guid BatchId,
    string BatchCode,
    Guid WarehouseId,
    string WarehouseCode,
    Guid ProductId,
    DateTimeOffset ExpiresAt,
    decimal RemainingQuantity);

/// <summary>
/// The leg from one warehouse to one retailer. A null route means the pair cannot be served, and
/// every batch in that warehouse is skipped for that order.
/// </summary>
public sealed record PlannerRoute(
    decimal DistanceMeters,
    DateTimeOffset EstimatedArrivalAt,
    DateOnly EstimatedArrivalDate);

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
    decimal WeightedScore);

/// <summary>
/// What the engine worked out for one line, tied back to the line it was asked about. The engine
/// itself deals in SKUs and knows nothing of run line identifiers, so the planner carries the link.
/// </summary>
public sealed record PlannedLineResult(
    Guid RunLineId,
    int LineNumber,
    AllocationResult Result);

public sealed record PlannedOrder(
    Guid RunOrderId,
    string OrderCode,
    bool IsFullyAllocated,
    string? ShortageReason,
    IReadOnlyList<PlannedAllocation> Allocations,
    IReadOnlyList<PlannedLineResult> LineResults);

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
/// What an order has been given is never taken back to serve a later one. Scope V10 cut batch
/// swapping, so first come really is first served and a plan can be read top to bottom.
///
/// An order is still all or nothing. A line that cannot be filled undoes every tentative draw the
/// order already made, including the lines that succeeded, and hands the quantity back to the pool
/// for the orders behind it. Keeping a partly served order would break "no partial delivery" and
/// quietly starve the queue.
/// </summary>
public sealed class AllocationPlanner(AllocationEngine engine)
{
    public AllocationPlan Plan(
        IReadOnlyList<PlannerOrder> orders,
        IReadOnlyCollection<PlannerBatch> pool,
        Func<Guid, Guid, PlannerRoute?> route)
    {
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(route);

        // The pool is drawn down as we go; this is the only state shared across orders.
        var remaining = pool.ToDictionary(item => item.BatchId, item => item.RemainingQuantity);
        var byProduct = pool.GroupBy(item => item.ProductId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var planned = new List<PlannedOrder>();

        foreach (var order in orders.OrderBy(item => item.PriorityRank))
        {
            var orderAllocations = new List<PlannedAllocation>();
            var lineResults = new List<PlannedLineResult>();
            // Draws this order has made but not yet earned: released in full if any line falls short.
            var tentative = new Dictionary<Guid, decimal>();
            string? shortageReason = null;

            foreach (var line in order.Lines.OrderBy(item => item.LineNumber))
            {
                var request = new AllocationRequest(
                    line.Sku, line.OrderedQuantity, line.TotalShelfLifeHours,
                    line.EffectiveMlorRatio, order.DeliveryDeadline);
                var batches = byProduct.TryGetValue(line.ProductId, out var found) ? found : [];
                var result = engine.Allocate(request, Candidates(batches, remaining, order.RetailerId, route));
                lineResults.Add(new PlannedLineResult(line.RunLineId, line.LineNumber, result));

                if (!result.IsFullyAllocated)
                {
                    shortageReason =
                        $"Dòng {line.LineNumber} ({line.Sku}) thiếu {result.ShortageQuantity:0.###} trên {line.OrderedQuantity:0.###}.";
                    break;
                }

                foreach (var decision in result.Allocations)
                {
                    remaining[decision.BatchId] = remaining.GetValueOrDefault(decision.BatchId) - decision.Quantity;
                    tentative[decision.BatchId] = tentative.GetValueOrDefault(decision.BatchId) + decision.Quantity;
                    orderAllocations.Add(new PlannedAllocation(
                        line.RunLineId,
                        decision.BatchId,
                        decision.WarehouseId,
                        decision.Quantity,
                        decision.Sequence,
                        decision.EstimatedArrivalAt,
                        decision.RemainingShelfLifeHours,
                        decision.WeightedScore));
                }
            }

            if (shortageReason is not null)
            {
                foreach (var (batchId, quantity) in tentative)
                {
                    remaining[batchId] = remaining.GetValueOrDefault(batchId) + quantity;
                }

                planned.Add(new PlannedOrder(
                    order.RunOrderId, order.OrderCode, false, shortageReason, [], lineResults));
                continue;
            }

            planned.Add(new PlannedOrder(
                order.RunOrderId, order.OrderCode, true, null, orderAllocations, lineResults));
        }

        return new AllocationPlan(planned, remaining);
    }

    private static BatchCandidate[] Candidates(
        IReadOnlyCollection<PlannerBatch> batches,
        IReadOnlyDictionary<Guid, decimal> remaining,
        Guid retailerId,
        Func<Guid, Guid, PlannerRoute?> route) =>
        batches.Select(batch =>
        {
            var leg = route(batch.WarehouseId, retailerId);
            return new BatchCandidate(
                batch.BatchId,
                batch.BatchCode,
                batch.WarehouseId,
                batch.WarehouseCode,
                batch.ExpiresAt,
                remaining.GetValueOrDefault(batch.BatchId),
                // A missing leg leaves the arrival null, which the engine reports as
                // RouteUnavailable rather than guessing a distance.
                leg?.DistanceMeters ?? 0m,
                leg?.EstimatedArrivalAt,
                leg?.EstimatedArrivalDate);
        }).ToArray();
}
