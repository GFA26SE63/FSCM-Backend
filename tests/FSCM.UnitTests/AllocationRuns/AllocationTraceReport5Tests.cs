using FSCM.Application.Features.AllocationRuns;
using FSCM.Domain.Allocation;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.AllocationRuns;

public sealed class AllocationTraceReport5Tests
{
    private static readonly DateTimeOffset Eta = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Report5Case", "UT-Allocation Trace-UTCID01")]
    public void Sku_mlor_rejection_traces_effective_rule_version_before_scoring()
    {
        var frozenLine = new SalesOrderLine
        {
            Id = Guid.NewGuid(), ProductId = Guid.NewGuid(), SkuSnapshot = "SKU-MLOR-70",
            OrderedQuantity = 5m, MlorSourceScope = "Sku", MlorSourceRuleId = Guid.NewGuid(),
            MlorSourceVersion = 7, MlorMinimumLifeRatio = 0.7m, MlorResolvedAt = Eta.AddMinutes(-1)
        };
        var result = new AllocationEngine().Allocate(
            new(frozenLine.SkuSnapshot, frozenLine.OrderedQuantity, 100, frozenLine.MlorMinimumLifeRatio, null),
            [Candidate("BELOW-SKU-MLOR", available: 5m, remainingLifeHours: 69)]);

        var rejection = Assert.Single(result.FilterTrace, item => !item.Passed);
        Assert.Equal(AllocationFilterStage.Delivery, rejection.Stage);
        Assert.Equal(AllocationFilterReason.BelowMlor, rejection.Reason);
        Assert.Equal(69m, rejection.RemainingShelfLifeHours);
        Assert.Equal(70m, rejection.RequiredShelfLifeHours);
        Assert.Empty(result.ScoreTrace);
        Assert.Equal("Sku", frozenLine.MlorSourceScope);
        Assert.Equal(7, frozenLine.MlorSourceVersion);

        // The immutable trace contract must expose the frozen version, not only its ratio/source.
        Assert.NotNull(typeof(TraceLineResult).GetProperty("MlorSourceVersion"));
    }

    [Fact]
    [Trait("Report5Case", "UT-Allocation Trace-UTCID02")]
    public void Mixed_candidates_trace_expired_locked_missing_distance_out_of_range_and_eligible_reasons()
    {
        var result = new AllocationEngine().Allocate(new("SKU-TRACE", 5m, 100, 0.2m, null),
        [
            Candidate("EXPIRED", available: 5m, remainingLifeHours: -1),
            Candidate("INVENTORY-LOCKED", available: 0m, remainingLifeHours: 80),
            Candidate("MAP-DISTANCE-MISSING", available: 5m, remainingLifeHours: 80, noRoute: true),
            Candidate("OUTSIDE-RADIUS", available: 5m, remainingLifeHours: 80, distanceMeters: 20_001m),
            Candidate("ELIGIBLE", available: 5m, remainingLifeHours: 80, distanceMeters: 5_000m)
        ]);

        string RejectionReason(string code) => result.FilterTrace.Single(item =>
            item.BatchCode == code && !item.Passed).Reason.ToString();
        Assert.Equal("Expired", RejectionReason("EXPIRED"));
        Assert.Equal("InventoryLocked", RejectionReason("INVENTORY-LOCKED"));
        Assert.Equal(nameof(AllocationFilterReason.RouteUnavailable), RejectionReason("MAP-DISTANCE-MISSING"));
        Assert.Equal(nameof(AllocationFilterReason.OutsideServiceRadius), RejectionReason("OUTSIDE-RADIUS"));
        Assert.DoesNotContain(result.FilterTrace, item =>
            item.BatchCode == "MAP-DISTANCE-MISSING" && item.Passed);
        Assert.Equal("ELIGIBLE", Assert.Single(result.Allocations).BatchCode);
    }

    [Fact]
    [Trait("Report5Case", "UT-Allocation Trace-UTCID03")]
    public void Weighted_components_are_snapshotted_and_choose_one_deterministic_batch_for_the_so()
    {
        var result = new AllocationEngine(new(0.5m, 0.3m, 0.2m, 20_000m)).Allocate(
            new("SKU-SCORED", 6m, 100, 0.2m, null),
            [Candidate("B-FEFO", 6m, 30, 12_000m), Candidate("B-NEAR", 6m, 70, 1_000m)]);

        var firstIteration = result.ScoreTrace.Where(item => item.Iteration == 1).OrderBy(item => item.Rank).ToArray();
        Assert.Equal(2, firstIteration.Length);
        Assert.NotEqual(firstIteration[0].ShelfLifeScore, firstIteration[1].ShelfLifeScore);
        Assert.NotEqual(firstIteration[0].DistanceScore, firstIteration[1].DistanceScore);
        Assert.All(firstIteration, score =>
        {
            Assert.Equal(1m, score.CoverageScore);
            Assert.Equal(decimal.Round(0.5m * score.ShelfLifeScore + 0.3m * score.DistanceScore +
                0.2m * score.CoverageScore, 6), score.WeightedScore);
        });
        var selected = Assert.Single(firstIteration, item => item.Selected);
        Assert.Equal(selected.BatchId, Assert.Single(result.Allocations).BatchId);
        Assert.Equal(6m, selected.SelectedQuantity);

        var persisted = firstIteration.Select(score => new AllocationTraceScore
        {
            Id = Guid.NewGuid(), AllocationLineTraceId = Guid.NewGuid(), Iteration = score.Iteration,
            Rank = score.Rank, BatchId = score.BatchId, BatchCodeSnapshot = score.BatchCode,
            WarehouseId = score.WarehouseId, WarehouseCodeSnapshot = score.WarehouseCode,
            RemainingDemand = score.RemainingDemand, ShelfLifeScore = score.ShelfLifeScore,
            DistanceScore = score.DistanceScore, CoverageScore = score.CoverageScore,
            WeightedScore = score.WeightedScore, Selected = score.Selected,
            SelectedQuantity = score.SelectedQuantity, CreatedAt = Eta
        }).ToArray();
        Assert.Equal(firstIteration.Select(item => item.WeightedScore), persisted.Select(item => item.WeightedScore));
        Assert.Single(persisted, item => item.Selected);
    }

    private static BatchCandidate Candidate(
        string code,
        decimal available,
        int remainingLifeHours,
        decimal distanceMeters = 1_000m,
        bool noRoute = false) => new(
            Guid.NewGuid(), code, Guid.NewGuid(), $"WH-{code}", Eta.AddHours(remainingLifeHours),
            available, distanceMeters, noRoute ? null : Eta, noRoute ? null : DateOnly.FromDateTime(Eta.Date));
}
