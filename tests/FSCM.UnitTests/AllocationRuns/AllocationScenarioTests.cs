using System.Text.Json;
using FSCM.Application.Features.AllocationRuns;
using FSCM.Application.Features.DeliveryEta;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.ShelfLifeDiscounts;
using FSCM.Application.Features.TransitTables;
using FSCM.Domain.Allocation;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.AllocationRuns;

/// <summary>
/// F97: the run end to end, over published configuration and reachable routes.
///
/// Every other allocation test here drives one collaborator. These drive the whole thing — cutoff,
/// priority, the shared pool, batch choice, pricing, the trace — because the rules that matter most
/// under scope V10 only appear when the parts run together. An order giving its stock back is not
/// visible to the engine, which never sees the order; it is visible only once the planner has run
/// two lines and the run has persisted what happened.
///
/// The fixture publishes a schedule, a transit table and a discount table, and gives every
/// warehouse a reachable route, so a short order is short because the stock ran out rather than
/// because nothing could be delivered.
/// </summary>
public sealed class AllocationScenarioTests
{
    private static readonly DateTimeOffset Cutoff = new(2026, 10, 26, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_order_accepted_at_the_cutoff_belongs_to_the_next_run()
    {
        var fixture = new ScenarioFixture();
        fixture.AddOrder("DH-000001", Cutoff.AddSeconds(-1), ("SKU001", 5m));
        fixture.AddOrder("DH-000002", Cutoff, ("SKU001", 5m));

        var run = await fixture.StartAsync();

        // The boundary is exclusive on purpose: "before the cutoff" has to mean one thing, or the
        // same order lands in different runs depending on which server clock read it.
        Assert.Equal("DH-000001", Assert.Single(run.Orders).OrderCode);
    }

    [Fact]
    public async Task Priority_is_arrival_time_then_order_code()
    {
        var fixture = new ScenarioFixture();
        var sameMoment = Cutoff.AddHours(-1);
        fixture.AddOrder("DH-000003", sameMoment, ("SKU001", 1m));
        fixture.AddOrder("DH-000002", sameMoment, ("SKU001", 1m));
        fixture.AddOrder("DH-000001", Cutoff.AddHours(-2), ("SKU001", 1m));

        var run = await fixture.StartAsync();

        Assert.Equal(
            ["DH-000001", "DH-000002", "DH-000003"],
            run.Orders.OrderBy(item => item.PriorityRank).Select(item => item.OrderCode));
    }

    [Fact]
    public async Task An_earlier_order_really_takes_the_stock_from_a_later_one()
    {
        var fixture = new ScenarioFixture();
        fixture.AddBatch("LO-A", "SKU001", Cutoff.AddDays(40), quantity: 30m);
        fixture.AddOrder("DH-000001", Cutoff.AddHours(-2), ("SKU001", 25m));
        fixture.AddOrder("DH-000002", Cutoff.AddHours(-1), ("SKU001", 25m));

        await fixture.RunAsync();

        // One shared pool, drawn down in priority order: the second order is not served from a
        // quantity the first already holds.
        Assert.Equal(AllocationRunOrderStatuses.Fulfilled, fixture.OrderStatus("DH-000001"));
        Assert.Equal(AllocationRunOrderStatuses.Short, fixture.OrderStatus("DH-000002"));
    }

    [Fact]
    public async Task A_short_order_hands_its_whole_tentative_draw_back_to_the_pool()
    {
        var fixture = new ScenarioFixture();
        // Enough of the first SKU, none of the second: the order draws, then cannot finish.
        fixture.AddBatch("LO-A", "SKU001", Cutoff.AddDays(40), quantity: 50m);
        fixture.AddBatch("LO-B", "SKU002", Cutoff.AddDays(40), quantity: 2m);
        fixture.AddOrder("DH-000001", Cutoff.AddHours(-2), ("SKU001", 10m), ("SKU002", 8m));
        fixture.AddOrder("DH-000002", Cutoff.AddHours(-1), ("SKU001", 10m));

        await fixture.RunAsync();

        Assert.Equal(AllocationRunOrderStatuses.Short, fixture.OrderStatus("DH-000001"));
        // The quantity the first order had already drawn is available again, so the second order
        // is served from it. Keeping the draw would strand stock nobody is going to ship.
        Assert.Equal(AllocationRunOrderStatuses.Fulfilled, fixture.OrderStatus("DH-000002"));
    }

    [Fact]
    public async Task The_line_that_drew_and_lost_it_is_Released_not_Short()
    {
        var fixture = new ScenarioFixture();
        fixture.AddBatch("LO-A", "SKU001", Cutoff.AddDays(40), quantity: 50m);
        fixture.AddBatch("LO-B", "SKU002", Cutoff.AddDays(40), quantity: 2m);
        fixture.AddOrder("DH-000001", Cutoff.AddHours(-2), ("SKU001", 10m), ("SKU002", 8m));

        await fixture.RunAsync();

        var traces = fixture.Store.LineTraces.OrderBy(item => fixture.LineNumberOf(item)).ToArray();
        Assert.Equal(2, traces.Length);
        // Line 1 succeeded and then lost its stock because line 2 could not be filled. It is not
        // the line that failed, and reading one outcome for the whole order would hide which was.
        Assert.Equal(AllocationLineTraceOutcomes.Released, traces[0].Outcome);
        Assert.Equal(10m, traces[0].DrawnQuantity);
        Assert.Equal(AllocationLineTraceOutcomes.Short, traces[1].Outcome);
        Assert.Equal(6m, traces[1].ShortageQuantity);
    }

    [Fact]
    public async Task A_fulfilled_order_records_its_batches_with_codes_and_arrival()
    {
        var fixture = new ScenarioFixture();
        fixture.AddBatch("LO-A", "SKU001", Cutoff.AddDays(40), quantity: 50m);
        fixture.AddOrder("DH-000001", Cutoff.AddHours(-2), ("SKU001", 12m));

        await fixture.RunAsync();

        var allocation = Assert.Single(fixture.Store.Allocations);
        Assert.Equal(12m, allocation.Quantity);
        Assert.True(allocation.RemainingShelfLifeHours > 0);
        Assert.True(allocation.EstimatedArrivalAt > Cutoff);
        // The trace records the ranking that produced it, so the choice can be read back.
        Assert.Contains(fixture.Store.TraceScores, score => score.Selected && score.BatchCodeSnapshot == "LO-A");
    }

    [Fact]
    public async Task A_batch_below_MLOR_is_refused_however_deep_its_discount()
    {
        var fixture = new ScenarioFixture();
        // Nearly expired, so it would attract the deepest shelf-life reduction on the table.
        fixture.AddBatch("LO-NEAR", "SKU001", Cutoff.AddDays(2), quantity: 50m);
        fixture.AddBatch("LO-FAR", "SKU001", Cutoff.AddDays(40), quantity: 50m);
        fixture.AddOrder("DH-000001", Cutoff.AddHours(-2), ("SKU001", 10m));

        await fixture.RunAsync();

        // Price is settled after the batches are fixed, so a discount can never buy a batch past
        // the MLOR gate. The cheap one is simply not eligible.
        var allocation = Assert.Single(fixture.Store.Allocations);
        var chosen = fixture.Store.TraceScores.Single(score => score.Selected).BatchCodeSnapshot;
        Assert.Equal("LO-FAR", chosen);
        Assert.Equal(10m, allocation.Quantity);
        Assert.Contains(fixture.Store.TraceCandidates, candidate =>
            candidate.BatchCodeSnapshot == "LO-NEAR" && !candidate.Passed &&
            candidate.Reason == nameof(AllocationFilterReason.BelowMlor));
    }

    [Fact]
    public async Task Pricing_happens_after_selection_and_reports_the_tier_it_used()
    {
        var fixture = new ScenarioFixture();
        // 420 h of a 720 h life left at the pricing moment: 58.3%, inside the 0-60% band, so 20%
        // off - and still above the 360 h that MLOR demands on arrival, so it is eligible at all.
        fixture.AddBatch("LO-A", "SKU001", Cutoff.AddHours(420), quantity: 50m);
        fixture.AddOrder("DH-000001", Cutoff.AddHours(-2), ("SKU001", 4m), unitPrice: 100_000m);

        var pricing = await fixture.RunAsync();

        var part = Assert.Single(Assert.Single(pricing.Orders).Parts);
        Assert.Equal(20_000m, part.ShelfLifeDiscountPerUnit);
        Assert.Equal(80_000m, part.FinalNetUnitPrice);
        Assert.Equal(320_000m, part.Amount);
        Assert.Equal(1, part.DiscountTableVersion);
    }

    [Fact]
    public async Task A_batch_that_matches_no_band_is_priced_at_its_base()
    {
        var fixture = new ScenarioFixture();
        fixture.AddBatch("LO-A", "SKU001", Cutoff.AddDays(40), quantity: 50m);
        fixture.AddOrder("DH-000001", Cutoff.AddHours(-2), ("SKU001", 4m), unitPrice: 100_000m);

        var pricing = await fixture.RunAsync();

        var part = Assert.Single(Assert.Single(pricing.Orders).Parts);
        Assert.Null(part.TierId);
        Assert.Equal(0m, part.ShelfLifeDiscountPerUnit);
        Assert.Equal(100_000m, part.FinalNetUnitPrice);
    }

    [Fact]
    public async Task Every_part_of_one_version_is_priced_from_the_same_moment()
    {
        var fixture = new ScenarioFixture();
        fixture.AddBatch("LO-A", "SKU001", Cutoff.AddHours(420), quantity: 6m);
        // Neither batch covers 12 on its own, so the line has to be split across both.
        fixture.AddBatch("LO-B", "SKU001", Cutoff.AddHours(420), quantity: 8m);
        fixture.AddOrder("DH-000001", Cutoff.AddHours(-2), ("SKU001", 12m), unitPrice: 100_000m);

        var pricing = await fixture.RunAsync();

        var parts = Assert.Single(pricing.Orders).Parts;
        Assert.Equal(2, parts.Count);
        // Two batches of one line with the same expiry must reach the same remaining percentage;
        // measuring each from its own arrival would let one line disagree with itself.
        Assert.Single(parts.Select(part => part.RemainingPercent).Distinct());
        Assert.All(fixture.Store.PlanPrices, price => Assert.Equal(pricing.PricedAt, price.PricedAt));
    }

    [Fact]
    public async Task A_short_order_is_priced_at_nothing_and_holds_nothing()
    {
        var fixture = new ScenarioFixture();
        fixture.AddBatch("LO-A", "SKU001", Cutoff.AddDays(40), quantity: 2m);
        fixture.AddOrder("DH-000001", Cutoff.AddHours(-2), ("SKU001", 10m));

        var pricing = await fixture.RunAsync();

        // Only a complete order is priced: a figure for a plan nobody can ship would read as a
        // quotation someone could collect against.
        Assert.Empty(pricing.Orders);
        Assert.Empty(fixture.Store.Allocations);
        Assert.Empty(fixture.Store.PlanPrices);
    }

    [Fact]
    public async Task Running_the_same_business_date_twice_joins_the_run_that_exists()
    {
        var fixture = new ScenarioFixture();
        fixture.AddOrder("DH-000001", Cutoff.AddHours(-2), ("SKU001", 5m));

        var first = await fixture.StartAsync();
        var second = await fixture.StartAsync();

        // A restart, a second instance or a late wake-up must not split one day's orders across
        // two plans.
        Assert.Equal(first.Id, second.Id);
        Assert.Single(fixture.Store.Runs);
    }

    private sealed class ScenarioFixture
    {
        private static readonly Guid RetailerId = Guid.NewGuid();
        private static readonly Guid WarehouseId = Guid.NewGuid();
        private readonly Dictionary<string, Guid> _products = [];

        public ScenarioStore Store { get; } = new(RetailerId, WarehouseId);
        public AllocationRunService Service { get; }

        public ScenarioFixture()
        {
            var clock = new FixedClock();
            var transit = new TransitTableService(new ScenarioTransitStore(), clock);
            transit.PublishAsync(
                new PublishTransitTableData(50_000, Cutoff.AddDays(-30), null,
                [
                    new TransitBandData(0, 10_000, 60, 120, 80_000m),
                    new TransitBandData(10_000, 50_000, 60, 240, 120_000m)
                ]),
                Guid.NewGuid(), CancellationToken.None).GetAwaiter().GetResult();

            var discounts = new ShelfLifeDiscountService(new ScenarioDiscountStore(), clock);
            discounts.PublishAsync(
                new PublishShelfLifeDiscountTableData(Cutoff.AddDays(-30), "Bậc giảm HSD",
                [
                    new ShelfLifeDiscountTierData(
                        ShelfLifeDiscountScopes.Sku, ProductId("SKU001"), null,
                        0m, 60m, ShelfLifeDiscountTypes.Percent, 20m)
                ]),
                Guid.NewGuid(), CancellationToken.None).GetAwaiter().GetResult();

            Service = new AllocationRunService(
                Store,
                new AllocationPlanner(new AllocationEngine()),
                new AllocationPricingService(discounts),
                new DeliveryEtaService(new ScenarioEtaStore(WarehouseId, RetailerId), transit, clock),
                clock);
        }

        public Guid ProductId(string sku)
        {
            if (!_products.TryGetValue(sku, out var id))
            {
                id = Guid.NewGuid();
                _products[sku] = id;
            }

            return id;
        }

        /// <summary>A batch of one SKU in the single warehouse, available to the whole run.</summary>
        public void AddBatch(string batchCode, string sku, DateTimeOffset expiresAt, decimal quantity) =>
            Store.Batches.Add(new ScenarioBatch(
                Guid.NewGuid(), batchCode, ProductId(sku), WarehouseId, expiresAt, quantity));

        public void AddOrder(
            string code, DateTimeOffset acceptedAt, params (string Sku, decimal Quantity)[] lines) =>
            AddOrder(code, acceptedAt, 250_000m, lines);

        public void AddOrder(
            string code, DateTimeOffset acceptedAt, (string Sku, decimal Quantity) line, decimal unitPrice) =>
            AddOrder(code, acceptedAt, unitPrice, [line]);

        private void AddOrder(
            string code, DateTimeOffset acceptedAt, decimal unitPrice,
            (string Sku, decimal Quantity)[] lines)
        {
            var order = new SalesOrder
            {
                Id = Guid.NewGuid(),
                OrderCode = code,
                RetailerId = RetailerId,
                Status = OrderStatuses.PendingAllocation,
                ServerReceivedAt = acceptedAt,
                ReviewedAt = acceptedAt
            };
            var number = 0;
            var views = lines.Select(line => new EligibleOrderLineView(
                new SalesOrderLine
                {
                    Id = Guid.NewGuid(),
                    ProductId = ProductId(line.Sku),
                    SkuSnapshot = line.Sku,
                    LineNumber = ++number,
                    OrderedQuantity = line.Quantity,
                    UnitPrice = unitPrice,
                    NetAmount = unitPrice * line.Quantity,
                    // 50% of a 720 h life: a batch must still have 360 hours when it arrives.
                    MlorMinimumLifeRatio = 0.5m,
                    MlorSourceScope = MlorSources.RetailerProduct
                },
                new Product { Id = ProductId(line.Sku), ShelfLifeHours = 720 })).ToArray();
            Store.Eligible.Add(new EligibleOrderView(order, views));
        }

        /// <summary>The status the run recorded for one order, read back from what it persisted.</summary>
        public string OrderStatus(string code) =>
            Store.Orders.Single(item => item.OrderCodeSnapshot == code).Status;

        public int LineNumberOf(AllocationLineTrace trace) =>
            Store.Lines.Single(line => line.Id == trace.AllocationRunLineId).LineNumber;

        public Task<AllocationRunResult> StartAsync() =>
            Service.StartAsync(Cutoff, CancellationToken.None);

        /// <summary>Closes the run and plans it, which is what a 12:00 job does end to end.</summary>
        public async Task<AllocationPricing> RunAsync()
        {
            var run = await StartAsync();
            return await Service.PlanAsync(run.Id, CancellationToken.None);
        }

        private sealed class FixedClock : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => Cutoff;
        }
    }

