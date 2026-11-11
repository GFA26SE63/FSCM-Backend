namespace FSCM.Domain.Allocation;

/// <summary>Deterministic comparison of the production weighted allocator with FEFO on identical input.</summary>
public sealed record AllocationBenchmarkResult(
    AllocationResult WeightedGreedy,
    bool FefoFullyAllocated,
    decimal FefoAllocatedQuantity,
    decimal FefoShortageQuantity,
    IReadOnlyList<string> FefoBatchOrder,
    decimal WeightedNearExpiryQuantity,
    decimal FefoNearExpiryQuantity)
{
    public IReadOnlyList<AllocationDecision> FefoAllocations { get; init; } = [];
}

public static class AllocationBenchmark
{
    public static AllocationBenchmarkResult Compare(
        AllocationRequest request, IReadOnlyCollection<BatchCandidate> candidates,
        AllocationWeights? weights = null, decimal? nearExpiryHours = null)
    {
        if (nearExpiryHours < 0) throw new ArgumentOutOfRangeException(nameof(nearExpiryHours));
        var threshold = nearExpiryHours ?? request.TotalShelfLifeHours * 0.5m;
        var weighted = new AllocationEngine(weights).Allocate(request, candidates);
        var eligibleIds = weighted.FilterTrace
            .Where(item => item.Stage == AllocationFilterStage.Delivery && item.Passed)
            .Select(item => item.BatchId).ToHashSet();
        var fefoCandidates = candidates.Where(item => eligibleIds.Contains(item.BatchId))
            .OrderBy(item => item.ExpiresAt).ThenBy(item => item.BatchCode, StringComparer.Ordinal).ToArray();
        var remaining = request.RequiredQuantity;
        var selected = new List<string>();
        var decisions = new List<AllocationDecision>();
        var fefoAllocated = 0m;
        var fefoNearExpiry = 0m;
        foreach (var candidate in fefoCandidates)
        {
            if (remaining <= 0) break;
            var quantity = Math.Min(candidate.AvailableQuantity, remaining);
            if (quantity <= 0) continue;
            selected.Add(candidate.BatchCode);
            fefoAllocated += quantity;
            var life = (decimal)(candidate.ExpiresAt - candidate.EstimatedArrivalAt!.Value).TotalHours;
            decisions.Add(new(candidate.BatchId, candidate.BatchCode, candidate.WarehouseId, candidate.WarehouseCode,
                quantity, decisions.Count + 1, candidate.DistanceMeters, candidate.EstimatedArrivalAt.Value, life, 0));
            if (life <= threshold)
                fefoNearExpiry += quantity;
            remaining -= quantity;
        }
        var weightedNearExpiry = weighted.Allocations
            .Where(item => item.RemainingShelfLifeHours <= threshold)
            .Sum(item => item.Quantity);
        var full = remaining <= 0;
        return new(weighted, full, full ? fefoAllocated : 0m, remaining, full ? selected : [], weightedNearExpiry, full ? fefoNearExpiry : 0m)
        { FefoAllocations = full ? decisions : [] };
    }
}
