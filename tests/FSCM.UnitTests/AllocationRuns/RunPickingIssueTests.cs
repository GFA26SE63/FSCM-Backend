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

        private RunPickingListView ToView(RunPickingList list)
        {
            var gathers = Gathers.Where(item => item.RunPickingListId == list.Id).ToArray();
            var ids = gathers.Select(item => item.Id).ToHashSet();
            return new RunPickingListView(
                list, gathers, Splits.Where(item => ids.Contains(item.RunPickingGatherId)).ToArray());
        }
    }
}
