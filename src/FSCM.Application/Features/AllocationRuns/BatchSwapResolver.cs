using FSCM.Domain.Allocation;

namespace FSCM.Application.Features.AllocationRuns;

/// <summary>
/// One quantity an order holds tentatively, with the facts that came from choosing that batch.
/// This ledger is the single source of truth for the plan: a swap rewrites the entry rather than
/// leaving a stale copy somewhere else.
/// </summary>
public sealed record BatchHolding(
    Guid RunOrderId,
    string OrderCode,
    int PriorityRank,
    Guid RunLineId,
    Guid BatchId,
    Guid WarehouseId,
    decimal Quantity,
    int Sequence,
    DateTimeOffset EstimatedArrivalAt,
    decimal RemainingShelfLifeHours,
    decimal WeightedScore,
    Guid? SwappedFromBatchId);

public static class SwapStopReasons
{
    public const string LineFilled = "LineFilled";
    public const string AlternativesExhausted = "AlternativesExhausted";
    public const string AttemptLimitReached = "AttemptLimitReached";
}

/// <summary>
/// One evaluation of moving a holder off a batch. Failed evaluations are recorded too, because the
/// attempt budget counts them and a trace has to show what was tried.
/// </summary>
public sealed record SwapAttempt(
    Guid DonorBatchId,
    string DonorBatchCode,
    string HolderOrderCode,
    Guid? AlternativeBatchId,
    string? AlternativeBatchCode,
    decimal Quantity,
    bool Succeeded,
    string Reason);

public sealed record SwapOutcome(
    decimal ReleasedQuantity,
    IReadOnlyList<SwapAttempt> Attempts,
    string StopReason);

/// <summary>
/// Frees stock for a short line by moving an earlier order off a batch it is holding, one level
/// only: the earlier order moves to a batch with quantity nobody has claimed, never to one it would
/// have to take from a third order. Allowing that would start a chain, and a chain can walk back
/// through the whole queue and make a plan impossible to explain.
///
/// The earlier order keeps everything it was promised. A move is only legal if the alternative
/// batch is itself valid for that order: its own MLOR at arrival, its own deadline, its own service
/// distance. Priority decides who chooses first, not who may be made worse off.
/// </summary>
public sealed class BatchSwapResolver(AllocationEngine engine)
{
    public const int DefaultAttemptLimit = 20;

    /// <summary>
    /// Tries to free <paramref name="shortfall"/> for the short line. The pool and the ledger are
    /// mutated only by moves that fully succeed; a move that cannot be completed leaves both as
    /// they were.
    /// </summary>
    public SwapOutcome Resolve(
        AllocationRequest shortRequest,
        Guid shortRetailerId,
        int shortPriorityRank,
        IReadOnlyCollection<PlannerBatch> productBatches,
        Func<Guid, AllocationRequest> holderRequest,
        Func<Guid, Guid> holderRetailer,
        Func<Guid, Guid, PlannerRoute?> route,
        IDictionary<Guid, decimal> remainingPool,
        IList<BatchHolding> holdings,
        decimal shortfall,
        int attemptLimit = DefaultAttemptLimit)
    {
        var attempts = new List<SwapAttempt>();
        var released = 0m;
        var batchByIdentifier = productBatches.ToDictionary(item => item.BatchId);

        // Only batches an earlier order is sitting on are worth freeing: anything unheld was
        // already offered to this line and rejected on its own merits.
        var donorBatchIds = holdings
            .Where(item => item.PriorityRank < shortPriorityRank && batchByIdentifier.ContainsKey(item.BatchId))
            .Select(item => item.BatchId)
            .Distinct()
            .ToArray();

        foreach (var donorBatchId in donorBatchIds)
        {
            if (released >= shortfall) break;
            if (attempts.Count >= attemptLimit)
            {
                return new SwapOutcome(released, attempts, SwapStopReasons.AttemptLimitReached);
            }

            var donorBatch = batchByIdentifier[donorBatchId];
            var held = holdings.Where(item => item.BatchId == donorBatchId
                && item.PriorityRank < shortPriorityRank).ToArray();
            var usability = engine.Evaluate(shortRequest, ToCandidate(
                donorBatch, held.Sum(item => item.Quantity), route(donorBatch.WarehouseId, shortRetailerId)));
            if (usability != AllocationFilterReason.Eligible)
            {
                attempts.Add(new SwapAttempt(donorBatchId, donorBatch.BatchCode, "-", null, null, 0m, false,
                    $"Dòng thiếu không dùng được batch này: {usability}."));
                continue;
            }

            foreach (var holding in held.OrderBy(item => item.PriorityRank))
            {
                if (released >= shortfall) break;
                if (attempts.Count >= attemptLimit)
                {
                    return new SwapOutcome(released, attempts, SwapStopReasons.AttemptLimitReached);
                }

                if (TryMove(holding, donorBatch, productBatches, holderRequest, holderRetailer, route,
                        remainingPool, holdings, attempts, attemptLimit))
                {
                    released += holding.Quantity;
                }
            }
        }

        // The budget can run out inside a move; report that rather than claiming the alternatives
        // were exhausted, because untried alternatives may still exist.
        var stopReason = released >= shortfall
            ? SwapStopReasons.LineFilled
            : attempts.Count >= attemptLimit
                ? SwapStopReasons.AttemptLimitReached
                : SwapStopReasons.AlternativesExhausted;
        return new SwapOutcome(released, attempts, stopReason);
    }

