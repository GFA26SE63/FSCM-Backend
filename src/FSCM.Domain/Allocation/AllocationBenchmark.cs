namespace FSCM.Domain.Allocation;

/// <summary>Deterministic comparison of the production weighted allocator with FEFO on identical input.</summary>
public sealed record AllocationBenchmarkResult(
    AllocationResult WeightedGreedy,
    bool FefoFullyAllocated,
    decimal FefoAllocatedQuantity,
    decimal FefoShortageQuantity,
    IReadOnlyList<string> FefoBatchOrder,
    decimal WeightedNearExpiryQuantity,
    decimal FefoNearExpiryQuantity);

public static class AllocationBenchmark
{
    public static AllocationBenchmarkResult Compare(
        AllocationRequest request, IReadOnlyCollection<BatchCandidate> candidates,
        AllocationWeights? weights = null)
    {
        var weighted = new AllocationEngine(weights).Allocate(request, candidates);
        var eligibleIds = weighted.FilterTrace
            .Where(item => item.Stage == AllocationFilterStage.Delivery && item.Passed)
            .Select(item => item.BatchId).ToHashSet();
        var fefoCandidates = candidates.Where(item => eligibleIds.Contains(item.BatchId))
            .OrderBy(item => item.ExpiresAt).ThenBy(item => item.BatchCode, StringComparer.Ordinal).ToArray();
        var remaining = request.RequiredQuantity;
        var selected = new List<string>();
        var fefoAllocated = 0m;
        var fefoNearExpiry = 0m;
        foreach (var candidate in fefoCandidates)
        {
            if (remaining <= 0) break;
            var quantity = Math.Min(candidate.AvailableQuantity, remaining);
            if (quantity <= 0) continue;
            selected.Add(candidate.BatchCode);
            fefoAllocated += quantity;
            if (candidate.ExpiresAt <= candidate.EstimatedArrivalAt!.Value.AddHours((double)(request.TotalShelfLifeHours * 0.5m)))
                fefoNearExpiry += quantity;
            remaining -= quantity;
        }
        var weightedNearExpiry = weighted.Allocations
            .Where(item => weighted.FilterTrace.Any(trace => trace.BatchId == item.BatchId && trace.Stage == AllocationFilterStage.Delivery && trace.Passed &&
                trace.RemainingShelfLifeHours <= request.TotalShelfLifeHours * 0.5m))
            .Sum(item => item.Quantity);
        return new(weighted, remaining <= 0, fefoAllocated, remaining, selected, weightedNearExpiry, fefoNearExpiry);
    }
}
