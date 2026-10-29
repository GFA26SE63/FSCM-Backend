using FSCM.Domain.Allocation;

namespace FSCM.UnitTests.Allocation;

public sealed class AllocationEngineTests
{
    private const int ShelfLifeHours = 720; // 30 days
    private static readonly DateTimeOffset Arrival = new(2026, 10, 24, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly ArrivalDate = new(2026, 10, 24);

    [Fact]
    public void Every_hard_filter_records_why_a_batch_was_dropped()
    {
        var engine = new AllocationEngine();
        var candidates = new[]
        {
            Candidate("B-EMPTY", available: 0m),
            Candidate("B-NO-ROUTE", noRoute: true),
            Candidate("B-FAR", distanceMeters: 20_001m),
            Candidate("B-LATE", arrivalDate: ArrivalDate.AddDays(5)),
            Candidate("B-THIN", remainingLifeHours: 100),
            Candidate("B-OK")
        };

        var result = engine.Allocate(Request(10m, deadline: ArrivalDate.AddDays(2)), candidates);

        Assert.True(result.IsFullyAllocated);
        Assert.Equal("B-OK", Assert.Single(result.Allocations).BatchCode);
        Assert.Contains(result.FilterTrace, item =>
            item.BatchCode == "B-EMPTY" && item.Reason == AllocationFilterReason.NoRemainingQuantity);
        Assert.Contains(result.FilterTrace, item =>
            item.BatchCode == "B-NO-ROUTE" && item.Reason == AllocationFilterReason.RouteUnavailable);
        Assert.Contains(result.FilterTrace, item =>
            item.BatchCode == "B-FAR" && item.Reason == AllocationFilterReason.OutsideServiceRadius);
        Assert.Contains(result.FilterTrace, item =>
            item.BatchCode == "B-LATE" && item.Reason == AllocationFilterReason.ArrivesAfterDeadline);
        Assert.Contains(result.FilterTrace, item =>
            item.BatchCode == "B-THIN" && item.Reason == AllocationFilterReason.BelowMlor);
    }

    [Fact]
    public void Mlor_is_measured_in_hours_so_a_same_day_difference_still_counts()
    {
        var engine = new AllocationEngine();
        // MLOR 25% of 720 h = 180 h. Both batches would round to the same number of days.
        var justUnder = Candidate("B-UNDER", remainingLifeHours: 179);
        var justOver = Candidate("B-OVER", remainingLifeHours: 181);

        var under = engine.Allocate(Request(5m, mlorRatio: 0.25m), [justUnder]);
        var over = engine.Allocate(Request(5m, mlorRatio: 0.25m), [justOver]);

        Assert.False(under.IsFullyAllocated);
        Assert.True(over.IsFullyAllocated);
        Assert.Equal(180m, over.RequiredShelfLifeHours);
    }

    [Fact]
    public void A_nearer_warehouse_beats_a_shorter_dated_batch_at_the_default_weights()
    {
        var engine = new AllocationEngine();
        // Section 4.1: A has 25% life left 18 km away, B has 45% left 3 km away. B wins on S.
        var batchA = Candidate("A", remainingLifeHours: 180, distanceMeters: 18_000m, available: 50m);
        var batchB = Candidate("B", remainingLifeHours: 324, distanceMeters: 3_000m, available: 50m);

        var result = engine.Allocate(Request(50m, mlorRatio: 0.20m), [batchA, batchB]);

        Assert.Equal("B", Assert.Single(result.Allocations).BatchCode);
        var scores = result.ScoreTrace.Where(item => item.Iteration == 1).ToArray();
        Assert.True(scores.Single(item => item.BatchCode == "B").WeightedScore
            > scores.Single(item => item.BatchCode == "A").WeightedScore);
    }

    [Fact]
    public void Coverage_is_recomputed_after_every_take()
    {
        var engine = new AllocationEngine();
        var small = Candidate("B-SMALL", available: 4m, distanceMeters: 1_000m);
        var large = Candidate("B-LARGE", available: 10m, distanceMeters: 19_000m);

        var result = engine.Allocate(Request(10m), [small, large]);

        Assert.True(result.IsFullyAllocated);
        Assert.Equal(10m, result.AllocatedQuantity);
        Assert.Equal(2, result.Allocations.Count);
        // The second turn scores against the 6 still missing, not the original 10.
        var secondTurn = result.ScoreTrace.Where(item => item.Iteration == 2).ToArray();
        Assert.All(secondTurn, item => Assert.Equal(6m, item.RemainingDemand));
    }

    [Fact]
    public void A_line_that_cannot_be_filled_returns_nothing_to_commit()
    {
        var engine = new AllocationEngine();

        var result = engine.Allocate(Request(30m), [Candidate("B-ONLY", available: 12m)]);

        Assert.False(result.IsFullyAllocated);
        Assert.Equal(18m, result.ShortageQuantity);
        Assert.Empty(result.Allocations);
        // The attempts are still visible so a trace can explain the shortage.
        Assert.NotEmpty(result.ScoreTrace);
    }

    [Fact]
    public void Equal_scores_break_on_batch_code_so_a_rerun_matches()
    {
        var engine = new AllocationEngine();
        var first = Candidate("B-002", available: 10m);
        var second = Candidate("B-001", available: 10m);

        var result = engine.Allocate(Request(10m), [first, second]);

        Assert.Equal("B-001", Assert.Single(result.Allocations).BatchCode);
    }

    [Fact]
    public void Weights_that_do_not_sum_to_one_are_refused()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new AllocationEngine(new AllocationWeights(0.5m, 0.3m, 0.3m)));

        Assert.Contains("sum to one", exception.Message);
    }

    [Fact]
    public void An_mlor_ratio_outside_zero_to_one_is_refused()
    {
        var engine = new AllocationEngine();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            engine.Allocate(Request(5m, mlorRatio: 1m), [Candidate("B-OK")]));
    }

    private static AllocationRequest Request(
        decimal quantity, decimal mlorRatio = 0.25m, DateOnly? deadline = null) =>
        new("SKU001", quantity, ShelfLifeHours, mlorRatio, deadline);

    /// <summary>
    /// A candidate that arrives at <see cref="Arrival"/> with the given hours of life left.
    /// Pass noRoute to drop the arrival, or arrivalDate to move only the calendar date the
    /// deadline is compared against.
    /// </summary>
    private static BatchCandidate Candidate(
        string code,
        decimal available = 100m,
        decimal distanceMeters = 5_000m,
        int remainingLifeHours = 400,
        bool noRoute = false,
        DateOnly? arrivalDate = null) =>
        new(Guid.NewGuid(),
            code,
            Guid.NewGuid(),
            "WH-01",
            Arrival.AddHours(remainingLifeHours),
            available,
            distanceMeters,
            noRoute ? null : Arrival,
            noRoute ? null : arrivalDate ?? ArrivalDate);
}
