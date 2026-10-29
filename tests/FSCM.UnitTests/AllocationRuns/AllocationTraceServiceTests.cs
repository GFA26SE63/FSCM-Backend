using FSCM.Application.Features.AllocationRuns;
using FSCM.Domain.Allocation;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.AllocationRuns;

public sealed class AllocationTraceServiceTests
{
    private static readonly Guid Order = Guid.NewGuid();

    [Fact]
    public async Task A_rejected_batch_is_kept_with_the_reason_it_was_ruled_out()
    {
        var line = Line(1, "SKU001", ordered: 10m);
        var store = new FakeStore(View(AllocationPlanVersionStatuses.Approved, [
            new LineFixture(line, AllocationLineTraceOutcomes.Allocated, Drawn: 10m, [
                Candidate(line, "LO-0001", AllocationFilterStage.Pool, true, AllocationFilterReason.Eligible),
                Candidate(line, "LO-0002", AllocationFilterStage.Pool, false, AllocationFilterReason.NoRemainingQuantity),
                Candidate(line, "LO-0001", AllocationFilterStage.Delivery, false, AllocationFilterReason.BelowMlor,
                    remaining: 100m, required: 216m)],
                [])]));

        var trace = await new AllocationTraceService(store).GetByOrderAsync(Order, default);

        var candidates = Assert.Single(trace.Lines).Candidates;
        Assert.Equal(3, candidates.Count);
        var belowMlor = candidates.Single(item => item.Reason == nameof(AllocationFilterReason.BelowMlor));
        Assert.False(belowMlor.Passed);
        // Enough to see the rejection was right, not just that it happened.
        Assert.Equal(100m, belowMlor.RemainingShelfLifeHours);
        Assert.Equal(216m, belowMlor.RequiredShelfLifeHours);
    }

    [Fact]
    public async Task Candidates_come_back_in_the_order_the_filters_were_applied()
    {
        var line = Line(1, "SKU001", ordered: 1m);
        var store = new FakeStore(View(AllocationPlanVersionStatuses.Approved, [
            new LineFixture(line, AllocationLineTraceOutcomes.Allocated, Drawn: 1m, [
                Candidate(line, "LO-0001", AllocationFilterStage.Delivery, true, AllocationFilterReason.Eligible),
                Candidate(line, "LO-0001", AllocationFilterStage.Pool, true, AllocationFilterReason.Eligible),
                Candidate(line, "LO-0001", AllocationFilterStage.Distance, true, AllocationFilterReason.Eligible)],
                [])]));

        var trace = await new AllocationTraceService(store).GetByOrderAsync(Order, default);

        Assert.Equal(
            [nameof(AllocationFilterStage.Pool), nameof(AllocationFilterStage.Distance), nameof(AllocationFilterStage.Delivery)],
            Assert.Single(trace.Lines).Candidates.Select(item => item.Stage));
    }

    [Fact]
    public async Task Losing_scores_are_kept_alongside_the_winner_of_each_turn()
    {
        var line = Line(1, "SKU001", ordered: 10m);
        var store = new FakeStore(View(AllocationPlanVersionStatuses.Approved, [
            new LineFixture(line, AllocationLineTraceOutcomes.Allocated, Drawn: 10m, [], [
                Score(line, iteration: 1, rank: 2, "LO-0002", weighted: 0.41m, selected: false, quantity: 0m),
                Score(line, iteration: 1, rank: 1, "LO-0001", weighted: 0.82m, selected: true, quantity: 6m),
                Score(line, iteration: 2, rank: 1, "LO-0002", weighted: 0.55m, selected: true, quantity: 4m)])]));

        var trace = await new AllocationTraceService(store).GetByOrderAsync(Order, default);

        var scores = Assert.Single(trace.Lines).Scores;
        Assert.Equal([(1, 1), (1, 2), (2, 1)], scores.Select(item => (item.Iteration, item.Rank)));
        // The same batch scores differently once the shortfall shrinks, which is why the turn is
        // part of the record.
        var second = scores.Where(item => item.BatchCode == "LO-0002").ToArray();
        Assert.Equal(0.41m, second[0].WeightedScore);
        Assert.Equal(0.55m, second[1].WeightedScore);
    }