    internal sealed record ScenarioBatch(
        Guid Id, string Code, Guid ProductId, Guid WarehouseId, DateTimeOffset ExpiresAt, decimal Quantity);

    private sealed class ScenarioEtaStore(Guid warehouseId, Guid retailerId) : IDeliveryEtaStore
    {
        private readonly MapLocation _warehouse = new() { WarehouseId = warehouseId, Version = 1 };
        private readonly MapLocation _retailer = new() { RetailerId = retailerId, Version = 1 };

        public Task<SystemConfiguration?> FindScheduleConfigurationAsync(CancellationToken cancellationToken) =>
            Task.FromResult<SystemConfiguration?>(new SystemConfiguration
            {
                ConfigKey = "eta.schedule",
                ConfigValue = JsonSerializer.Serialize(new
                {
                    timeZone = "Asia/Ho_Chi_Minh",
                    allocationRunLocalTime = "12:00",
                    planApprovalDeadlineLocalTime = "14:00",
                    preparationMinutes = 240,
                    bufferMinutes = 60,
                    dispatchSlotsLocalTime = new[] { "06:00", "14:00" }
                }),
                Version = 1,
                IsActive = true
            });

        public Task<MapDistanceCache?> FindDistanceAsync(
            Guid warehouse, Guid retailer, CancellationToken cancellationToken) =>
            Task.FromResult<MapDistanceCache?>(new MapDistanceCache
            {
                WarehouseId = warehouseId,
                RetailerId = retailerId,
                OriginLocationVersion = 1,
                DestinationLocationVersion = 1,
                DistanceMeters = 5_000,
                DurationSeconds = 900,
                Version = 1,
                ExpiresAt = Cutoff.AddHours(6),
                IsActive = true
            });

