using FSCM.Domain.Allocation;

namespace FSCM.UnitTests.AllocationRuns;

public sealed class AllocationBenchmarkTests
{
    [Fact]
    public void A_short_FEFO_order_has_no_committed_quantity_or_expiry_benefit()
    {
        var now=DateTimeOffset.Parse("2026-11-11T00:00:00Z");
        var result=AllocationBenchmark.Compare(new("SKU-1",30,720,0.25m,null),
            [Candidate("B-SHORT",now.AddHours(200),now,12,1000)]);
        Assert.False(result.FefoFullyAllocated);Assert.Equal(18,result.FefoShortageQuantity);
        Assert.Equal(0,result.FefoAllocatedQuantity);Assert.Empty(result.FefoBatchOrder);Assert.Empty(result.FefoAllocations);
        Assert.Equal(0,result.FefoNearExpiryQuantity);Assert.Equal(0,result.WeightedNearExpiryQuantity);
    }

    [Fact]
    public void Near_expiry_measurement_can_use_the_SKU_threshold_instead_of_half_the_shelf_life()
    {
        var now=DateTimeOffset.Parse("2026-11-11T00:00:00Z");
        var result=AllocationBenchmark.Compare(new("SKU-1",10,720,0.25m,null),
            [Candidate("B-VALID",now.AddHours(200),now,10,1000)],nearExpiryHours:192);
        Assert.Equal(0,result.FefoNearExpiryQuantity);Assert.Equal(0,result.WeightedNearExpiryQuantity);
    }
    [Fact]
    public void Compares_both_algorithms_on_the_same_filtered_pool_without_partial_delivery()
    {
        var now = new DateTimeOffset(2026, 11, 2, 5, 0, 0, TimeSpan.Zero);
        var request = new AllocationRequest("SKU-1", 10, 720, 0.4m, null);
        var candidates = new[]
        {
            Candidate("B-NEAR", now.AddDays(16), now.AddDays(1), 10, 6_000),
            Candidate("B-FAR", now.AddDays(40), now.AddDays(1), 10, 2_000),
        };

        var result = AllocationBenchmark.Compare(request, candidates);

        Assert.True(result.WeightedGreedy.IsFullyAllocated,
            $"shortage={result.WeightedGreedy.ShortageQuantity}; trace={string.Join(" | ", result.WeightedGreedy.FilterTrace.Select(item => $"{item.BatchCode}:{item.Stage}:{item.Passed}:{item.Reason}"))}");
        Assert.True(result.FefoFullyAllocated);
        Assert.Equal(10m, result.FefoAllocatedQuantity);
        Assert.Equal(new[] { "B-NEAR" }, result.FefoBatchOrder);
        Assert.Equal(0m, result.FefoShortageQuantity);
    }

    private static BatchCandidate Candidate(string code, DateTimeOffset expiresAt, DateTimeOffset arrivalAt, decimal quantity, decimal distance) =>
        new(Guid.NewGuid(), code, Guid.NewGuid(), $"WH-{code}", expiresAt, quantity, distance,
            arrivalAt, DateOnly.FromDateTime(arrivalAt.Date));
}
