using FSCM.Application.Features.AllocationRuns;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.AllocationRuns;

public sealed class AllocationRunClosureServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid RunId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ActorId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task A_finished_run_can_be_closed_and_the_closing_is_recorded()
    {
        var (service, store) = Build();

        var report = await service.CloseAsync(RunId, Close("Đã đối soát đợt"),
            ActorId, SystemRoles.Admin, CancellationToken.None);

        Assert.Equal(AllocationRunStatuses.Closed, report.Status);
        Assert.Equal(AllocationRunStatuses.Closed, store.Run!.Status);
        var audit = Assert.Single(store.Audits);
        Assert.Equal(AllocationRunClosureService.ClosureAction, audit.Action);
        Assert.Equal(RunId, audit.EntityId);
        Assert.Equal(ActorId, audit.UserAccountId);
        Assert.Equal("Đã đối soát đợt", audit.Reason);
        // The report tells the reader who closed it and why without a second call.
        Assert.Equal(Now, report.ClosedAt);
        Assert.Equal("Đã đối soát đợt", report.ClosureNote);
    }

    [Theory]
    [InlineData(AllocationRunStatuses.AwaitingApproval)]
    [InlineData(AllocationRunStatuses.Snapshotted)]
    [InlineData(AllocationRunStatuses.Planning)]
    [InlineData(AllocationRunStatuses.Failed)]
    public async Task A_run_that_was_never_approved_cannot_be_closed(string status)
    {
        var (service, store) = Build();
        store.Run!.Status = status;

        var error = await Assert.ThrowsAsync<AllocationRunOperationException>(() =>
            service.CloseAsync(RunId, Close(), ActorId, SystemRoles.Admin, CancellationToken.None));

        Assert.Equal("conflict", error.Code);
        Assert.Equal(status, store.Run.Status);
        Assert.Empty(store.Audits);
    }

    [Fact]
    public async Task Closing_a_run_twice_returns_it_closed_rather_than_refusing()
    {
        var (service, store) = Build();
        await service.CloseAsync(RunId, Close(), ActorId, SystemRoles.Admin, CancellationToken.None);

        // A double click on a report that took a moment to load should not read as an error.
        var again = await service.CloseAsync(
            RunId, Close(), ActorId, SystemRoles.Admin, CancellationToken.None);

        Assert.Equal(AllocationRunStatuses.Closed, again.Status);
        Assert.Empty(again.Blockers);
        Assert.Single(store.Audits);
    }

    [Fact]
    public async Task A_run_with_a_warehouse_still_picking_is_not_finished()
    {
        var (service, store) = Build();
        store.UnfinishedPickingSheets = ["PK-20261104-01A"];

        var report = await service.GetReportAsync(RunId, CancellationToken.None);

        Assert.False(report.CanClose);
        Assert.Contains("phiếu soạn", Assert.Single(report.Blockers));
        await Assert.ThrowsAsync<AllocationRunOperationException>(() =>
            service.CloseAsync(RunId, Close(), ActorId, SystemRoles.Admin, CancellationToken.None));
        Assert.Equal(AllocationRunStatuses.Approved, store.Run!.Status);
    }

    [Fact]
    public async Task A_run_whose_goods_are_still_on_the_road_is_not_finished()
    {
        var (service, store) = Build();
        store.ShipmentsInFlight = ["SH-0001", "SH-0002"];

        var report = await service.GetReportAsync(RunId, CancellationToken.None);

        Assert.False(report.CanClose);
        Assert.Contains("phần giao", Assert.Single(report.Blockers));
    }

    [Fact]
    public async Task A_run_still_holding_stock_is_not_finished()
    {
        var (service, store) = Build();
        store.StillHeldReservationCount = 2;
        store.Stock = [StockRow(planned: 50m, held: 50m, issued: 30m, released: 0m, stillHeld: 20m)];

        var report = await service.GetReportAsync(RunId, CancellationToken.None);

        // Closing here would leave 20 cartons held in the name of a run nobody looks at any more -
        // the same orphaned hold F94 exists to prevent.
        Assert.False(report.CanClose);
        var blocker = Assert.Single(report.Blockers);
        Assert.Contains("20", blocker);
        Assert.Contains("giữ tồn", blocker);
    }

    [Fact]
    public async Task An_exception_nobody_wrote_down_keeps_the_run_open()
    {
        var (service, _) = Build(store => store.Exceptions =
        [
            new RunReportException(RunExceptionKinds.Shipment, "SH-0001", "Exception", IsRecorded: false)
        ]);

        var report = await service.GetReportAsync(RunId, CancellationToken.None);

        Assert.False(report.CanClose);
        Assert.Contains("SH-0001", Assert.Single(report.Blockers));
    }

    [Fact]
    public async Task An_exception_that_has_been_written_down_does_not()
    {
        var (service, _) = Build(store => store.Exceptions =
        [
            new RunReportException(RunExceptionKinds.Shipment, "SH-0001", "Exception", IsRecorded: true)
        ]);

        var report = await service.GetReportAsync(RunId, CancellationToken.None);

        // F95 closes a run whose discrepancies are reconciled *or recorded as handled*. A reason on
        // the exception is that record; the exception itself still shows on the report.
        Assert.True(report.CanClose);
        Assert.Single(report.Exceptions);
    }

    [Fact]
    public async Task An_open_complaint_or_refund_does_not_stand_in_the_way_but_stays_on_the_report()
    {
        var (service, store) = Build(store => store.OpenObligations =
        [
            new RunReportObligation(RunObligationKinds.Complaint, "abc", "DH-001038", null, "Open"),
            new RunReportObligation(RunObligationKinds.Refund, "def", "DH-001039", 250_000m, "PendingRefund")
        ]);

        var report = await service.CloseAsync(
            RunId, Close(), ActorId, SystemRoles.Admin, CancellationToken.None);

        // These change what is owed, not what the run moved, and they outlive the run by design.
        // Closing must not make them unreachable, so the report keeps carrying them.
        Assert.Equal(AllocationRunStatuses.Closed, report.Status);
        Assert.Equal(2, report.OpenObligations.Count);
        Assert.Equal(250_000m, report.OpenObligations.Single(
            item => item.Kind == RunObligationKinds.Refund).Amount);
    }

    [Fact]
    public async Task Closing_writes_no_history_away()
    {
        var (service, store) = Build(store =>
        {
            store.Orders = [OrderRow(AllocationRunOrderStatuses.Fulfilled, null)];
            store.Stock = [StockRow(planned: 50m, held: 50m, issued: 50m, released: 0m, stillHeld: 0m)];
            store.Exceptions =
            [
                new RunReportException(RunExceptionKinds.PickingSheet, "PK-01A", "ShortPick", true)
            ];
        });

        var report = await service.CloseAsync(
            RunId, Close(), ActorId, SystemRoles.Admin, CancellationToken.None);

        // The status moves; everything the run did stays exactly as it was.
        Assert.Single(report.Orders);
        Assert.Single(report.Stock);
        Assert.Single(report.Exceptions);
        Assert.Equal(50m, report.IssuedQuantity);
    }

    [Fact]
    public async Task The_report_counts_the_orders_the_run_served_and_the_ones_it_could_not()
    {
        var (service, _) = Build(store => store.Orders =
        [
            OrderRow(AllocationRunOrderStatuses.Fulfilled, null),
            OrderRow(AllocationRunOrderStatuses.Fulfilled, null),
            OrderRow(AllocationRunOrderStatuses.Short, "Không đủ hàng đạt MLOR")
        ]);

        var report = await service.GetReportAsync(RunId, CancellationToken.None);

        Assert.Equal(2, report.FulfilledOrderCount);
        Assert.Equal(1, report.ShortOrderCount);
        Assert.Equal("Không đủ hàng đạt MLOR",
            report.Orders.Single(row => row.RunOrderStatus == AllocationRunOrderStatuses.Short)
                .ShortageReason);
    }

    [Fact]
    public async Task Only_Admin_or_Operator_can_close_a_run()
    {
        var (service, store) = Build();

        var error = await Assert.ThrowsAsync<AllocationRunOperationException>(() =>
            service.CloseAsync(
                RunId, Close(), ActorId, SystemRoles.WarehouseKeeper, CancellationToken.None));

        Assert.Equal("forbidden", error.Code);
        Assert.Equal(AllocationRunStatuses.Approved, store.Run!.Status);
    }

    [Fact]
    public async Task A_run_read_before_somebody_else_touched_it_cannot_be_closed_on_stale_numbers()
    {
        var (service, store) = Build();
        store.Run!.Version = 7;

        var error = await Assert.ThrowsAsync<AllocationRunOperationException>(() =>
            service.CloseAsync(
                RunId, new CloseAllocationRunData(4, null), ActorId, SystemRoles.Admin,
                CancellationToken.None));

        Assert.Equal("conflict", error.Code);
        Assert.Equal(AllocationRunStatuses.Approved, store.Run.Status);
    }

    [Fact]
    public async Task An_unknown_run_is_a_not_found_rather_than_an_empty_report()
    {
        var (service, _) = Build();

        var error = await Assert.ThrowsAsync<AllocationRunOperationException>(() =>
            service.GetReportAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal("not_found", error.Code);
    }

    /// <summary>A run that has nothing left in flight, unless a test says otherwise.</summary>
    private static (AllocationRunClosureService Service, FakeAllocationRunReportStore Store) Build(
        Action<FakeAllocationRunReportStore>? arrange = null)
    {
        var store = new FakeAllocationRunReportStore
        {
            Run = new AllocationRun
            {
                Id = RunId,
                RunCode = "DOT-20261104-01",
                BusinessDate = new DateOnly(2026, 11, 4),
                CutoffAt = new DateTimeOffset(2026, 11, 3, 17, 0, 0, TimeSpan.Zero),
                Status = AllocationRunStatuses.Approved,
                AlgorithmSettings = "{}",
                OrderCount = 3,
                StartedAt = Now.AddHours(-8),
                Version = 4
            }
        };
        arrange?.Invoke(store);
        return (new AllocationRunClosureService(store, new FixedClock()), store);
    }

    private static CloseAllocationRunData Close(string? reason = null) => new(4, reason);

    private static RunReportOrderRow OrderRow(string runStatus, string? shortageReason) => new(
        Guid.NewGuid(), "DH-001038", "Cửa hàng A", 1, runStatus,
        shortageReason is null ? "Delivered" : "PendingAllocation", shortageReason, 1_000_000m);

    private static RunReportStockRow StockRow(
        decimal planned, decimal held, decimal issued, decimal released, decimal stillHeld) => new(
        Guid.NewGuid(), "WH-TD", Guid.NewGuid(), "B-001", "SKU001",
        planned, held, issued, released, stillHeld);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