    [Fact]
    public async Task A_line_released_because_another_line_fell_short_says_so()
    {
        var first = Line(1, "SKU001", ordered: 10m);
        var second = Line(2, "SKU002", ordered: 5m);
        var store = new FakeStore(View(AllocationPlanVersionStatuses.AwaitingApproval, [
            // The first line drew its stock, then the second fell short, so an all-or-nothing order
            // hands the first line's draw back to the pool.
            new LineFixture(first, AllocationLineTraceOutcomes.Released, Drawn: 10m, [], [],
                Note: "Trả 10 về quỹ chung vì đơn DH-000001 thiếu ở dòng khác."),
            new LineFixture(second, AllocationLineTraceOutcomes.Short, Drawn: 0m, [], [],
                Shortage: 5m, Note: "Dòng 2 (SKU002) thiếu 5 trên 5.")]));

        var trace = await new AllocationTraceService(store).GetByOrderAsync(Order, default);

        var released = trace.Lines.Single(item => item.LineNumber == 1);
        Assert.Equal(AllocationLineTraceOutcomes.Released, released.Outcome);
        Assert.Equal(10m, released.DrawnQuantity);
        Assert.Contains("về quỹ chung", released.Note);

        var shortLine = trace.Lines.Single(item => item.LineNumber == 2);
        Assert.Equal(AllocationLineTraceOutcomes.Short, shortLine.Outcome);
        Assert.Equal(5m, shortLine.ShortageQuantity);
    }

    [Fact]
    public async Task The_mlor_the_run_froze_is_reported_with_its_source()
    {
        var line = Line(1, "SKU001", ordered: 1m, mlorSource: MlorSources.RetailerCategory, mlorRatio: 0.3m);
        var store = new FakeStore(View(AllocationPlanVersionStatuses.Approved, [
            new LineFixture(line, AllocationLineTraceOutcomes.Allocated, Drawn: 1m, [], [], Required: 216m)]));

        var trace = await new AllocationTraceService(store).GetByOrderAsync(Order, default);

        var only = Assert.Single(trace.Lines);
        Assert.Equal(MlorSources.RetailerCategory, only.MlorSource);
        Assert.Equal(0.3m, only.EffectiveMlorRatio);
        // 30% of a 720 h life, which is what candidates were measured against in hours.
        Assert.Equal(720, only.TotalShelfLifeHours);
        Assert.Equal(216m, only.RequiredShelfLifeHours);
    }

    [Fact]
    public async Task The_weights_the_run_used_travel_with_the_line()
    {
        var line = Line(1, "SKU001", ordered: 1m);
        var store = new FakeStore(View(AllocationPlanVersionStatuses.Approved, [
            new LineFixture(line, AllocationLineTraceOutcomes.Allocated, Drawn: 1m, [], [])]));

        var weights = Assert.Single((await new AllocationTraceService(store)
            .GetByOrderAsync(Order, default)).Lines).Weights;

        // A weight changed next week must not restate a decision already taken.
        Assert.Equal(0.5m, weights.ShelfLife);
        Assert.Equal(0.3m, weights.Distance);
        Assert.Equal(0.2m, weights.Coverage);
        Assert.Equal(20_000m, weights.MaximumDistanceMeters);
    }

    [Fact]
    public async Task A_trace_from_a_version_still_awaiting_approval_is_not_reported_as_approved()
    {
        var line = Line(1, "SKU001", ordered: 1m);
        var store = new FakeStore(View(AllocationPlanVersionStatuses.AwaitingApproval, [
            new LineFixture(line, AllocationLineTraceOutcomes.Allocated, Drawn: 1m, [], [])]));

        var trace = await new AllocationTraceService(store).GetByOrderAsync(Order, default);

        Assert.False(trace.IsApproved);
        Assert.Equal(AllocationPlanVersionStatuses.AwaitingApproval, trace.PlanVersionStatus);
    }

