using FSCM.Application.Features.AllocationRuns;
using FSCM.Application.Features.DeliveryEta;
using FSCM.Application.Features.ShelfLifeDiscounts;
using FSCM.Application.Features.TransitTables;
using FSCM.Domain.Allocation;
using FSCM.Application.Features.Orders;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.AllocationRuns;

public sealed class AllocationRunServiceTests
{
    // 2026-10-22 05:00 UTC is 12:00 in Vietnam: the cutoff for the 22nd.
    private static readonly DateTimeOffset Cutoff = new(2026, 10, 22, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Closing_the_run_locks_orders_in_acceptance_order()
    {
        var fixture = new Fixture();
        fixture.AddOrder("DH-000002", Cutoff.AddHours(-1));
        fixture.AddOrder("DH-000001", Cutoff.AddHours(-3));
        fixture.AddOrder("DH-000003", Cutoff.AddHours(-2));

        var run = await fixture.Service.StartAsync(Cutoff, CancellationToken.None);

        Assert.Equal(["DH-000001", "DH-000003", "DH-000002"], run.Orders.Select(item => item.OrderCode));
        Assert.Equal([1, 2, 3], run.Orders.Select(item => item.PriorityRank));
        Assert.Equal(3, run.OrderCount);
    }

    [Fact]
    public async Task Orders_accepted_at_the_same_instant_are_ordered_by_code()
    {
        var fixture = new Fixture();
        fixture.AddOrder("DH-000009", Cutoff.AddHours(-1));
        fixture.AddOrder("DH-000004", Cutoff.AddHours(-1));

        var run = await fixture.Service.StartAsync(Cutoff, CancellationToken.None);

        Assert.Equal(["DH-000004", "DH-000009"], run.Orders.Select(item => item.OrderCode));
    }

    [Fact]
    public async Task Running_the_same_day_twice_joins_the_existing_run()
    {
        var fixture = new Fixture();
        fixture.AddOrder("DH-000001", Cutoff.AddHours(-1));
        var first = await fixture.Service.StartAsync(Cutoff, CancellationToken.None);

        var second = await fixture.Service.StartAsync(Cutoff.AddMinutes(30), CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(fixture.Store.Runs);
        Assert.Equal(1, second.OrderCount);
    }

    [Fact]
    public async Task The_run_belongs_to_the_Vietnam_calendar_day_not_the_UTC_one()
    {
        var fixture = new Fixture();
        fixture.AddOrder("DH-000001", Cutoff.AddHours(-1));

        var run = await fixture.Service.StartAsync(Cutoff, CancellationToken.None);

        // 05:00 UTC is still the 22nd in Vietnam, and the run code follows the local day.
        Assert.Equal(new DateOnly(2026, 10, 22), run.BusinessDate);
        Assert.StartsWith("DOT-20261022", run.RunCode);
    }

    [Fact]
    public async Task The_pool_is_a_copy_so_no_real_balance_moves()
    {
        var fixture = new Fixture();
        fixture.AddOrder("DH-000001", Cutoff.AddHours(-1));

        var run = await fixture.Service.StartAsync(Cutoff, CancellationToken.None);

        var pool = Assert.Single(run.Pool);
        Assert.Equal(pool.SnapshotQuantity, pool.RemainingQuantity);
        Assert.Equal(40m, fixture.Store.AvailableQuantity);
        Assert.Equal(7, pool.InventoryVersion);
    }

    [Fact]
    public async Task Settings_are_frozen_on_the_run()
    {
        var fixture = new Fixture();
        fixture.AddOrder("DH-000001", Cutoff.AddHours(-1));

        var run = await fixture.Service.StartAsync(Cutoff, CancellationToken.None);

        Assert.Equal(0.5m, run.Settings.ShelfLifeWeight);
        Assert.Equal(0.3m, run.Settings.DistanceWeight);
        Assert.Equal(0.2m, run.Settings.CoverageWeight);
        Assert.Equal(20_000, run.Settings.MaxServingDistanceMeters);
        Assert.Equal(AllocationRunStatuses.Snapshotted, run.Status);
    }

    [Fact]
    public async Task Each_line_carries_its_own_mlor_and_base_price()
    {
        var fixture = new Fixture();
        fixture.AddOrder("DH-000001", Cutoff.AddHours(-1), netAmount: 900m, quantity: 10m);

        var run = await fixture.Service.StartAsync(Cutoff, CancellationToken.None);

        var line = Assert.Single(run.Orders.Single().Lines);
        Assert.Equal(0.3m, line.EffectiveMlorRatio);
        Assert.Equal(MlorSources.RetailerProduct, line.MlorSource);
        Assert.Equal(90m, line.BaseNetUnitPrice);
        Assert.Equal(720, line.TotalShelfLifeHours);
        Assert.Equal(0m, line.AllocatedQuantity);
    }

    [Fact]
    public async Task A_line_keeps_the_mlor_the_order_was_accepted_under()
    {
        var fixture = new Fixture();
        // The order was captured at 40%; the current configuration says 30%. The run must honour
        // what the retailer agreed to, not what the table says today.
        fixture.AddOrder("DH-000001", Cutoff.AddHours(-1), capturedMlorRatio: 0.40m);

        var run = await fixture.Service.StartAsync(Cutoff, CancellationToken.None);

        var line = Assert.Single(run.Orders.Single().Lines);
        Assert.Equal(0.40m, line.EffectiveMlorRatio);
        Assert.Equal("RetailerCategory", line.MlorSource);
    }

    [Fact]
    public async Task A_run_with_no_eligible_order_still_exists_and_holds_nothing()
    {
        var fixture = new Fixture();

        var run = await fixture.Service.StartAsync(Cutoff, CancellationToken.None);

        Assert.Equal(0, run.OrderCount);
        Assert.Empty(run.Orders);
        Assert.Empty(run.Pool);
    }

    internal class Fixture
    {
        public FakeStore Store { get; } = new();
        public AllocationRunService Service { get; }

        /// <summary>
        /// These tests cover closing the run, so the planning collaborators are real objects over
        /// empty stores: if planning were reached by accident it would fail with a clear
        /// "nothing published" error rather than a null reference.
        /// </summary>
        public Fixture()
        {
            var engine = new AllocationEngine();
            var clock = new FixedClock();
            var transit = new TransitTableService(new EmptyTransitTableStore(), clock);
            Service = new AllocationRunService(
                Store,
                new AllocationPlanner(engine, new BatchSwapResolver(engine)),
                new AllocationPricingService(new ShelfLifeDiscountService(new EmptyDiscountStore(), clock)),
                new DeliveryEtaService(new EmptyEtaStore(), transit, clock),
                clock);
        }

        public void AddOrder(
            string code, DateTimeOffset acceptedAt, decimal netAmount = 500m, decimal quantity = 5m,
            decimal capturedMlorRatio = 0m) =>
            Store.Eligible.Add(new EligibleOrderView(
                new SalesOrder
                {
                    Id = Guid.NewGuid(),
                    OrderCode = code,
                    RetailerId = Store.RetailerId,
                    Status = OrderStatuses.PendingAllocation,
                    ServerReceivedAt = acceptedAt,
                    ReviewedAt = acceptedAt,
                    RequestedDeliveryDate = new DateOnly(2026, 10, 26)
                },
                [
                    new EligibleOrderLineView(
                        new SalesOrderLine
                        {
                            Id = Guid.NewGuid(),
                            ProductId = Store.ProductId,
                            SkuSnapshot = "SKU001",
                            LineNumber = 1,
                            OrderedQuantity = quantity,
                            NetAmount = netAmount,
                            MlorMinimumLifeRatio = capturedMlorRatio,
                            MlorSourceScope = capturedMlorRatio > 0 ? "RetailerCategory" : string.Empty
                        },
                        new Product { Id = Store.ProductId, ShelfLifeHours = 720 })
                ]));
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Cutoff;
    }

    internal sealed class FakeStore : IAllocationRunStore
    {
        public Guid RetailerId { get; } = Guid.NewGuid();
        public Guid ProductId { get; } = Guid.NewGuid();
        public Guid BatchId { get; } = Guid.NewGuid();
        public decimal AvailableQuantity { get; } = 40m;
        public List<EligibleOrderView> Eligible { get; } = [];
        public List<AllocationRun> Runs { get; } = [];
        public List<AllocationRunOrder> Orders { get; } = [];
        public List<AllocationRunLine> Lines { get; } = [];
        public List<AllocationRunStock> Pool { get; } = [];

        public Task<AllocationRun?> FindByBusinessDateAsync(DateOnly businessDate, CancellationToken cancellationToken) =>
            Task.FromResult(Runs.SingleOrDefault(item => item.BusinessDate == businessDate));

        public Task<AllocationRunView?> FindViewAsync(Guid runId, CancellationToken cancellationToken) =>
            Task.FromResult(ToView(Runs.SingleOrDefault(item => item.Id == runId)));

        public Task<AllocationRunView?> FindViewByBusinessDateAsync(
            DateOnly businessDate, CancellationToken cancellationToken) =>
            Task.FromResult(ToView(Runs.SingleOrDefault(item => item.BusinessDate == businessDate)));

        public Task<IReadOnlyCollection<EligibleOrderView>> ListEligibleOrdersAsync(
            DateTimeOffset cutoffAt, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<EligibleOrderView>>(
                Eligible.Where(item => item.Order.ServerReceivedAt < cutoffAt).ToArray());

        public Task<IReadOnlyCollection<AvailableBatchView>> ListAvailableStockAsync(
            IReadOnlyCollection<Guid> productIds, DateOnly businessDate, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<AvailableBatchView>>(productIds.Contains(ProductId)
                ?
                [
                    new AvailableBatchView(
                        new Batch
                        {
                            Id = BatchId,
                            ProductId = ProductId,
                            WarehouseId = Guid.NewGuid(),
                            ExpirationDate = businessDate.AddDays(20)
                        },
                        Guid.NewGuid(), AvailableQuantity, 7)
                ]
                : []);

        public Task<IReadOnlyDictionary<(Guid RetailerId, Guid ProductId), (decimal Ratio, string Source)>>
            ResolveMlorAsync(
                IReadOnlyCollection<(Guid RetailerId, Guid ProductId)> pairs, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<(Guid, Guid), (decimal, string)>>(
                pairs.ToDictionary(pair => pair, _ => (0.3m, MlorSources.RetailerProduct)));

        public List<AllocationRunAllocation> Allocations { get; } = [];

        public Task<IReadOnlyCollection<PlannerBatch>> ListPlannerBatchesAsync(
            Guid runId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<PlannerBatch>>(Pool
                .Where(item => item.AllocationRunId == runId && item.RemainingQuantity > 0)
                .Select(item => new PlannerBatch(
                    item.BatchId, "B-001", item.WarehouseId, "WH-01", item.ProductId,
                    new DateTimeOffset(item.ExpirationDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero),
                    item.RemainingQuantity))
                .ToArray());

        public Task<IReadOnlyDictionary<(Guid WarehouseId, Guid RetailerId), decimal>> ListDistancesAsync(
            IReadOnlyCollection<Guid> warehouseIds,
            IReadOnlyCollection<Guid> retailerIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<(Guid, Guid), decimal>>(
                warehouseIds.SelectMany(warehouse => retailerIds.Select(retailer => (warehouse, retailer)))
                    .ToDictionary(pair => pair, _ => 5_000m));

        public Task<IReadOnlyDictionary<Guid, Guid>> ListProductCategoriesAsync(
            IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, Guid>>(
                productIds.ToDictionary(item => item, _ => Guid.NewGuid()));

        public Task<IReadOnlyDictionary<Guid, DateTimeOffset>> ListBatchExpiryAsync(
            IReadOnlyCollection<Guid> batchIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, DateTimeOffset>>(
                batchIds.ToDictionary(item => item, _ => Cutoff.AddDays(20)));

        public void AddAllocations(IEnumerable<AllocationRunAllocation> allocations) =>
            Allocations.AddRange(allocations);

        public List<AllocationPlanVersion> PlanVersions { get; } = [];
        public List<AllocationPlanPrice> PlanPrices { get; } = [];

        public void AddPlanVersion(AllocationPlanVersion version) => PlanVersions.Add(version);

        public void AddPlanPrices(IEnumerable<AllocationPlanPrice> prices) => PlanPrices.AddRange(prices);

        public Task<int> MaxPlanVersionNumberAsync(Guid runId, CancellationToken cancellationToken) =>
            Task.FromResult(PlanVersions.Where(item => item.AllocationRunId == runId)
                .Select(item => item.VersionNumber).DefaultIfEmpty(0).Max());

        public Task<AllocationPlanVersion?> FindLivePlanVersionAsync(
            Guid runId, CancellationToken cancellationToken) =>
            Task.FromResult(PlanVersions.SingleOrDefault(item => item.AllocationRunId == runId
                && item.Status == AllocationPlanVersionStatuses.AwaitingApproval));

        public Task<AllocationPlanVersion?> FindPlanVersionForUpdateAsync(
            Guid versionId, CancellationToken cancellationToken) =>
            Task.FromResult(PlanVersions.SingleOrDefault(item => item.Id == versionId));

        public Task<AllocationPlanVersionView?> FindPlanVersionViewAsync(
            Guid versionId, CancellationToken cancellationToken) =>
            Task.FromResult(ToPlanView(PlanVersions.SingleOrDefault(item => item.Id == versionId)));

        public Task<IReadOnlyCollection<AllocationPlanVersionView>> ListPlanVersionsAsync(
            Guid runId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<AllocationPlanVersionView>>(PlanVersions
                .Where(item => item.AllocationRunId == runId)
                .OrderByDescending(item => item.VersionNumber)
                .Select(item => ToPlanView(item)!)
                .ToArray());

        public Task<AllocationRun?> FindRunForUpdateAsync(Guid runId, CancellationToken cancellationToken) =>
            Task.FromResult(Runs.SingleOrDefault(item => item.Id == runId));

        public List<Reservation> Reservations { get; } = [];
        public List<InventoryTransaction> InventoryTransactions { get; } = [];

        /// <summary>Live stock per batch, so a test can move it between planning and approval.</summary>
        public Dictionary<Guid, InventoryBalance> Balances { get; } = [];
        public Dictionary<Guid, Batch> Batches { get; } = [];

        public Task<IReadOnlyCollection<ReservableAllocation>> ListReservableAsync(
            Guid planVersionId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<ReservableAllocation>>(Allocations
                .Where(item => item.AllocationPlanVersionId == planVersionId)
                .Select(item => new ReservableAllocation(
                    item,
                    Batches.GetValueOrDefault(item.BatchId)
                        ?? new Batch { Id = item.BatchId, BatchCode = "B-001", ExpiryStatus = "Valid" },
                    Balances.TryGetValue(item.BatchId, out var balance)
                        ? balance
                        : Balances[item.BatchId] = new InventoryBalance
                        {
                            BatchId = item.BatchId,
                            PhysicalQuantity = AvailableQuantity,
                            AvailableQuantity = AvailableQuantity
                        },
                    Reservations.Any(reservation => reservation.AllocationRunAllocationId == item.Id
                        && reservation.Status == "Active")))
                .ToArray());

        public void AddReservation(Reservation reservation) => Reservations.Add(reservation);

        public void AddInventoryTransaction(InventoryTransaction transaction) =>
            InventoryTransactions.Add(transaction);

        private AllocationPlanVersionView? ToPlanView(AllocationPlanVersion? version) => version is null
            ? null
            : new AllocationPlanVersionView(
                version,
                Runs.Single(item => item.Id == version.AllocationRunId).RunCode,
                PlanPrices.Where(item => item.AllocationPlanVersionId == version.Id).ToArray());

        public Task<AllocationSettings> GetSettingsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AllocationSettings(0.5m, 0.3m, 0.2m, 20_000));

        public Task<string> NextRunCodeAsync(DateOnly businessDate, CancellationToken cancellationToken) =>
            Task.FromResult($"DOT-{businessDate:yyyyMMdd}-01");

        public void AddRun(AllocationRun run) => Runs.Add(run);

        public void AddOrders(IEnumerable<AllocationRunOrder> orders) => Orders.AddRange(orders);

        public void AddLines(IEnumerable<AllocationRunLine> lines) => Lines.AddRange(lines);

        public void AddPool(IEnumerable<AllocationRunStock> pool) => Pool.AddRange(pool);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ExecuteInTransactionAsync(
            Func<CancellationToken, Task> action, CancellationToken cancellationToken) =>
            action(cancellationToken);

        public void ClearTracking() { }

        private AllocationRunView? ToView(AllocationRun? run) => run is null
            ? null
            : new AllocationRunView(
                run,
                Orders.Where(item => item.AllocationRunId == run.Id).ToArray(),
                Lines.Where(line => Orders.Any(order =>
                    order.Id == line.AllocationRunOrderId && order.AllocationRunId == run.Id)).ToArray(),
                Pool.Where(item => item.AllocationRunId == run.Id).ToArray());
    }
}
