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
    decimal WeightedScore);

public sealed record PlannedOrder(
    Guid RunOrderId,
    string OrderCode,
    bool IsFullyAllocated,
    string? ShortageReason,
    IReadOnlyList<PlannedAllocation> Allocations,
    IReadOnlyList<AllocationResult> LineResults);

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
/// An order is all or nothing. A line that cannot be filled undoes every tentative draw the order
/// already made, including the lines that succeeded, and hands the quantity back to the pool for
/// the orders behind it. Keeping a partly served order would both break "no partial delivery" and
/// quietly starve the orders that follow.
/// </summary>
public sealed class AllocationPlanner(AllocationEngine engine)
{
    public AllocationPlan Plan(
        IReadOnlyList<PlannerOrder> orders,
        IReadOnlyCollection<PlannerBatch> pool)
    {
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(pool);

        // The pool is drawn down as we go; this is the only mutable state across orders.
        var remaining = pool.ToDictionary(item => item.BatchId, item => item.RemainingQuantity);
        var byProduct = pool.GroupBy(item => item.ProductId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var planned = new List<PlannedOrder>();

        foreach (var order in orders.OrderBy(item => item.PriorityRank))
        {
            var orderAllocations = new List<PlannedAllocation>();
            var lineResults = new List<AllocationResult>();
            // Draws this order has made but not yet earned: released in full if any line falls short.
            var tentative = new Dictionary<Guid, decimal>();
            string? shortageReason = null;

            foreach (var line in order.Lines.OrderBy(item => item.LineNumber))
            {
                var candidates = (byProduct.TryGetValue(line.ProductId, out var batches) ? batches : [])
                    .Select(batch => new BatchCandidate(
                        batch.BatchId,
                        batch.BatchCode,
                        batch.WarehouseId,
                        batch.WarehouseCode,
                        batch.ExpiresAt,
                        remaining.GetValueOrDefault(batch.BatchId),
                        batch.DistanceMeters,
                        batch.EstimatedArrivalAt,
                        batch.EstimatedArrivalDate))
                    .ToArray();

                var result = engine.Allocate(
                    new AllocationRequest(
                        line.Sku,
                        line.OrderedQuantity,
                        line.TotalShelfLifeHours,
                        line.EffectiveMlorRatio,
                        order.DeliveryDeadline),
                    candidates);
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

                planned.Add(new PlannedOrder(order.RunOrderId, order.OrderCode, false, shortageReason, [], lineResults));
                continue;
            }

            planned.Add(new PlannedOrder(order.RunOrderId, order.OrderCode, true, null, orderAllocations, lineResults));
        }

        return new AllocationPlan(planned, remaining);
    }
}
