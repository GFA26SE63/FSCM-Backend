using FSCM.Application.Features.AllocationRuns;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.AllocationRuns;

public sealed class AllocationRunResultTests
{
    private static readonly DateTimeOffset Cutoff = new(2026, 10, 26, 5, 0, 0, TimeSpan.Zero);
    private static readonly Guid Order = Guid.NewGuid();

    [Fact]
    public async Task A_fulfilled_order_reports_what_it_drew_and_what_it_costs()
    {
        var line = Line(1, "SKU001", ordered: 10m, allocated: 10m);
        var store = new FakeStore(View(
            AllocationRunOrderStatuses.Fulfilled, null,
            [(line, AllocationLineTraceOutcomes.Allocated, null, 800_000m)]));

        var result = await new AllocationRunResultService(store).GetByOrderAsync(Order, default);

        Assert.True(result.IsFulfilled);
        Assert.Equal(800_000m, result.FinalAmount);
        var only = Assert.Single(result.Lines);
        Assert.Equal(10m, only.AllocatedQuantity);
        Assert.Equal(0m, only.ShortageQuantity);
    }

    [Fact]
    public async Task A_fulfilled_order_is_holding_stock_and_a_short_one_is_not()
    {
        var kept = Line(1, "SKU001", ordered: 10m, allocated: 10m);
        var fulfilled = new FakeStore(View(
            AllocationRunOrderStatuses.Fulfilled, null,
            [(kept, AllocationLineTraceOutcomes.Allocated, null, 800_000m)]));

        var lost = Line(1, "SKU001", ordered: 10m, allocated: 0m);
        var short_ = new FakeStore(View(
            AllocationRunOrderStatuses.Short, "Dòng 1 (SKU001) thiếu 10 trên 10.",
            [(lost, AllocationLineTraceOutcomes.Short, "Dòng 1 (SKU001) thiếu 10 trên 10.", 0m)]));

        Assert.True((await new AllocationRunResultService(fulfilled).GetByOrderAsync(Order, default))
            .HasReservedStock);
        // Approval reserves only what it could fill completely, so a short order holds nothing.
        // A screen that showed reserved stock here would promise goods nobody set aside.
        Assert.False((await new AllocationRunResultService(short_).GetByOrderAsync(Order, default))
            .HasReservedStock);
    }

    [Fact]
    public async Task No_result_ever_offers_to_resend_keeping_its_place_in_the_queue()
    {
        var line = Line(1, "SKU001", ordered: 10m, allocated: 0m);
        var store = new FakeStore(View(
            AllocationRunOrderStatuses.Short, "Hết hàng hợp lệ.",
            [(line, AllocationLineTraceOutcomes.Short, "Hết hàng hợp lệ.", 0m)]));

        var result = await new AllocationRunResultService(store).GetByOrderAsync(Order, default);

        // Buying the rest means a new order, which the server timestamps on arrival and which
        // therefore queues behind whatever came in before it. Scope V10 removed the resend.
        Assert.False(result.CanResubmitKeepingPriority);
    }

    [Fact]
    public async Task Each_line_carries_its_own_outcome_and_reason()
    {
        var first = Line(1, "SKU001", ordered: 10m, allocated: 10m);
        var second = Line(2, "SKU002", ordered: 8m, allocated: 2m);
        var store = new FakeStore(View(
            AllocationRunOrderStatuses.Short, "Dòng 2 (SKU002) thiếu 6 trên 8.",
            [
                (first, AllocationLineTraceOutcomes.Released, "Trả 10 về quỹ chung vì đơn thiếu ở dòng khác.", 0m),
                (second, AllocationLineTraceOutcomes.Short, "Dòng 2 (SKU002) thiếu 6 trên 8.", 0m)
            ]));

        var result = await new AllocationRunResultService(store).GetByOrderAsync(Order, default);

        // The line that drew and lost it is Released, not Short: reading one outcome for the whole
        // order would hide which line actually failed.
        Assert.Equal(AllocationLineTraceOutcomes.Released, result.Lines.First().Outcome);
        Assert.Contains("về quỹ chung", result.Lines.First().Reason);
        Assert.Equal(6m, result.Lines.Last().ShortageQuantity);
    }

    [Fact]
    public async Task Lines_come_back_in_line_number_order()
    {
        var third = Line(3, "SKU003", ordered: 1m, allocated: 1m);
        var first = Line(1, "SKU001", ordered: 1m, allocated: 1m);
        var store = new FakeStore(View(
            AllocationRunOrderStatuses.Fulfilled, null,
            [
                (third, AllocationLineTraceOutcomes.Allocated, null, 10m),
                (first, AllocationLineTraceOutcomes.Allocated, null, 20m)
            ]));

        var result = await new AllocationRunResultService(store).GetByOrderAsync(Order, default);

        Assert.Equal([1, 3], result.Lines.Select(line => line.LineNumber));
    }

    [Fact]
    public async Task An_order_whose_plan_is_not_approved_yet_has_no_result()
    {
        // The store only serves an approved version; a proposal is the approval screen's business.
        var error = await Assert.ThrowsAsync<AllocationRunOperationException>(
            () => new AllocationRunResultService(new FakeStore(null)).GetByOrderAsync(Order, default));

        Assert.Equal("not_found", error.Code);
    }

    private static AllocationRunLine Line(int lineNumber, string sku, decimal ordered, decimal allocated) =>
        new()
        {
            Id = Guid.NewGuid(),
            SalesOrderLineId = Guid.NewGuid(),
            SkuSnapshot = sku,
            LineNumber = lineNumber,
            OrderedQuantity = ordered,
            AllocatedQuantity = allocated,
        };

    private static OrderRunResultView View(
        string orderStatus,
        string? shortageReason,
        IReadOnlyList<(AllocationRunLine Line, string Outcome, string? Note, decimal Amount)> lines)
    {
        var versionId = Guid.NewGuid();
        return new OrderRunResultView(
            new AllocationRunOrder
            {
                Id = Guid.NewGuid(),
                SalesOrderId = Order,
                OrderCodeSnapshot = "DH-000001",
                Status = orderStatus,
                ShortageReason = shortageReason,
            },
            new AllocationRun
            {
                Id = Guid.NewGuid(),
                RunCode = "PB-20261026-01",
                BusinessDate = new DateOnly(2026, 10, 26),
                CutoffAt = Cutoff,
            },
            new AllocationPlanVersion
            {
                Id = versionId,
                VersionNumber = 1,
                Status = AllocationPlanVersionStatuses.Approved,
                ApprovedAt = Cutoff.AddHours(2),
            },
            lines.Select(item => item.Line).ToArray(),
            lines.Select(item => new AllocationLineTrace
            {
                Id = Guid.NewGuid(),
                AllocationPlanVersionId = versionId,
                AllocationRunLineId = item.Line.Id,
                Outcome = item.Outcome,
                Note = item.Note,
            }).ToArray(),
            lines.Where(item => item.Amount > 0).Select(item => new AllocationPlanPrice
            {
                Id = Guid.NewGuid(),
                AllocationPlanVersionId = versionId,
                AllocationRunLineId = item.Line.Id,
                Amount = item.Amount,
            }).ToArray());
    }

    private sealed class FakeStore(OrderRunResultView? view) : StubAllocationRunStore
    {
        public override Task<OrderRunResultView?> FindOrderRunResultAsync(
            Guid salesOrderId, CancellationToken cancellationToken) => Task.FromResult(view);
    }
}
