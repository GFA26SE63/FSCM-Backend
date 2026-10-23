using FSCM.Application.Features.AllocationRuns;
using FSCM.Domain.Allocation;

namespace FSCM.UnitTests.AllocationRuns;

/// <summary>
/// The orchestration side of F85: a snapshotted run is planned once, and only once.
/// Batch choice and pricing have their own tests; this covers the wiring and the guard.
/// </summary>
public sealed class AllocationRunPlanningTests
{
    private static readonly DateTimeOffset Cutoff = new(2026, 10, 22, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Planning_a_run_that_is_not_snapshotted_is_refused()
    {
        var fixture = new PlanningFixture();
        var run = await fixture.StartAsync();
        await fixture.Service.PlanAsync(run.Id, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<AllocationRunOperationException>(() =>
            fixture.Service.PlanAsync(run.Id, CancellationToken.None));

        Assert.Equal("conflict", exception.Code);
        Assert.Contains("only a snapshotted run", exception.Message);
    }

    [Fact]
    public async Task Planning_an_unknown_run_reports_not_found()
    {
        var fixture = new PlanningFixture();

        var exception = await Assert.ThrowsAsync<AllocationRunOperationException>(() =>
            fixture.Service.PlanAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal("not_found", exception.Code);
    }

    [Fact]
    public async Task A_run_whose_warehouses_cannot_be_reached_marks_its_orders_short()
    {
        // The fixture publishes no ETA schedule, so every leg is unreachable and nothing can be
        // allocated. The run must still complete and explain itself rather than fail.
        var fixture = new PlanningFixture();
        var run = await fixture.StartAsync();

        var pricing = await fixture.Service.PlanAsync(run.Id, CancellationToken.None);
        var planned = await fixture.Service.GetAsync(run.Id, CancellationToken.None);

        Assert.Empty(pricing.Orders);
        Assert.Equal(AllocationRunStatuses.AwaitingApproval, planned.Status);
        Assert.All(planned.Orders, order =>
            Assert.Equal(AllocationRunOrderStatuses.Short, order.Status));
        Assert.All(planned.Orders, order => Assert.NotNull(order.ShortageReason));
    }

    [Fact]
    public async Task A_planned_run_moves_to_awaiting_approval_and_reserves_nothing()
    {
        var fixture = new PlanningFixture();
        var run = await fixture.StartAsync();

        await fixture.Service.PlanAsync(run.Id, CancellationToken.None);
        var planned = await fixture.Service.GetAsync(run.Id, CancellationToken.None);

        Assert.Equal(AllocationRunStatuses.AwaitingApproval, planned.Status);
        // The pool is the run's own copy and no real balance was touched.
        Assert.All(planned.Pool, row => Assert.Equal(row.SnapshotQuantity, row.RemainingQuantity));
    }

    [Fact]
    public async Task Planning_records_why_each_line_came_out_the_way_it_did()
    {
        // No ETA schedule, so every leg is unreachable: the interesting part is that the trace says
        // which batches were looked at and what stopped them, not just that the order went short.
        var fixture = new PlanningFixture();
        var run = await fixture.StartAsync();

        await fixture.Service.PlanAsync(run.Id, CancellationToken.None);

        var trace = Assert.Single(fixture.Store.LineTraces);
        Assert.Equal(AllocationLineTraceOutcomes.Short, trace.Outcome);
        Assert.Equal(0m, trace.DrawnQuantity);
        Assert.NotNull(trace.Note);
        // The weights and the MLOR in hours are frozen on the trace, not read back later.
        Assert.Equal(0.5m, trace.ShelfLifeWeight);
        Assert.True(trace.RequiredShelfLifeHours > 0);

        var rejected = Assert.Single(fixture.Store.TraceCandidates,
            item => item.Stage == nameof(AllocationFilterStage.Distance));
        Assert.False(rejected.Passed);
        Assert.Equal(nameof(AllocationFilterReason.RouteUnavailable), rejected.Reason);
        // Nothing survived the filters, so there was never a ranking to record.
        Assert.Empty(fixture.Store.TraceScores);
    }

    private sealed class PlanningFixture : AllocationRunServiceTests.Fixture
    {
        public Task<AllocationRunResult> StartAsync()
        {
            AddOrder("DH-000001", Cutoff.AddHours(-1));
            return Service.StartAsync(Cutoff, CancellationToken.None);
        }
    }
}
