using FSCM.Domain.Allocation;

namespace FSCM.UnitTests.Allocation;

public sealed class AllocationEngineTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    [Fact]
    public void Allocate_records_every_hard_filter_reason()
    {
        var engine = new AllocationEngine();
        var request = Request(requiredQuantity: 10, requestedDeliveryDate: Today.AddDays(2));
        var candidates = new[]
        {
            Candidate("B-EXPIRED", expiration: Today),
            Candidate("B-EMPTY", available: 0),
            Candidate("B-NO-ROUTE", transitDays: null),
            Candidate("B-FAR", distanceMeters: 20_001),
            Candidate("B-LATE", transitDays: 3),
            Candidate("B-MLOR", expiration: Today.AddDays(5), transitDays: 1),
            Candidate("B-OK", expiration: Today.AddDays(20), transitDays: 1)
        };

        var result = engine.Allocate(request, candidates);

        AssertReason(result, "B-EXPIRED", AllocationFilterReason.Expired);
        AssertReason(result, "B-EMPTY", AllocationFilterReason.NoAvailableStock);
        AssertReason(result, "B-NO-ROUTE", AllocationFilterReason.RouteUnavailable);
        AssertReason(result, "B-FAR", AllocationFilterReason.OutsideServiceRadius);
        AssertReason(result, "B-LATE", AllocationFilterReason.ArrivesAfterRequestedDate);
        AssertReason(result, "B-MLOR", AllocationFilterReason.BelowMlor);
        Assert.True(result.IsFullyAllocated);
        Assert.Equal("B-OK", Assert.Single(result.Allocations).BatchCode);
    }

    [Fact]
    public void Allocate_uses_approved_weighted_score()
    {
        var engine = new AllocationEngine();
        var result = engine.Allocate(
            Request(requiredQuantity: 10, mlorRatio: 0.25m),
            [Candidate("B-SCORE", expiration: Today.AddDays(16), available: 5, distanceMeters: 5_000)]);

        var score = Assert.Single(result.ScoreTrace);
        Assert.Equal(0.666667m, score.ShelfLifeScore);
        Assert.Equal(0.75m, score.DistanceScore);
        Assert.Equal(0.5m, score.CoverageScore);
        Assert.Equal(0.658333m, score.WeightedScore);
    }

    [Fact]
    public void Allocate_breaks_equal_scores_by_batch_code_ascending()
    {
        var engine = new AllocationEngine();
        var result = engine.Allocate(
            Request(requiredQuantity: 5),
            [Candidate("B-002", available: 5), Candidate("B-001", available: 5)]);

        Assert.True(result.IsFullyAllocated);
        Assert.Equal("B-001", Assert.Single(result.Allocations).BatchCode);
        Assert.True(result.ScoreTrace.Single(item => item.BatchCode == "B-001").Selected);
    }

    [Fact]
    public void Allocate_rejects_all_partial_allocations_when_stock_is_insufficient()
    {
        var engine = new AllocationEngine();
        var result = engine.Allocate(
            Request(requiredQuantity: 12),
            [Candidate("B-001", available: 4), Candidate("B-002", available: 5)]);

        Assert.False(result.IsFullyAllocated);
        Assert.Equal(3, result.ShortageQuantity);
        Assert.Equal(0, result.AllocatedQuantity);
        Assert.Empty(result.Allocations);
        Assert.Equal(9, result.ScoreTrace.Where(item => item.Selected).Sum(item => item.SelectedQuantity));
    }

    [Fact]
    public void Allocate_can_fulfil_one_line_from_multiple_warehouses()
    {
        var engine = new AllocationEngine();
        var firstWarehouse = Guid.NewGuid();
        var secondWarehouse = Guid.NewGuid();
        var result = engine.Allocate(
            Request(requiredQuantity: 10),
            [
                Candidate("B-001", warehouseId: firstWarehouse, warehouseCode: "WH-01", available: 6,
                    expiration: Today.AddDays(12), distanceMeters: 2_000),
                Candidate("B-002", warehouseId: secondWarehouse, warehouseCode: "WH-02", available: 8,
                    expiration: Today.AddDays(20), distanceMeters: 6_000)
            ]);

        Assert.True(result.IsFullyAllocated);
        Assert.Equal(10, result.AllocatedQuantity);
        Assert.Equal(2, result.Allocations.Count);
        Assert.Equal(2, result.Allocations.Select(item => item.WarehouseId).Distinct().Count());
        Assert.Equal(new[] { 6, 4 }, result.Allocations.Select(item => item.Quantity));
        Assert.Contains(result.ScoreTrace, item => item.Iteration == 2 && item.RemainingDemand == 4);
    }

    [Fact]
    public void Constructor_rejects_invalid_weight_total()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new AllocationEngine(new AllocationWeights(0.5m, 0.3m, 0.3m)));

        Assert.Contains("sum to one", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertReason(
        AllocationResult result,
        string batchCode,
        AllocationFilterReason expectedReason) =>
        Assert.Contains(result.FilterTrace,
            item => item.BatchCode == batchCode && !item.Passed && item.Reason == expectedReason);

    private static AllocationRequest Request(
        int requiredQuantity,
        decimal mlorRatio = 0.25m,
        DateOnly? requestedDeliveryDate = null) =>
        new("SKU001", requiredQuantity, 30, mlorRatio, Today, Today, requestedDeliveryDate);

    private static BatchCandidate Candidate(
        string batchCode,
        Guid? warehouseId = null,
        string warehouseCode = "WH-01",
        DateOnly? expiration = null,
        int available = 10,
        decimal distanceMeters = 5_000,
        int? transitDays = 1) =>
        new(Guid.NewGuid(), batchCode, warehouseId ?? Guid.NewGuid(), warehouseCode,
            expiration ?? Today.AddDays(20), available, distanceMeters, transitDays);
}
