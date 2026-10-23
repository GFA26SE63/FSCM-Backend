using FSCM.Application.Features.AllocationRuns;

namespace FSCM.UnitTests.AllocationRuns;

/// <summary>
/// F87: Admin decides on a plan version. Goods and price are one decision, nothing decides itself,
/// and a recalculation supersedes rather than edits.
/// </summary>
public sealed class AllocationPlanApprovalTests
{
    private static readonly DateTimeOffset Cutoff = new(2026, 10, 22, 5, 0, 0, TimeSpan.Zero);
    private static readonly Guid Admin = Guid.NewGuid();

    [Fact]
    public async Task Planning_puts_one_version_up_for_a_decision()
    {
        var fixture = new ApprovalFixture();
        var run = await fixture.PlanAsync();

        var versions = await fixture.Approvals.ListAsync(run.Id, CancellationToken.None);

        var version = Assert.Single(versions);
        Assert.Equal(1, version.VersionNumber);
        Assert.Equal(AllocationPlanVersionStatuses.AwaitingApproval, version.Status);
        Assert.Null(version.ApprovedAt);
    }

    [Fact]
    public async Task Approving_marks_the_version_and_the_run()
    {
        var fixture = new ApprovalFixture();
        var run = await fixture.PlanAsync();
        var version = (await fixture.Approvals.ListAsync(run.Id, CancellationToken.None)).Single();

        var approved = await fixture.Approvals.ApproveAsync(
            version.Id, version.Version, Admin, CancellationToken.None);
        var after = await fixture.Service.GetAsync(run.Id, CancellationToken.None);

        Assert.Equal(AllocationPlanVersionStatuses.Approved, approved.Status);
        Assert.Equal(Admin, approved.ApprovedByUserId);
        Assert.NotNull(approved.ApprovedAt);
        Assert.Equal(AllocationRunStatuses.Approved, after.Status);
    }

    [Fact]
    public async Task A_version_already_decided_cannot_be_decided_again()
    {
        var fixture = new ApprovalFixture();
        var run = await fixture.PlanAsync();
        var version = (await fixture.Approvals.ListAsync(run.Id, CancellationToken.None)).Single();
        var approved = await fixture.Approvals.ApproveAsync(
            version.Id, version.Version, Admin, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<AllocationRunOperationException>(() =>
            fixture.Approvals.ApproveAsync(approved.Id, approved.Version, Admin, CancellationToken.None));

        Assert.Equal("conflict", exception.Code);
    }

    [Fact]
    public async Task An_approval_against_a_stale_read_is_refused()
    {
        var fixture = new ApprovalFixture();
        var run = await fixture.PlanAsync();
        var version = (await fixture.Approvals.ListAsync(run.Id, CancellationToken.None)).Single();

        var exception = await Assert.ThrowsAsync<AllocationRunOperationException>(() =>
            fixture.Approvals.ApproveAsync(version.Id, version.Version + 1, Admin, CancellationToken.None));

        Assert.Equal("conflict", exception.Code);
        Assert.Contains("reload", exception.Message);
    }

    [Fact]
    public async Task Requesting_a_recalculation_keeps_the_reason_and_reopens_the_run()
    {
        var fixture = new ApprovalFixture();
        var run = await fixture.PlanAsync();
        var version = (await fixture.Approvals.ListAsync(run.Id, CancellationToken.None)).Single();

        var sent = await fixture.Approvals.RequestRecalculationAsync(
            version.Id, version.Version, "Giá cao hơn dự kiến", Admin, CancellationToken.None);
        var after = await fixture.Service.GetAsync(run.Id, CancellationToken.None);

        Assert.Equal(AllocationPlanVersionStatuses.RecalculationRequested, sent.Status);
        Assert.Equal("Giá cao hơn dự kiến", sent.RecalculationReason);
        // Back to snapshotted so a fresh version can be built over the same locked orders.
        Assert.Equal(AllocationRunStatuses.Snapshotted, after.Status);
    }

    [Fact]
    public async Task A_recalculation_needs_a_reason()
    {
        var fixture = new ApprovalFixture();
        var run = await fixture.PlanAsync();
        var version = (await fixture.Approvals.ListAsync(run.Id, CancellationToken.None)).Single();

        var exception = await Assert.ThrowsAsync<AllocationRunOperationException>(() =>
            fixture.Approvals.RequestRecalculationAsync(
                version.Id, version.Version, "   ", Admin, CancellationToken.None));

        Assert.Equal("validation_error", exception.Code);
    }

    [Fact]
    public async Task Replanning_supersedes_the_old_version_instead_of_editing_it()
    {
        var fixture = new ApprovalFixture();
        var run = await fixture.PlanAsync();
        var first = (await fixture.Approvals.ListAsync(run.Id, CancellationToken.None)).Single();
        await fixture.Approvals.RequestRecalculationAsync(
            first.Id, first.Version, "Tính lại", Admin, CancellationToken.None);

        await fixture.Service.PlanAsync(run.Id, CancellationToken.None);
        var versions = await fixture.Approvals.ListAsync(run.Id, CancellationToken.None);

        Assert.Equal(2, versions.Count);
        Assert.Equal(AllocationPlanVersionStatuses.AwaitingApproval, versions.First().Status);
        Assert.Equal(2, versions.First().VersionNumber);
        // The rejected proposal is kept exactly as it was decided.
        var kept = versions.Single(item => item.VersionNumber == 1);
        Assert.Equal(AllocationPlanVersionStatuses.RecalculationRequested, kept.Status);
        Assert.Equal("Tính lại", kept.RecalculationReason);
    }

    [Fact]
    public async Task Deciding_an_unknown_version_reports_not_found()
    {
        var fixture = new ApprovalFixture();

        var exception = await Assert.ThrowsAsync<AllocationRunOperationException>(() =>
            fixture.Approvals.ApproveAsync(Guid.NewGuid(), 0, Admin, CancellationToken.None));

        Assert.Equal("not_found", exception.Code);
    }

    private sealed class ApprovalFixture : AllocationRunServiceTests.Fixture
    {
        public AllocationPlanApprovalService Approvals { get; }


        public ApprovalFixture()
        {
            var clock = new FixedClock();
            Reservations = new AllocationReservationService(Store, clock);
            Approvals = new AllocationPlanApprovalService(Store, Reservations, clock);
        }

        public AllocationReservationService Reservations { get; }

        public async Task<AllocationRunResult> PlanAsync()
        {
            AddOrder("DH-000001", Cutoff.AddHours(-1));
            var run = await Service.StartAsync(Cutoff, CancellationToken.None);
            await Service.PlanAsync(run.Id, CancellationToken.None);
            return run;
        }

        private sealed class FixedClock : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => Cutoff;
        }
    }
}