    private bool TryMove(
        BatchHolding holding,
        PlannerBatch donorBatch,
        IReadOnlyCollection<PlannerBatch> productBatches,
        Func<Guid, AllocationRequest> holderRequest,
        Func<Guid, Guid> holderRetailer,
        Func<Guid, Guid, PlannerRoute?> route,
        IDictionary<Guid, decimal> remainingPool,
        IList<BatchHolding> holdings,
        List<SwapAttempt> attempts,
        int attemptLimit)
    {
        var request = holderRequest(holding.RunLineId);
        // The alternative is judged against the holder's own leg: it is the order that must keep
        // meeting its deadline and MLOR after the move.
        var retailerId = holderRetailer(holding.RunLineId);
        foreach (var alternative in productBatches
                     .Where(item => item.BatchId != donorBatch.BatchId)
                     .OrderBy(item => item.ExpiresAt)
                     .ThenBy(item => item.BatchCode, StringComparer.Ordinal))
        {
            if (attempts.Count >= attemptLimit) return false;

            // Only quantity nobody has claimed may be used. A partly assigned batch offers only its
            // remainder, which is what stops the move from robbing a third order.
            var unassigned = remainingPool.TryGetValue(alternative.BatchId, out var free) ? free : 0m;
            if (unassigned < holding.Quantity)
            {
                attempts.Add(new SwapAttempt(
                    donorBatch.BatchId, donorBatch.BatchCode, holding.OrderCode,
                    alternative.BatchId, alternative.BatchCode, holding.Quantity, false,
                    $"Batch thay thế chỉ còn {unassigned:0.###} chưa cấp, cần {holding.Quantity:0.###}."));
                continue;
            }

            var alternativeRoute = route(alternative.WarehouseId, retailerId);
            var candidate = ToCandidate(alternative, unassigned, alternativeRoute);
            var reason = engine.Evaluate(request, candidate);
            if (reason != AllocationFilterReason.Eligible)
            {
                attempts.Add(new SwapAttempt(
                    donorBatch.BatchId, donorBatch.BatchCode, holding.OrderCode,
                    alternative.BatchId, alternative.BatchCode, holding.Quantity, false,
                    $"Đơn trước không giữ đủ điều kiện trên batch thay thế: {reason}."));
                continue;
            }

            // Commit the whole move or nothing: the holder leaves the donor batch entirely, and its
            // arrival and remaining life are restated for the batch it actually ends up on.
            var arrival = alternativeRoute!.EstimatedArrivalAt;
            remainingPool[alternative.BatchId] = unassigned - holding.Quantity;
            remainingPool[donorBatch.BatchId] =
                (remainingPool.TryGetValue(donorBatch.BatchId, out var donorFree) ? donorFree : 0m) + holding.Quantity;
            holdings.Remove(holding);
            holdings.Add(holding with
            {
                BatchId = alternative.BatchId,
                WarehouseId = alternative.WarehouseId,
                EstimatedArrivalAt = arrival,
                RemainingShelfLifeHours = (decimal)(alternative.ExpiresAt - arrival).TotalHours,
                SwappedFromBatchId = donorBatch.BatchId
            });
            attempts.Add(new SwapAttempt(
                donorBatch.BatchId, donorBatch.BatchCode, holding.OrderCode,
                alternative.BatchId, alternative.BatchCode, holding.Quantity, true,
                "Đã chuyển đơn trước sang batch thay thế và nhường batch cũ."));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Builds an engine candidate for one batch on one leg. A missing route leaves the arrival
    /// null, which the engine reports as RouteUnavailable rather than guessing a distance.
    /// </summary>
    public static BatchCandidate ToCandidate(PlannerBatch batch, decimal availableQuantity, PlannerRoute? route) =>
        new(batch.BatchId,
            batch.BatchCode,
            batch.WarehouseId,
            batch.WarehouseCode,
            batch.ExpiresAt,
            availableQuantity,
            route?.DistanceMeters ?? 0m,
            route?.EstimatedArrivalAt,
            route?.EstimatedArrivalDate);
}
