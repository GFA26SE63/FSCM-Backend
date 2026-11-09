using FSCM.Application.Features.AllocationRuns;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.AllocationRuns;

/// <summary>
/// F43: issuing picking sheets for an approved run. One sheet per warehouse, gathered by batch,
/// split back to orders, each split still carrying the price that was approved for it.
/// </summary>
public sealed class RunPickingIssueTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task One_sheet_is_issued_per_warehouse()
    {
        var fixture = new Fixture();
        var north = Guid.NewGuid();
        var south = Guid.NewGuid();
        fixture.Add("DH-000001", north, "B-001", 10m);
        fixture.Add("DH-000002", south, "B-002", 4m);

        var lists = await fixture.Service.IssueAsync(fixture.RunId, CancellationToken.None);

        Assert.Equal(2, lists.Count);
        Assert.Equal(new[] { north, south }.Order(), lists.Select(item => item.WarehouseId).Order());
        Assert.All(lists, list => Assert.Equal(RunPickingListStatuses.Issued, list.Status));
    }

    [Fact]
    public async Task One_batch_needed_by_two_orders_is_gathered_once_and_split_twice()
    {
        var fixture = new Fixture();
        var warehouse = Guid.NewGuid();
        fixture.Add("DH-000001", warehouse, "B-001", 10m);
        fixture.Add("DH-000002", warehouse, "B-001", 6m);

        var list = Assert.Single(await fixture.Service.IssueAsync(fixture.RunId, CancellationToken.None));

        var gather = Assert.Single(list.Gathers);
        Assert.Equal(16m, gather.RequiredQuantity);
        Assert.Equal(2, gather.Splits.Count);
        Assert.Equal(["DH-000001", "DH-000002"], gather.Splits.Select(item => item.OrderCode));
        Assert.Equal(16m, gather.Splits.Sum(item => item.Quantity));
    }

    [Fact]
    public async Task Each_split_carries_the_price_record_approved_for_it()
    {
        var fixture = new Fixture();
        var warehouse = Guid.NewGuid();
        // One order line served from two batches: two parts, two prices.
        fixture.Add("DH-000001", warehouse, "B-001", 10m, sharedLine: true);
        fixture.Add("DH-000001", warehouse, "B-002", 5m, sharedLine: true);

        var list = Assert.Single(await fixture.Service.IssueAsync(fixture.RunId, CancellationToken.None));

        var priceIds = list.Gathers.SelectMany(item => item.Splits)
            .Select(item => item.AllocationPlanPriceId).ToArray();
        Assert.Equal(2, priceIds.Length);
        Assert.Equal(2, priceIds.Distinct().Count());
    }

    [Fact]
    public async Task Issuing_twice_returns_the_same_sheets()
    {
        var fixture = new Fixture();
        fixture.Add("DH-000001", Guid.NewGuid(), "B-001", 10m);
        var first = await fixture.Service.IssueAsync(fixture.RunId, CancellationToken.None);

        var second = await fixture.Service.IssueAsync(fixture.RunId, CancellationToken.None);

        Assert.Equal(first.Single().Id, second.Single().Id);
        Assert.Single(fixture.Store.Lists);
        Assert.Single(fixture.Store.Gathers);
    }

    [Fact]
    public async Task Issuing_moves_no_stock()
    {
        var fixture = new Fixture();
        fixture.Add("DH-000001", Guid.NewGuid(), "B-001", 10m);

        await fixture.Service.IssueAsync(fixture.RunId, CancellationToken.None);

        // The plan already holds the stock; a sheet is paperwork.
        Assert.All(fixture.Store.Gathers, gather => Assert.Equal(0m, gather.PickedQuantity));
        Assert.All(fixture.Store.Splits, split => Assert.Equal(0m, split.PickedQuantity));
    }

    [Fact]
    public async Task A_run_that_is_not_approved_issues_nothing()
    {
        var fixture = new Fixture(runStatus: AllocationRunStatuses.AwaitingApproval);
        fixture.Add("DH-000001", Guid.NewGuid(), "B-001", 10m);

        var exception = await Assert.ThrowsAsync<AllocationRunOperationException>(() =>
            fixture.Service.IssueAsync(fixture.RunId, CancellationToken.None));

        Assert.Equal("conflict", exception.Code);
        Assert.Contains("only an approved run", exception.Message);
    }

    [Fact]
    public async Task A_run_that_allocated_nothing_says_so()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<AllocationRunOperationException>(() =>
            fixture.Service.IssueAsync(fixture.RunId, CancellationToken.None));

        Assert.Equal("conflict", exception.Code);
        Assert.Contains("nothing to pick", exception.Message);
    }

    [Fact]
    public async Task Only_orders_with_accepted_quote_and_po_are_picked_and_rejected_are_released()
    {
        var runId = Guid.NewGuid();
        var store = new FakeStore(runId, AllocationRunStatuses.Approved);
        var order1Id = Guid.NewGuid();
        var order2Id = Guid.NewGuid();
        var order3Id = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();

        store.AddCustom(order1Id, "DH-000001", warehouseId, "B-001", 10m);
        store.AddCustom(order2Id, "DH-000002", warehouseId, "B-001", 5m);
        store.AddCustom(order3Id, "DH-000003", warehouseId, "B-001", 3m);

        var quotes = new FakeQuoteStore();
        quotes.Views.Add(new FSCM.Application.Features.Quotes.QuoteView(
            new SalesOrderQuote { Id = Guid.NewGuid(), SalesOrderId = order1Id, Status = FSCM.Application.Features.Quotes.SalesOrderQuoteStatuses.Accepted },
            new SalesOrder { Id = order1Id, OrderCode = "DH-000001" },
            new PurchaseOrder { Id = Guid.NewGuid(), PurchaseOrderCode = "PO-001" }));
        quotes.Views.Add(new FSCM.Application.Features.Quotes.QuoteView(
            new SalesOrderQuote { Id = Guid.NewGuid(), SalesOrderId = order2Id, Status = FSCM.Application.Features.Quotes.SalesOrderQuoteStatuses.PendingRetailerAcceptance },
            new SalesOrder { Id = order2Id, OrderCode = "DH-000002" },
            null));
        quotes.Views.Add(new FSCM.Application.Features.Quotes.QuoteView(
            new SalesOrderQuote { Id = Guid.NewGuid(), SalesOrderId = order3Id, Status = FSCM.Application.Features.Quotes.SalesOrderQuoteStatuses.Rejected },
            new SalesOrder { Id = order3Id, OrderCode = "DH-000003" },
            null));

        var releaseStore = new FSCM.UnitTests.Orders.FakeOrderReleaseStore();
        var releaseService = new FSCM.Application.Features.Orders.OrderReleaseService(releaseStore, new FixedClock());

        var service = new RunPickingIssueService(store, new FixedClock(), quotes, releaseService);

        var lists = await service.IssueAsync(runId, CancellationToken.None);

        var list = Assert.Single(lists);
        var gather = Assert.Single(list.Gathers);
        Assert.Equal(10m, gather.RequiredQuantity);
        var split = Assert.Single(gather.Splits);
        Assert.Equal("DH-000001", split.OrderCode);
        Assert.Equal(10m, split.Quantity);
    }

    [Fact]
    public async Task Audit_late_po_must_not_leave_order_without_picking_split()
    {
        var run = Guid.NewGuid();
        var store = new FakeStore(run, AllocationRunStatuses.Approved);
        var quotes = new FakeQuoteStore();
        var warehouse = Guid.NewGuid();
        var first = Guid.NewGuid();
        var late = Guid.NewGuid();
        store.AddCustom(first, "DH-000001", warehouse, "B-001", 10m);
        store.AddCustom(late, "DH-000002", warehouse, "B-001", 5m);
        quotes.Views.Add(new(new SalesOrderQuote { SalesOrderId = first, Status = "Accepted" }, new SalesOrder { Id = first }, new PurchaseOrder()));
        var pending = new SalesOrderQuote { SalesOrderId = late, Status = "PendingRetailerAcceptance" };
        quotes.Views.Add(new(pending, new SalesOrder { Id = late }, null));
        var service = new RunPickingIssueService(store, new FixedClock(), quotes);
        await service.IssueAsync(run, default);
        store.Lists.Single().Status = RunPickingListStatuses.Ready;
        store.Lists.Single().StartedAt = Now;
        store.Lists.Single().CompletedAt = Now;
        store.Gathers.Single().PickedQuantity = 10m;
        store.Gathers.Single().Status = RunPickingGatherStatuses.Picked;
        pending.Status = "Accepted";
        quotes.Views[1] = new(pending, new SalesOrder { Id = late }, new PurchaseOrder());
        var result = await service.IssueAsync(run, default);
        Assert.Contains(result.SelectMany(x => x.Gathers).SelectMany(x => x.Splits), x => x.OrderCode == "DH-000002");
        Assert.Equal(15m, store.Gathers.Single().RequiredQuantity);
        Assert.Equal(10m, store.Gathers.Single().PickedQuantity);
        Assert.Equal(RunPickingListStatuses.Picking, store.Lists.Single().Status);
        var again = await service.IssueAsync(run, default);
        Assert.Equal(2, again.Single().Gathers.Single().Splits.Count);
    }

    private sealed class Fixture
    {
        public FakeStore Store { get; }
        public RunPickingIssueService Service { get; }
        public Guid RunId { get; } = Guid.NewGuid();

        public Fixture(string runStatus = AllocationRunStatuses.Approved)
        {
            Store = new FakeStore(RunId, runStatus);
            Service = new RunPickingIssueService(Store, new FixedClock());
        }

        public void Add(string orderCode, Guid warehouseId, string batchCode, decimal quantity, bool sharedLine = false)
        {
            var batchId = Store.BatchIds.TryGetValue(batchCode, out var known) ? known : Guid.NewGuid();
            Store.BatchIds[batchCode] = batchId;
            var lineId = sharedLine && Store.LineIds.TryGetValue(orderCode, out var line) ? line : Guid.NewGuid();
            Store.LineIds[orderCode] = lineId;

            Store.Pickable.Add(new PickableAllocation(
                new AllocationRunAllocation
                {
                    Id = Guid.NewGuid(),
                    AllocationPlanVersionId = Store.PlanVersionId,
                    AllocationRunLineId = lineId,
                    BatchId = batchId,
                    WarehouseId = warehouseId,
                    Quantity = quantity
                },
                new AllocationRunLine { Id = lineId, ProductId = Guid.NewGuid(), SkuSnapshot = "SKU001" },
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                orderCode,
                batchCode));
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeStore(Guid runId, string runStatus) : StubAllocationRunStore
    {
        public Guid PlanVersionId { get; } = Guid.NewGuid();
        public Dictionary<string, Guid> BatchIds { get; } = [];
        public Dictionary<string, Guid> LineIds { get; } = [];
        public List<PickableAllocation> Pickable { get; } = [];
        public List<RunPickingList> Lists { get; } = [];
        public List<RunPickingGather> Gathers { get; } = [];
        public List<RunPickingSplit> Splits { get; } = [];

        public override Task<AllocationRun?> FindRunForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<AllocationRun?>(id == runId
                ? new AllocationRun { Id = runId, RunCode = "DOT-20261022-01", Status = runStatus }
                : null);

        public override Task<AllocationPlanVersion?> FindApprovedPlanVersionAsync(
            Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<AllocationPlanVersion?>(new AllocationPlanVersion
            {
                Id = PlanVersionId,
                AllocationRunId = runId,
                Status = AllocationPlanVersionStatuses.Approved
            });

        public override Task<IReadOnlyCollection<PickableAllocation>> ListPickableAsync(
            Guid planVersionId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<PickableAllocation>>(Pickable
                .Where(item => item.Allocation.AllocationPlanVersionId == planVersionId).ToArray());

        public override Task<IReadOnlyCollection<RunPickingListView>> ListRunPickingListsAsync(
            Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<RunPickingListView>>(Lists
                .Where(item => item.AllocationRunId == id)
                .Select(ToView)
                .ToArray());

        public override void AddRunPickingLists(IEnumerable<RunPickingList> lists) => Lists.AddRange(lists);

        public override void AddRunPickingGathers(IEnumerable<RunPickingGather> gathers) => Gathers.AddRange(gathers);

        public override void AddRunPickingSplits(IEnumerable<RunPickingSplit> splits) => Splits.AddRange(splits);

        public void AddCustom(Guid orderId, string orderCode, Guid warehouseId, string batchCode, decimal quantity)
        {
            var batchId = BatchIds.TryGetValue(batchCode, out var known) ? known : Guid.NewGuid();
            BatchIds[batchCode] = batchId;
            var lineId = Guid.NewGuid();

            Pickable.Add(new PickableAllocation(
                new AllocationRunAllocation
                {
                    Id = Guid.NewGuid(),
                    AllocationPlanVersionId = PlanVersionId,
                    AllocationRunLineId = lineId,
                    BatchId = batchId,
                    WarehouseId = warehouseId,
                    Quantity = quantity
                },
                new AllocationRunLine { Id = lineId, ProductId = Guid.NewGuid(), SkuSnapshot = "SKU001" },
                Guid.NewGuid(),
                orderId,
                Guid.NewGuid(),
                orderCode,
                batchCode));
        }

        private RunPickingListView ToView(RunPickingList list)
        {
            var gathers = Gathers.Where(item => item.RunPickingListId == list.Id).ToArray();
            var ids = gathers.Select(item => item.Id).ToHashSet();
            return new RunPickingListView(
                list, gathers, Splits.Where(item => ids.Contains(item.RunPickingGatherId)).ToArray());
        }
    }

    private sealed class FakeQuoteStore : FSCM.Application.Features.Quotes.IQuoteStore
    {
        public List<FSCM.Application.Features.Quotes.QuoteView> Views { get; } = [];

        public Task<FSCM.Application.Features.Quotes.QuoteView?> FindAsync(Guid id, bool forUpdate, CancellationToken cancellationToken) =>
            Task.FromResult(Views.FirstOrDefault(x => x.Quote.Id == id));

        public Task<IReadOnlyCollection<FSCM.Application.Features.Quotes.QuoteView>> ListForOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<FSCM.Application.Features.Quotes.QuoteView>>(Views.Where(x => x.Order.Id == orderId).ToArray());

        public Task<IReadOnlyCollection<FSCM.Application.Features.Quotes.QuoteView>> ListForRunAsync(Guid runId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<FSCM.Application.Features.Quotes.QuoteView>>(Views);

        public Task<bool> CanRetailerAccessAsync(Guid quoteId, Guid userId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<IReadOnlyCollection<FSCM.Application.Features.Quotes.QuoteIssueOrder>> ListOrdersForPlanAsync(Guid allocationPlanVersionId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<FSCM.Application.Features.Quotes.QuoteIssueOrder>>([]);

        public void Add(SalesOrderQuote quote) { }
        public void AddPurchaseOrder(PurchaseOrder purchaseOrder) { }
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
