namespace FSCM.Application.Features.AllocationRuns;

/// <summary>
/// F90: the result of the run for one order, as Sales and the retailer see it.
///
/// Published only once an Admin approved the plan. Before that there is a proposal, which the
/// approval screen shows and which may still be recalculated; handing the same figures out as a
/// "result" would have someone acting on a number that is still being argued about.
///
/// A short order ends here. It holds no stock, nothing is part-delivered, and it does not roll into
/// tomorrow's run carrying today's place in the queue. Buying the rest means a new order, which the
/// server timestamps on arrival and which therefore queues behind whatever arrived before it. The
/// result says both of those in fields rather than in prose, because a screen that cannot read them
/// will sooner or later offer a "resend" button the run cannot honour.
/// </summary>
public sealed class AllocationRunResultService(IAllocationRunStore store)
{
    public async Task<OrderRunResult> GetByOrderAsync(Guid salesOrderId, CancellationToken cancellationToken)
    {
        var view = await store.FindOrderRunResultAsync(salesOrderId, cancellationToken)
            ?? throw AllocationRunOperationException.NotFound(
                "This order has no approved allocation result yet.");

        var pricesByLine = view.Prices
            .GroupBy(price => price.AllocationRunLineId)
            .ToDictionary(group => group.Key, group => group.Sum(price => price.Amount));
        var traceByLine = view.Traces.ToDictionary(trace => trace.AllocationRunLineId);

        var lines = view.Lines
            .OrderBy(line => line.LineNumber)
            .Select(line =>
            {
                var shortage = Math.Max(0m, line.OrderedQuantity - line.AllocatedQuantity);
                var trace = traceByLine.GetValueOrDefault(line.Id);
                return new OrderRunResultLine(
                    line.SalesOrderLineId,
                    line.SkuSnapshot,
                    line.LineNumber,
                    line.OrderedQuantity,
                    line.AllocatedQuantity,
                    shortage,
                    trace?.Outcome ?? (shortage > 0
                        ? AllocationLineTraceOutcomes.Short
                        : AllocationLineTraceOutcomes.Allocated),
                    trace?.Note,
                    pricesByLine.GetValueOrDefault(line.Id));
            })
            .ToArray();

        var fulfilled = view.Order.Status == AllocationRunOrderStatuses.Fulfilled;

        return new OrderRunResult(
            salesOrderId,
            view.Order.OrderCodeSnapshot,
            view.Run.Id,
            view.Run.RunCode,
            view.Run.BusinessDate,
            view.Run.CutoffAt,
            view.PlanVersion.ApprovedAt ?? view.PlanVersion.UpdatedAt,
            view.PlanVersion.VersionNumber,
            fulfilled,
            view.Order.ShortageReason,
            // A short order was never held: approval reserves only what it could fill completely.
            fulfilled,
            // There is no resend that keeps a place in the queue, for any order, ever.
            false,
            Round(lines.Sum(line => line.FinalAmount)),
            lines);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
