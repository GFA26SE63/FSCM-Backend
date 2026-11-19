using FSCM.Application.Features.AllocationRuns;
using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.ShelfLifeDiscounts;
using FSCM.Domain.Allocation;
using FSCM.Domain.Entities;
using AllocationEntity = FSCM.Domain.Entities.Allocation;

namespace FSCM.UnitTests.AllocationRuns;

public sealed class AllocationRunPlanningReport5Tests
{
    private static readonly DateTimeOffset AcceptedAt =
        new(2026, 11, 18, 9, 1, 5, TimeSpan.FromHours(7));

    [Fact]
    [Trait("Report5Case", "UT-Allocation Run Planning-UTCID01")]
    public async Task Accepted_so_starts_its_own_allocation_attempt_at_09_01_05_without_a_batch_window()
    {
        var fixture = new Fixture();

        await fixture.Service.AllocateAsync(fixture.Order.Id, fixture.ActorId, default);

        var run = Assert.Single(fixture.Store.Runs);
        Assert.Equal(AcceptedAt, fixture.Order.AcceptedAt);
        Assert.Equal(AcceptedAt, run.StartedAt);
        Assert.Equal(fixture.Order.Id, run.SalesOrderId);
        Assert.Equal("ALT-DH-PLANNING-01", run.RunCode);
        Assert.Equal(AllocationRunStatuses.Succeeded, run.Status);
        Assert.Equal(OrderStatuses.Quoted, fixture.Order.Status);
        Assert.Single(fixture.Store.Reservations);
        Assert.Single(fixture.Store.Transactions);
        Assert.Single(fixture.Store.Quotes);
        Assert.Equal(1, fixture.Store.SaveCalls);
    }

    [Fact]
    [Trait("Report5Case", "UT-Allocation Run Planning-UTCID02")]
    public async Task Duplicate_delivery_returns_the_existing_result_without_a_second_reservation_effect()
    {
        var fixture = new Fixture();

        await fixture.Service.AllocateAsync(fixture.Order.Id, fixture.ActorId, default);
        var firstRun = Assert.Single(fixture.Store.Runs);
        var firstReservation = Assert.Single(fixture.Store.Reservations);
        var firstQuote = Assert.Single(fixture.Store.Quotes);
        var reservedAfterFirstDelivery = fixture.Balance.ReservedQuantity;

        await fixture.Service.AllocateAsync(fixture.Order.Id, fixture.ActorId, default);

        Assert.Same(firstRun, Assert.Single(fixture.Store.Runs));
        Assert.Same(firstReservation, Assert.Single(fixture.Store.Reservations));
        Assert.Same(firstQuote, Assert.Single(fixture.Store.Quotes));
        Assert.Single(fixture.Store.Allocations);
        Assert.Single(fixture.Store.Transactions);
        Assert.Equal(reservedAfterFirstDelivery, fixture.Balance.ReservedQuantity);
        Assert.Equal(10m, fixture.Balance.ReservedQuantity);
        Assert.Equal(1, fixture.Store.SaveCalls);
    }

