namespace FSCM.Domain.Allocation;

public sealed record AllocationWeights(
    decimal ShelfLife = 0.5m,
    decimal Distance = 0.3m,
    decimal Coverage = 0.2m,
    decimal MaximumDistanceMeters = 20_000m);

public sealed record AllocationRequest(
    string Sku,
    int RequiredQuantity,
    int TotalShelfLifeDays,
    decimal MlorRatio,
    DateOnly Today,
    DateOnly DispatchDate,
    DateOnly? RequestedDeliveryDate);

public sealed record BatchCandidate(
    Guid BatchId,
    string BatchCode,
    Guid WarehouseId,
    string WarehouseCode,
    DateOnly ExpirationDate,
    int AvailableQuantity,
    decimal DistanceMeters,
    int? TransitDays);

public enum AllocationFilterStage
{
    Inventory,
    Distance,
    Delivery
}

public enum AllocationFilterReason
{
    Eligible,
    Expired,
    NoAvailableStock,
    RouteUnavailable,
    OutsideServiceRadius,
    ArrivesAfterRequestedDate,
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
    DateOnly? EstimatedArrivalDate = null,
    decimal? RemainingShelfLifeDays = null,
    decimal? RequiredMlorDays = null);

public sealed record AllocationScoreTrace(
    int Iteration,
    int Rank,
    Guid BatchId,
    string BatchCode,
    Guid WarehouseId,
    string WarehouseCode,
    int RemainingDemand,
    decimal ShelfLifeScore,
    decimal DistanceScore,
    decimal CoverageScore,
    decimal WeightedScore,
    bool Selected,
    int SelectedQuantity);

public sealed record AllocationDecision(
    Guid BatchId,
    string BatchCode,
    Guid WarehouseId,
    string WarehouseCode,
    int Quantity,
    int Sequence,
    decimal DistanceMeters,
    DateOnly EstimatedArrivalDate,
    decimal RemainingShelfLifeDays,
    decimal WeightedScore);

public sealed record AllocationResult(
    string Sku,
    int RequiredQuantity,
    decimal RequiredMlorDays,
    AllocationWeights Weights,
    bool IsFullyAllocated,
    int ShortageQuantity,
    IReadOnlyList<AllocationDecision> Allocations,
    IReadOnlyList<AllocationFilterTrace> FilterTrace,
    IReadOnlyList<AllocationScoreTrace> ScoreTrace)
{
    public int AllocatedQuantity => Allocations.Sum(item => item.Quantity);
}

