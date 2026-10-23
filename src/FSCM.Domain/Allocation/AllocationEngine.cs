namespace FSCM.Domain.Allocation;

public sealed record AllocationWeights(
    decimal ShelfLife = 0.5m,
    decimal Distance = 0.3m,
    decimal Coverage = 0.2m,
    decimal MaximumDistanceMeters = 20_000m);

/// <summary>
/// One order line to serve. Shelf life and MLOR are compared in hours against the product's total
/// shelf life, never in whole days: a run that starts at 12:00 and arrives the next morning differs
/// from one arriving that evening, and rounding to days would hide it.
/// </summary>
public sealed record AllocationRequest(
    string Sku,
    decimal RequiredQuantity,
    int TotalShelfLifeHours,
    decimal MlorRatio,
    DateOnly? DeliveryDeadline);

/// <summary>
/// A batch the run may draw from. <paramref name="AvailableQuantity"/> is what is left in the run
/// pool, not the real balance. <paramref name="EstimatedArrivalDate"/> is the Vietnam calendar date
/// of <paramref name="EstimatedArrivalAt"/>; the caller converts so this stays free of time zones.
/// A null arrival means no route was found and the batch is skipped.
/// </summary>
public sealed record BatchCandidate(
    Guid BatchId,
    string BatchCode,
    Guid WarehouseId,
    string WarehouseCode,
    DateTimeOffset ExpiresAt,
    decimal AvailableQuantity,
    decimal DistanceMeters,
    DateTimeOffset? EstimatedArrivalAt,
    DateOnly? EstimatedArrivalDate);

public enum AllocationFilterStage
{
    Pool,
    Distance,
    Delivery
}

public enum AllocationFilterReason
{
    Eligible,
    NoRemainingQuantity,
    RouteUnavailable,
    OutsideServiceRadius,
    ArrivesAfterDeadline,
    BelowMlor
}

public sealed record AllocationFilterTrace(
    Guid BatchId,
    string BatchCode,
    Guid WarehouseId,
    string WarehouseCode,
    AllocationFilterStage Stage,
    bool Passed,
    AllocationFilterReason Reason,
    DateTimeOffset? EstimatedArrivalAt = null,
    decimal? RemainingShelfLifeHours = null,
    decimal? RequiredShelfLifeHours = null);