        public Task<MapLocation?> FindLocationAsync(
            string entityType, Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult<MapLocation?>(entityType == "warehouse" ? _warehouse : _retailer);
    }

    private sealed class ScenarioTransitStore : ITransitTableStore
    {
        private readonly List<TransitTable> _tables = [];
        private readonly List<TransitTableBand> _bands = [];

        public Task<TransitTableView?> FindCurrentAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ToView(_tables.SingleOrDefault(item => item.IsActive)));

        public Task<TransitTableView?> FindEffectiveAtAsync(DateTimeOffset at, CancellationToken cancellationToken) =>
            Task.FromResult(ToView(_tables.SingleOrDefault(item =>
                item.EffectiveFrom <= at && (item.EffectiveTo is null || item.EffectiveTo > at))));

        public Task<TransitTableView?> FindByVersionAsync(int version, CancellationToken cancellationToken) =>
            Task.FromResult(ToView(_tables.SingleOrDefault(item => item.Version == version)));

        public Task<IReadOnlyCollection<TransitTableView>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<TransitTableView>>(
                _tables.Select(item => ToView(item)!).ToArray());

        public Task<TransitTable?> FindCurrentForUpdateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_tables.SingleOrDefault(item => item.IsActive));

        public Task<int> MaxVersionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_tables.Count == 0 ? 0 : _tables.Max(item => item.Version));

        public void AddTable(TransitTable table) => _tables.Add(table);
        public void AddBands(IEnumerable<TransitTableBand> bands) => _bands.AddRange(bands);
        public void AddAuditLog(AuditLog auditLog) { }
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private TransitTableView? ToView(TransitTable? table) => table is null
            ? null
            : new TransitTableView(table, _bands.Where(item => item.TransitTableId == table.Id).ToArray());
    }

    private sealed class ScenarioDiscountStore : IShelfLifeDiscountStore
    {
        private readonly List<ShelfLifeDiscountTable> _tables = [];
        private readonly List<ShelfLifeDiscountTier> _tiers = [];

        public Task<ShelfLifeDiscountTableView?> FindCurrentAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ToView(_tables.SingleOrDefault(item => item.IsActive)));

        public Task<ShelfLifeDiscountTableView?> FindByVersionAsync(int version, CancellationToken cancellationToken) =>
            Task.FromResult(ToView(_tables.SingleOrDefault(item => item.Version == version)));

        public Task<ShelfLifeDiscountTableView?> FindEffectiveAtAsync(
            DateTimeOffset at, CancellationToken cancellationToken) =>
            Task.FromResult(ToView(_tables.SingleOrDefault(item =>
                item.EffectiveFrom <= at && (item.EffectiveTo is null || item.EffectiveTo > at))));

        public Task<IReadOnlyCollection<ShelfLifeDiscountTableView>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<ShelfLifeDiscountTableView>>(
                _tables.Select(item => ToView(item)!).ToArray());

        public Task<ShelfLifeDiscountTable?> FindCurrentForUpdateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_tables.SingleOrDefault(item => item.IsActive));

        public Task<int> MaxVersionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_tables.Count == 0 ? 0 : _tables.Max(item => item.Version));

        public void AddTable(ShelfLifeDiscountTable table) => _tables.Add(table);
        public void AddTiers(IEnumerable<ShelfLifeDiscountTier> tiers) => _tiers.AddRange(tiers);
        public void AddAuditLog(AuditLog auditLog) { }
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private ShelfLifeDiscountTableView? ToView(ShelfLifeDiscountTable? table) => table is null
            ? null
            : new ShelfLifeDiscountTableView(table, _tiers
                .Where(item => item.ShelfLifeDiscountTableId == table.Id).ToArray());
    }
}