    [Fact]
    [Trait("Report5Case", "UT-Allocation Run Planning-UTCID04")]
    public void Obsolete_global_batch_schedule_and_approval_commands_are_not_exposed()
    {
        var allocationCommands = typeof(IRealTimeAllocationService).GetMethods();
        var orderCommands = typeof(OrderService).GetMethods()
            .Where(method => method.DeclaringType == typeof(OrderService))
            .ToArray();

        var commandNames = allocationCommands.Concat(orderCommands)
            .Select(method => method.Name)
            .ToArray();

        Assert.Equal([nameof(IRealTimeAllocationService.AllocateAsync)],
            allocationCommands.Select(method => method.Name));
        Assert.DoesNotContain(commandNames, name =>
            name.Contains("Batch", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Schedule", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("ApproveAllocation", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("ApproveRun", StringComparison.OrdinalIgnoreCase));

        // Read commands remain present even though the obsolete bulk mutation contract is absent.
        Assert.Contains(nameof(OrderService.GetAsync), commandNames);
        Assert.Contains(nameof(OrderService.ListAsync), commandNames);
    }

    [Fact]
    [Trait("Report5Case", "UT-Allocation Run Result-UTCID01")]
    public async Task Two_region_linked_warehouses_return_a_complete_quoted_result_with_locked_fragment_prices()
    {
        var fixture = new Fixture([6m, 4m]);

        await fixture.Service.AllocateAsync(fixture.Order.Id, fixture.ActorId, default);

        Assert.Equal(OrderStatuses.Quoted, fixture.Order.Status);
        var allocations = fixture.Store.Allocations.OrderBy(item => item.AllocationSequence).ToArray();
        Assert.Equal([6m, 4m], allocations.Select(item => item.AllocatedQuantity));
        Assert.Equal(2, allocations.Select(item => item.WarehouseId).Distinct().Count());
        Assert.Single(fixture.RegionIds.Distinct());
        Assert.All(allocations, item =>
        {
            Assert.Equal(100_000m, item.UnitPrice);
            Assert.Equal(100_000m, item.PostPromotionUnitPrice);
            Assert.Equal(item.AllocatedQuantity * item.UnitPrice, item.NetAmount);
            Assert.Equal(AcceptedAt, item.PricedAt);
            Assert.Equal(AllocationInventoryStatuses.Reserved, item.Status);
            Assert.Contains(fixture.Store.Reservations, reservation =>
                reservation.AllocationId == item.Id && reservation.BatchId == item.BatchId &&
                reservation.Quantity == item.AllocatedQuantity && reservation.Status == ReservationStatuses.Active);
        });
        Assert.Equal(10m, fixture.Store.Reservations.Sum(item => item.Quantity));
        Assert.Equal(1_000_000m, Assert.Single(fixture.Store.Quotes).TotalAmount);
        Assert.Equal(AllocationRunStatuses.Succeeded, Assert.Single(fixture.Store.Runs).Status);
    }

    [Fact]
    [Trait("Report5Case", "UT-Allocation Run Result-UTCID02")]
    public async Task Short_line_returns_out_of_stock_with_sku_and_missing_quantity_without_reservation_or_quote()
    {
        var fixture = new Fixture([6m, 3m]);

        await fixture.Service.AllocateAsync(fixture.Order.Id, fixture.ActorId, default);

        Assert.Equal(OrderStatuses.OutOfStock, fixture.Order.Status);
        var run = Assert.Single(fixture.Store.Runs);
        Assert.Equal(AllocationRunStatuses.OutOfStock, run.Status);
        Assert.Contains("SKU-PLANNING", run.FailureReason);
        Assert.Contains("Thiếu 1 thùng", run.FailureReason);
        Assert.Empty(fixture.Store.Allocations);
        Assert.Empty(fixture.Store.Reservations);
        Assert.Empty(fixture.Store.Transactions);
        Assert.Empty(fixture.Store.Quotes);
        Assert.All(fixture.Balances, balance => Assert.Equal(0m, balance.ReservedQuantity));
    }

    [Fact]
    [Trait("Report5Case", "UT-Allocation Run Result-UTCID04")]
    public async Task Failure_after_temporary_fragment_selection_rolls_back_to_zero_net_reservations()
    {
        var fixture = new Fixture([10m], includeUnfulfillableSecondLine: true);
        var physicalBefore = fixture.Balances.Sum(item => item.PhysicalQuantity);

        await fixture.Service.AllocateAsync(fixture.Order.Id, fixture.ActorId, default);

        Assert.Equal(OrderStatuses.OutOfStock, fixture.Order.Status);
        Assert.Equal(AllocationRunStatuses.OutOfStock, Assert.Single(fixture.Store.Runs).Status);
        Assert.Empty(fixture.Store.Allocations);
        Assert.Empty(fixture.Store.Reservations);
        Assert.Empty(fixture.Store.Transactions);
        Assert.Empty(fixture.Store.Quotes);
        Assert.Equal(physicalBefore, fixture.Balances.Sum(item => item.PhysicalQuantity));
        Assert.All(fixture.Balances, balance =>
        {
            Assert.Equal(0m, balance.ReservedQuantity);
            Assert.Equal(balance.PhysicalQuantity, balance.AvailableQuantity);
        });
        Assert.DoesNotContain(fixture.Store.History, item => item.ToStatus == OrderStatuses.Quoted);
    }

    private sealed class Fixture
    {
        public Guid ActorId { get; } = Guid.NewGuid();
        public SalesOrder Order { get; }
        public InventoryBalance Balance => Balances[0];
        public IReadOnlyList<InventoryBalance> Balances { get; }
        public IReadOnlyList<Guid> RegionIds { get; }
        public RecordingStore Store { get; }
        public RealTimeAllocationService Service { get; }

        public Fixture(decimal[]? availableQuantities = null, bool includeUnfulfillableSecondLine = false)
        {
            availableQuantities ??= [20m];
            var product = new Product
            {
                Id = Guid.NewGuid(), ProductCategoryId = Guid.NewGuid(), Sku = "SKU-PLANNING",
                Name = "Sản phẩm lập kế hoạch", ShelfLifeHours = 720, Status = ProductStatuses.Published
            };
            var regionId = Guid.NewGuid();
            var warehouses = availableQuantities.Select((_, index) => new Warehouse
            {
                Id = Guid.NewGuid(), RegionId = regionId, Code = $"WH-PLANNING-{index + 1}",
                Name = $"Kho lập kế hoạch {index + 1}", IsActive = true
            }).ToArray();
            var batches = availableQuantities.Select((quantity, index) => new Batch
            {
                Id = Guid.NewGuid(), ProductId = product.Id, WarehouseId = warehouses[index].Id,
                BatchCode = $"LOT-PLANNING-{index + 1}", ProductionDate = new DateOnly(2026, 11, 1),
                ExpirationDate = new DateOnly(2026, 12, 15).AddDays(index), Quantity = quantity
            }).ToArray();
            Balances = batches.Select((batch, index) => new InventoryBalance
            {
                Id = Guid.NewGuid(), BatchId = batch.Id, PhysicalQuantity = availableQuantities[index],
                AvailableQuantity = availableQuantities[index], ReservedQuantity = 0m,
                Version = 1, UpdatedAt = AcceptedAt
            }).ToArray();
            RegionIds = warehouses.Select(item => item.RegionId).ToArray();
            Order = new SalesOrder
            {
                Id = Guid.NewGuid(), OrderCode = "DH-PLANNING", RetailerId = Guid.NewGuid(),
                Status = OrderStatuses.Allocating, AcceptedAt = AcceptedAt, ServerReceivedAt = AcceptedAt,
                Version = 1, CreatedAt = AcceptedAt, UpdatedAt = AcceptedAt,
                FinalPricePendingAllocation = true
            };
            var line = new SalesOrderLine
            {
                Id = Guid.NewGuid(), SalesOrderId = Order.Id, ProductId = product.Id, LineNumber = 1,
                SkuSnapshot = product.Sku, ProductNameSnapshot = product.Name, OrderedQuantity = 10m,
                UnitPrice = 100_000m, GrossAmount = 1_000_000m, NetAmount = 1_000_000m,
                MlorMinimumLifeRatio = 0.25m, MlorSourceScope = "Default", MlorSourceVersion = 1,
                PricedAt = AcceptedAt, MlorResolvedAt = AcceptedAt, CreatedAt = AcceptedAt,
                FinalPricePendingAllocation = true
            };
            var lines = new List<SalesOrderLine> { line };
            if (includeUnfulfillableSecondLine)
            {
                lines.Add(new SalesOrderLine
                {
                    Id = Guid.NewGuid(), SalesOrderId = Order.Id, ProductId = Guid.NewGuid(), LineNumber = 2,
                    SkuSnapshot = "SKU-MISSING", ProductNameSnapshot = "Sản phẩm thiếu", OrderedQuantity = 1m,
                    UnitPrice = 50_000m, GrossAmount = 50_000m, NetAmount = 50_000m,
                    MlorMinimumLifeRatio = 0.25m, MlorSourceScope = "Default", MlorSourceVersion = 1,
                    PricedAt = AcceptedAt, MlorResolvedAt = AcceptedAt, CreatedAt = AcceptedAt,
                    FinalPricePendingAllocation = true
                });
            }
            var candidates = batches.Select((batch, index) =>
                new RealTimeInventoryCandidate(batch, Balances[index], warehouses[index], product, null, null)).ToArray();
            Store = new RecordingStore(new(Order, lines, null, candidates, 1));
            var discounts = new ShelfLifeDiscountService(new NoDiscountStore(), new FixedClock(AcceptedAt));
            Service = new RealTimeAllocationService(Store, new AllocationPricingService(discounts),
                new FixedClock(AcceptedAt), new DeterministicPolicy());
        }
    }

    private sealed class DeterministicPolicy : AllocationPolicyService
    {
        public DeterministicPolicy() : base(null!, null!) { }

        public override Task<AllocationWeights> WeightsAsync(CancellationToken ct) =>
            Task.FromResult(new AllocationWeights(MaximumDistanceMeters: 30_000m));

        public override Task<BatchCandidate[]> CandidatesAsync(Guid retailerId,
            IReadOnlyCollection<RealTimeInventoryCandidate> positions, DateTimeOffset at, CancellationToken ct) =>
            Task.FromResult(positions.Select((position, index) =>
                new BatchCandidate(position.Batch.Id, position.Batch.BatchCode, position.Warehouse.Id,
                    position.Warehouse.Code, BatchExpiration.ExclusiveEnd(position.Batch.ExpirationDate),
                    position.IssuableQuantity, 1_000m + index,
                    at.AddHours(1), DateOnly.FromDateTime(at.AddHours(8).DateTime))).ToArray());
    }

    private sealed class RecordingStore(RealTimeAllocationContext context) : IRealTimeAllocationStore
    {
        public List<AllocationRun> Runs { get; } = [];
        public List<AllocationEntity> Allocations { get; } = [];
        public List<Reservation> Reservations { get; } = [];
        public List<InventoryTransaction> Transactions { get; } = [];
        public List<SalesOrderQuote> Quotes { get; } = [];
        public List<OrderStatusHistory> History { get; } = [];
        public int SaveCalls { get; private set; }

        public Task<RealTimeAllocationContext?> LoadForUpdateAsync(Guid orderId, CancellationToken ct) =>
            Task.FromResult(orderId == context.Order.Id ? context : null);
        public Task<(string OrderCode, string Status)> ReadOrderStateAsync(Guid orderId, CancellationToken ct) =>
            Task.FromResult((context.Order.OrderCode, context.Order.Status));
        public void AddRun(AllocationRun run) => Runs.Add(run);
        public void AddAllocation(AllocationEntity allocation) => Allocations.Add(allocation);
        public void AddReservation(Reservation reservation) => Reservations.Add(reservation);
        public void AddTransaction(InventoryTransaction transaction) => Transactions.Add(transaction);
        public void AddQuote(SalesOrderQuote quote) => Quotes.Add(quote);
        public void AddHistory(OrderStatusHistory history) => History.Add(history);
        public Task SaveChangesAsync(CancellationToken ct) { SaveCalls++; return Task.CompletedTask; }
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct) => operation(ct);
        public void ClearTracking() { }
    }

    private sealed class NoDiscountStore : IShelfLifeDiscountStore
    {
        public Task<bool> ScopeIsUsableAsync(string scope, Guid id, CancellationToken ct) => Task.FromResult(true);
        public Task<ShelfLifeDiscountTableView?> FindCurrentAsync(CancellationToken ct) => Task.FromResult<ShelfLifeDiscountTableView?>(null);
        public Task<ShelfLifeDiscountTableView?> FindByVersionAsync(int version, CancellationToken ct) => Task.FromResult<ShelfLifeDiscountTableView?>(null);
        public Task<ShelfLifeDiscountTableView?> FindEffectiveAtAsync(DateTimeOffset at, CancellationToken ct) => Task.FromResult<ShelfLifeDiscountTableView?>(null);
        public Task<IReadOnlyCollection<ShelfLifeDiscountTableView>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyCollection<ShelfLifeDiscountTableView>>([]);
        public Task<ShelfLifeDiscountTable?> FindCurrentForUpdateAsync(CancellationToken ct) => Task.FromResult<ShelfLifeDiscountTable?>(null);
        public Task<int> MaxVersionAsync(CancellationToken ct) => Task.FromResult(0);
        public void AddTable(ShelfLifeDiscountTable table) { }
        public void AddTiers(IEnumerable<ShelfLifeDiscountTier> tiers) { }
        public void AddAuditLog(AuditLog auditLog) { }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