/// <summary>
/// Implements the approved B1-B5 order-allocation policy without persistence or I/O.
/// Failed plans expose their attempted greedy selections through <see cref="AllocationResult.ScoreTrace"/>,
/// but never expose partial allocations that a caller could accidentally hold.
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

        var requiredMlorDays = request.MlorRatio * request.TotalShelfLifeDays;
        var filterTrace = new List<AllocationFilterTrace>();
        var inventoryEligible = new List<BatchCandidate>();

        foreach (var candidate in candidates
                     .OrderBy(item => item.ExpirationDate)
                     .ThenBy(item => item.BatchCode, StringComparer.Ordinal))
        {
            var reason = candidate.ExpirationDate <= request.Today
                ? AllocationFilterReason.Expired
                : candidate.AvailableQuantity <= 0
                    ? AllocationFilterReason.NoAvailableStock
                    : AllocationFilterReason.Eligible;
            var passed = reason == AllocationFilterReason.Eligible;
            filterTrace.Add(ToFilterTrace(candidate, AllocationFilterStage.Inventory, passed, reason));
            if (passed)
            {
                inventoryEligible.Add(candidate);
            }
        }

        var distanceEligible = new List<BatchCandidate>();
        foreach (var candidate in inventoryEligible)
        {
            var reason = candidate.TransitDays is null
                ? AllocationFilterReason.RouteUnavailable
                : candidate.DistanceMeters > _weights.MaximumDistanceMeters
                    ? AllocationFilterReason.OutsideServiceRadius
                    : AllocationFilterReason.Eligible;
            var passed = reason == AllocationFilterReason.Eligible;
            filterTrace.Add(ToFilterTrace(candidate, AllocationFilterStage.Distance, passed, reason));
            if (passed)
            {
                distanceEligible.Add(candidate);
            }
        }

        var eligible = new List<EligibleCandidate>();
        foreach (var candidate in distanceEligible)
        {
            var estimatedArrivalDate = request.DispatchDate.AddDays(candidate.TransitDays!.Value);
            var remainingShelfLifeDays = candidate.ExpirationDate.DayNumber - estimatedArrivalDate.DayNumber;
            var reason = request.RequestedDeliveryDate is { } requestedDate && estimatedArrivalDate > requestedDate
                ? AllocationFilterReason.ArrivesAfterRequestedDate
                : remainingShelfLifeDays < requiredMlorDays
                    ? AllocationFilterReason.BelowMlor
                    : AllocationFilterReason.Eligible;
            var passed = reason == AllocationFilterReason.Eligible;
            filterTrace.Add(ToFilterTrace(candidate, AllocationFilterStage.Delivery, passed, reason,
                estimatedArrivalDate, remainingShelfLifeDays, requiredMlorDays));
            if (passed)
            {
                eligible.Add(new EligibleCandidate(candidate, estimatedArrivalDate, remainingShelfLifeDays));
            }
        }

        var scoreTrace = new List<AllocationScoreTrace>();
        var attemptedAllocations = new List<AllocationDecision>();
        var remainingDemand = request.RequiredQuantity;
        var iteration = 0;

        while (remainingDemand > 0 && eligible.Count > 0)
        {
            iteration++;
            var ranked = eligible
                .Select(item => Score(item, request, requiredMlorDays, remainingDemand))
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
                    isSelected ? selectedQuantity : 0));
            }

            attemptedAllocations.Add(new AllocationDecision(
                selected.Candidate.Candidate.BatchId,
                selected.Candidate.Candidate.BatchCode,
                selected.Candidate.Candidate.WarehouseId,
                selected.Candidate.Candidate.WarehouseCode,
                selectedQuantity,
                iteration,
                selected.Candidate.Candidate.DistanceMeters,
                selected.Candidate.EstimatedArrivalDate,
                selected.Candidate.RemainingShelfLifeDays,
                Round(selected.WeightedScore)));

            remainingDemand -= selectedQuantity;
            eligible.Remove(selected.Candidate);
        }

        var fullyAllocated = remainingDemand == 0;
        return new AllocationResult(
            request.Sku,
            request.RequiredQuantity,
            requiredMlorDays,
            _weights,
            fullyAllocated,
            remainingDemand,
            fullyAllocated ? attemptedAllocations : [],
            filterTrace,
            scoreTrace);
    }

    private ScoredCandidate Score(
        EligibleCandidate candidate,
        AllocationRequest request,
        decimal requiredMlorDays,
        int remainingDemand)
    {
        var shelfLifeDenominator = request.TotalShelfLifeDays - requiredMlorDays;
        var shelfLifeScore = shelfLifeDenominator <= 0
            ? 1m
            : Math.Clamp(
                1m - (candidate.RemainingShelfLifeDays - requiredMlorDays) / shelfLifeDenominator,
                0m,
                1m);
        var distanceScore = Math.Clamp(
            1m - candidate.Candidate.DistanceMeters / _weights.MaximumDistanceMeters,
            0m,
            1m);
        var coverageScore = Math.Min(candidate.Candidate.AvailableQuantity, remainingDemand) / (decimal)remainingDemand;
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
        DateOnly? estimatedArrivalDate = null,
        decimal? remainingShelfLifeDays = null,
        decimal? requiredMlorDays = null) =>
        new(candidate.BatchId, candidate.BatchCode, candidate.WarehouseId, candidate.WarehouseCode,
            stage, passed, reason, estimatedArrivalDate, remainingShelfLifeDays, requiredMlorDays);

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

        if (request.TotalShelfLifeDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Total shelf life must be greater than zero.");
        }

        if (request.MlorRatio is < 0 or > 1)
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

        if (candidate.AvailableQuantity < 0 || candidate.DistanceMeters < 0 || candidate.TransitDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(candidate), "Candidate quantities, distance, and transit days cannot be negative.");
        }
    }

    private sealed record EligibleCandidate(
        BatchCandidate Candidate,
        DateOnly EstimatedArrivalDate,
        decimal RemainingShelfLifeDays);

    private sealed record ScoredCandidate(
        EligibleCandidate Candidate,
        decimal ShelfLifeScore,
        decimal DistanceScore,
        decimal CoverageScore,
        decimal WeightedScore);
}