public sealed record AllocationScoreTrace(
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

public sealed record AllocationDecision(
    Guid BatchId,
    string BatchCode,
    Guid WarehouseId,
    string WarehouseCode,
    decimal Quantity,
    int Sequence,
    decimal DistanceMeters,
    DateTimeOffset EstimatedArrivalAt,
    decimal RemainingShelfLifeHours,
    decimal WeightedScore);

public sealed record AllocationResult(
    string Sku,
    decimal RequiredQuantity,
    decimal RequiredShelfLifeHours,
    AllocationWeights Weights,
    bool IsFullyAllocated,
    decimal ShortageQuantity,
    IReadOnlyList<AllocationDecision> Allocations,
    IReadOnlyList<AllocationFilterTrace> FilterTrace,
    IReadOnlyList<AllocationScoreTrace> ScoreTrace)
{
    public decimal AllocatedQuantity => Allocations.Sum(item => item.Quantity);
}

/// <summary>
/// Picks batches for one order line out of the run's shared pool: hard filters first, then
/// S = wE·fE + wD·fD + wQ·fQ over whatever survives, taking the best each turn and recomputing
/// coverage after every take.
///
/// A near-expiry reduction never reaches this scoring. Price is settled after the batches are
/// fixed (F89), so letting a discount raise a batch's score would let the cheaper batch win and
/// then justify its own discount.
///
/// A line that cannot be filled completely returns no allocations at all. Partial delivery is not
/// a thing the run may decide on its own, so handing back a half-filled plan would invite a caller
/// to commit one.
/// </summary>
public sealed class AllocationEngine
{
    private readonly AllocationWeights _weights;

    public AllocationEngine(AllocationWeights? weights = null)
    {
        _weights = weights ?? new AllocationWeights();
        ValidateWeights(_weights);
    }

    public AllocationResult Allocate(AllocationRequest request, IReadOnlyCollection<BatchCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(candidates);
        ValidateRequest(request);

        var duplicateBatchCode = candidates
            .GroupBy(item => item.BatchCode, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateBatchCode is not null)
        {
            throw new ArgumentException($"Batch code '{duplicateBatchCode}' occurs more than once.", nameof(candidates));
        }

        foreach (var candidate in candidates)
        {
            ValidateCandidate(candidate);
        }

        var requiredShelfLifeHours = request.MlorRatio * request.TotalShelfLifeHours;
        var filterTrace = new List<AllocationFilterTrace>();

        var poolEligible = new List<BatchCandidate>();
        foreach (var candidate in candidates
                     .OrderBy(item => item.ExpiresAt)
                     .ThenBy(item => item.BatchCode, StringComparer.Ordinal))
        {
            var passed = candidate.AvailableQuantity > 0;
            filterTrace.Add(ToFilterTrace(candidate, AllocationFilterStage.Pool, passed,
                passed ? AllocationFilterReason.Eligible : AllocationFilterReason.NoRemainingQuantity));
            if (passed) poolEligible.Add(candidate);
        }

        var distanceEligible = new List<BatchCandidate>();
        foreach (var candidate in poolEligible)
        {
            var reason = candidate.EstimatedArrivalAt is null
                ? AllocationFilterReason.RouteUnavailable
                : candidate.DistanceMeters > _weights.MaximumDistanceMeters
                    ? AllocationFilterReason.OutsideServiceRadius
                    : AllocationFilterReason.Eligible;
            var passed = reason == AllocationFilterReason.Eligible;
            filterTrace.Add(ToFilterTrace(candidate, AllocationFilterStage.Distance, passed, reason));
            if (passed) distanceEligible.Add(candidate);
        }

        var eligible = new List<EligibleCandidate>();
        foreach (var candidate in distanceEligible)
        {
            var arrival = candidate.EstimatedArrivalAt!.Value;
            var remainingShelfLifeHours = (decimal)(candidate.ExpiresAt - arrival).TotalHours;
            var reason = request.DeliveryDeadline is { } deadline
                    && candidate.EstimatedArrivalDate is { } arrivalDate
                    && arrivalDate > deadline
                ? AllocationFilterReason.ArrivesAfterDeadline
                : remainingShelfLifeHours < requiredShelfLifeHours
                    ? AllocationFilterReason.BelowMlor
                    : AllocationFilterReason.Eligible;
            var passed = reason == AllocationFilterReason.Eligible;
            filterTrace.Add(ToFilterTrace(candidate, AllocationFilterStage.Delivery, passed, reason,
                arrival, remainingShelfLifeHours, requiredShelfLifeHours));
            if (passed) eligible.Add(new EligibleCandidate(candidate, arrival, remainingShelfLifeHours));
        }

        var scoreTrace = new List<AllocationScoreTrace>();
        var attempted = new List<AllocationDecision>();
        var remainingDemand = request.RequiredQuantity;
        var iteration = 0;

        while (remainingDemand > 0 && eligible.Count > 0)
        {
            iteration++;
            var ranked = eligible
                .Select(item => Score(item, request, requiredShelfLifeHours, remainingDemand))
                .OrderByDescending(item => item.WeightedScore)
                .ThenBy(item => item.Candidate.Candidate.BatchCode, StringComparer.Ordinal)
                .ToArray();

            var selected = ranked[0];
            var selectedQuantity = Math.Min(selected.Candidate.Candidate.AvailableQuantity, remainingDemand);
            for (var index = 0; index < ranked.Length; index++)
            {
                var item = ranked[index];
                var isSelected = index == 0;
                scoreTrace.Add(new AllocationScoreTrace(
                    iteration,
                    index + 1,
                    item.Candidate.Candidate.BatchId,
                    item.Candidate.Candidate.BatchCode,
                    item.Candidate.Candidate.WarehouseId,
                    item.Candidate.Candidate.WarehouseCode,
                    remainingDemand,
                    Round(item.ShelfLifeScore),
                    Round(item.DistanceScore),
                    Round(item.CoverageScore),
                    Round(item.WeightedScore),
                    isSelected,
                    isSelected ? selectedQuantity : 0m));
            }

            attempted.Add(new AllocationDecision(
                selected.Candidate.Candidate.BatchId,
                selected.Candidate.Candidate.BatchCode,
                selected.Candidate.Candidate.WarehouseId,
                selected.Candidate.Candidate.WarehouseCode,
                selectedQuantity,
                iteration,
                selected.Candidate.Candidate.DistanceMeters,
                selected.Candidate.EstimatedArrivalAt,
                selected.Candidate.RemainingShelfLifeHours,
                Round(selected.WeightedScore)));

            remainingDemand -= selectedQuantity;
            eligible.Remove(selected.Candidate);
        }

        var fullyAllocated = remainingDemand <= 0;
        return new AllocationResult(
            request.Sku,
            request.RequiredQuantity,
            requiredShelfLifeHours,
            _weights,
            fullyAllocated,
            fullyAllocated ? 0m : remainingDemand,
            fullyAllocated ? attempted : [],
            filterTrace,
            scoreTrace);
    }

    /// <summary>
    /// fE rewards the shortest life that still clears MLOR at arrival, which is what makes this
    /// FEFO rather than "freshest first". fD rewards the nearer warehouse. fQ rewards covering more
    /// of what is still missing, and is recomputed every turn because the shortfall shrinks.
    /// </summary>
    private ScoredCandidate Score(
        EligibleCandidate candidate,
        AllocationRequest request,
        decimal requiredShelfLifeHours,
        decimal remainingDemand)
    {
        var shelfLifeDenominator = request.TotalShelfLifeHours - requiredShelfLifeHours;
        var shelfLifeScore = shelfLifeDenominator <= 0
            ? 1m
            : Math.Clamp(
                1m - (candidate.RemainingShelfLifeHours - requiredShelfLifeHours) / shelfLifeDenominator,
                0m,
                1m);
        var distanceScore = Math.Clamp(
            1m - candidate.Candidate.DistanceMeters / _weights.MaximumDistanceMeters,
            0m,
            1m);
        var coverageScore = Math.Min(candidate.Candidate.AvailableQuantity, remainingDemand) / remainingDemand;
        var weightedScore =
            _weights.ShelfLife * shelfLifeScore +
            _weights.Distance * distanceScore +
            _weights.Coverage * coverageScore;
        return new ScoredCandidate(candidate, shelfLifeScore, distanceScore, coverageScore, weightedScore);
    }

    private static AllocationFilterTrace ToFilterTrace(
        BatchCandidate candidate,
        AllocationFilterStage stage,
        bool passed,
        AllocationFilterReason reason,
        DateTimeOffset? estimatedArrivalAt = null,
        decimal? remainingShelfLifeHours = null,
        decimal? requiredShelfLifeHours = null) =>
        new(candidate.BatchId, candidate.BatchCode, candidate.WarehouseId, candidate.WarehouseCode,
            stage, passed, reason, estimatedArrivalAt, remainingShelfLifeHours, requiredShelfLifeHours);

    private static decimal Round(decimal value) =>
        Math.Round(value, 6, MidpointRounding.AwayFromZero);

    private static void ValidateWeights(AllocationWeights weights)
    {
        if (weights.ShelfLife < 0 || weights.Distance < 0 || weights.Coverage < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(weights), "Allocation weights cannot be negative.");
        }

        if (weights.ShelfLife + weights.Distance + weights.Coverage != 1m)
        {
            throw new ArgumentException("Allocation weights must sum to one.", nameof(weights));
        }

        if (weights.MaximumDistanceMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(weights), "Maximum distance must be greater than zero.");
        }
    }

    private static void ValidateRequest(AllocationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Sku))
        {
            throw new ArgumentException("SKU is required.", nameof(request));
        }

        if (request.RequiredQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Required quantity must be greater than zero.");
        }

        if (request.TotalShelfLifeHours <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Total shelf life must be greater than zero.");
        }

        if (request.MlorRatio is <= 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "MLOR ratio must be between zero and one.");
        }
    }

    private static void ValidateCandidate(BatchCandidate candidate)
    {
        if (candidate.BatchId == Guid.Empty || candidate.WarehouseId == Guid.Empty)
        {
            throw new ArgumentException("Batch and warehouse identifiers are required.", nameof(candidate));
        }

        if (string.IsNullOrWhiteSpace(candidate.BatchCode) || string.IsNullOrWhiteSpace(candidate.WarehouseCode))
        {
            throw new ArgumentException("Batch and warehouse codes are required.", nameof(candidate));
        }

        if (candidate.AvailableQuantity < 0 || candidate.DistanceMeters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(candidate), "Candidate quantity and distance cannot be negative.");
        }
    }

    private sealed record EligibleCandidate(
        BatchCandidate Candidate,
        DateTimeOffset EstimatedArrivalAt,
        decimal RemainingShelfLifeHours);

    private sealed record ScoredCandidate(
        EligibleCandidate Candidate,
        decimal ShelfLifeScore,
        decimal DistanceScore,
        decimal CoverageScore,
        decimal WeightedScore);
}