    [Fact]
    public async Task An_order_no_run_ever_touched_is_not_found()
    {
        var error = await Assert.ThrowsAsync<AllocationRunOperationException>(
            () => new AllocationTraceService(new FakeStore(null)).GetByOrderAsync(Order, default));

        Assert.Equal("not_found", error.Code);
    }

    private static AllocationRunLine Line(
        int lineNumber,
        string sku,
        decimal ordered,
        string mlorSource = MlorSources.RetailerProduct,
        decimal mlorRatio = 0.5m) =>
        new()
        {
            Id = Guid.NewGuid(),
            SalesOrderLineId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            SkuSnapshot = sku,
            LineNumber = lineNumber,
            OrderedQuantity = ordered,
            TotalShelfLifeHours = 720,
            EffectiveMlorRatio = mlorRatio,
            MlorSource = mlorSource,
        };

    private static AllocationTraceCandidate Candidate(
        AllocationRunLine line,
        string batchCode,
        AllocationFilterStage stage,
        bool passed,
        AllocationFilterReason reason,
        decimal? remaining = null,
        decimal? required = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            BatchId = Guid.NewGuid(),
            BatchCodeSnapshot = batchCode,
            WarehouseId = Guid.NewGuid(),
            WarehouseCodeSnapshot = "KHO-01",
            Stage = stage.ToString(),
            Passed = passed,
            Reason = reason.ToString(),
            RemainingShelfLifeHours = remaining,
            RequiredShelfLifeHours = required,
        };

    private static AllocationTraceScore Score(
        AllocationRunLine line,
        int iteration,
        int rank,
        string batchCode,
        decimal weighted,
        bool selected,
        decimal quantity) =>
        new()
        {
            Id = Guid.NewGuid(),
            Iteration = iteration,
            Rank = rank,
            BatchId = Guid.NewGuid(),
            BatchCodeSnapshot = batchCode,
            WarehouseId = Guid.NewGuid(),
            WarehouseCodeSnapshot = "KHO-01",
            RemainingDemand = 10m,
            WeightedScore = weighted,
            Selected = selected,
            SelectedQuantity = quantity,
        };

    private sealed record LineFixture(
        AllocationRunLine Line,
        string Outcome,
        decimal Drawn,
        IReadOnlyList<AllocationTraceCandidate> Candidates,
        IReadOnlyList<AllocationTraceScore> Scores,
        decimal Shortage = 0m,
        decimal Required = 360m,
        string? Note = null);

    private static AllocationOrderTraceView View(string status, IReadOnlyList<LineFixture> lines) =>
        new("DH-000001",
            Guid.NewGuid(),
            "PB-20261023-01",
            new AllocationPlanVersion
            {
                Id = Guid.NewGuid(),
                VersionNumber = 1,
                Status = status,
            },
            lines.Select(fixture =>
            {
                var traceId = Guid.NewGuid();
                return new AllocationLineTraceView(
                    new AllocationLineTrace
                    {
                        Id = traceId,
                        AllocationRunLineId = fixture.Line.Id,
                        Outcome = fixture.Outcome,
                        OrderedQuantity = fixture.Line.OrderedQuantity,
                        DrawnQuantity = fixture.Drawn,
                        ShortageQuantity = fixture.Shortage,
                        RequiredShelfLifeHours = fixture.Required,
                        ShelfLifeWeight = 0.5m,
                        DistanceWeight = 0.3m,
                        CoverageWeight = 0.2m,
                        MaximumDistanceMeters = 20_000m,
                        Note = fixture.Note,
                    },
                    fixture.Line,
                    Owned(fixture.Candidates, item => item.AllocationLineTraceId = traceId),
                    Owned(fixture.Scores, item => item.AllocationLineTraceId = traceId));
            }).ToArray());

    private static T[] Owned<T>(IReadOnlyList<T> items, Action<T> attach)
    {
        foreach (var item in items) attach(item);
        return items.ToArray();
    }

    private sealed class FakeStore(AllocationOrderTraceView? view) : StubAllocationRunStore
    {
        public override Task<AllocationOrderTraceView?> FindOrderTraceAsync(
            Guid salesOrderId, CancellationToken cancellationToken) => Task.FromResult(view);
    }
}
