using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Picking;
using FSCM.Application.Features.WarehouseConfiguration;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using DomainAllocation = FSCM.Domain.Entities.Allocation;

namespace FSCM.UnitTests.Orders;

public sealed class OrderServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sales_captures_indicative_order_without_selecting_batch_or_changing_inventory()
    {
        var fixture = new Fixture();

        var result = await fixture.CreateAsync(quantity: 10);

        Assert.Equal(OrderStatuses.PendingApproval, result.Status);
        Assert.Equal(1_000m, result.SubtotalAmount);
        Assert.Equal(100m, result.DiscountAmount);
        Assert.Equal(900m, result.FinalAmount);
        Assert.True(result.FinalPricePendingAllocation);
        Assert.True(Assert.Single(result.Lines).FinalPricePendingAllocation);
        var line = Assert.Single(result.Lines);
        Assert.Equal("KM-001", line.DiscountSourceCode);
        Assert.Equal("Sku", line.Mlor.SourceScope);
        Assert.Equal(2, line.Mlor.SourceVersion);
        Assert.Equal(0.25m, line.Mlor.MinimumLifeRatio);
        Assert.NotEqual(Guid.Empty, line.ProductPriceId);
        Assert.Equal("Khuyến mãi tháng mười", line.OrdinaryPromotion!.Name);
        Assert.Empty(result.Lines.Single().Allocations);
        Assert.Equal(0m, fixture.Store.Balance.ReservedQuantity);
        Assert.Equal(50m, fixture.Store.Balance.AvailableQuantity);
        Assert.Contains("pendingRun", fixture.Store.Orders.Single().AllocationTraceJson, StringComparison.OrdinalIgnoreCase);
        Assert.False(Assert.Single(fixture.Store.Promotions).IsServerFinal);
        Assert.Equal("KM-001", fixture.Store.Promotions.Single().PromotionCodeSnapshot);
        Assert.Equal("OrderCaptured", Assert.Single(fixture.Store.Audits).Action);
    }

    [Fact]
    public async Task Repeated_client_order_key_returns_original_order_without_inventory_side_effect()
    {
        var fixture = new Fixture();
        var request = fixture.Request(10);

        var first = await fixture.Service.CreateAsync(request, fixture.SalesUserId, default);
        var second = await fixture.Service.CreateAsync(request, fixture.SalesUserId, default);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(fixture.Store.Orders);
        Assert.Equal(0m, fixture.Store.Balance.ReservedQuantity);
        Assert.Equal(50m, fixture.Store.Balance.AvailableQuantity);
    }

    [Fact]
    public async Task Sales_cannot_create_order_outside_assigned_territory()
    {
        var fixture = new Fixture { TerritoryAllowed = false };

        var exception = await Assert.ThrowsAsync<OrderOperationException>(() => fixture.CreateAsync(10));

        Assert.Equal("forbidden", exception.Code);
        Assert.Empty(fixture.Store.Orders);
    }

    [Fact]
    public async Task Order_capture_accepts_without_checking_current_stock()
    {
        var fixture = new Fixture(physicalQuantity: 5);

        var result = await fixture.CreateAsync(10);

        Assert.Equal(OrderStatuses.PendingApproval, result.Status);
        Assert.Single(fixture.Store.Orders);
        Assert.Empty(fixture.Store.Allocations);
        Assert.Equal(5m, fixture.Store.Balance.AvailableQuantity);
    }

    [Fact]
    public async Task Configured_order_step_is_enforced()
    {
        var fixture = new Fixture();
        fixture.Store.Configurations[BusinessConfigurationKeys.OrderStepCartons] = "5";

        var exception = await Assert.ThrowsAsync<OrderOperationException>(() => fixture.CreateAsync(7));

        Assert.Equal("validation_error", exception.Code);
        Assert.Empty(fixture.Store.Orders);
    }

    [Fact]
    public async Task Existing_review_endpoint_does_not_reserve_when_capture_has_no_allocation()
    {
        var fixture = new Fixture();
        var created = await fixture.CreateAsync(10);

        var approved = await fixture.Service.ApproveAsync(
            created.Id, created.Version, fixture.OperatorUserId, default);

        Assert.Equal(OrderStatuses.Approved, approved.Status);
        Assert.Equal(0m, fixture.Store.Balance.ReservedQuantity);
        Assert.Equal(50m, fixture.Store.Balance.AvailableQuantity);
        Assert.Equal(1, approved.Version);
        Assert.Equal(0, fixture.Inventory.ReserveCalls);
        Assert.Equal(1, fixture.PickingPlan.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reject_or_cancel_has_no_inventory_side_effect_before_allocation(bool cancel)
    {
        var fixture = new Fixture();
        var created = await fixture.CreateAsync(10);

        var result = cancel
            ? await fixture.Service.CancelAsync(created.Id, created.Version, fixture.SalesUserId,
                SystemRoles.Sales, "Khách hàng đổi kế hoạch", default)
            : await fixture.Service.RejectAsync(created.Id, created.Version,
                "Không đáp ứng lịch giao", fixture.OperatorUserId, default);

        Assert.Equal(cancel ? OrderStatuses.Cancelled : OrderStatuses.Rejected, result.Status);
        Assert.Equal(50m, fixture.Store.Balance.AvailableQuantity);
        Assert.Equal(0, fixture.Inventory.ReleaseCalls);
        Assert.Equal(cancel ? 1 : 0, fixture.PickingPlan.CancelCalls);
    }

    [Fact]
    public async Task Stale_order_version_is_rejected()
    {
        var fixture = new Fixture();
        var created = await fixture.CreateAsync(10);

        var exception = await Assert.ThrowsAsync<OrderOperationException>(() =>
            fixture.Service.ApproveAsync(created.Id, created.Version + 1, fixture.OperatorUserId, default));

        Assert.Equal("conflict", exception.Code);
        Assert.Equal(OrderStatuses.PendingApproval, fixture.Store.Orders.Single().Status);
    }

    private sealed class Fixture
    {
        public Guid SalesUserId { get; } = Guid.NewGuid();
        public Guid OperatorUserId { get; } = Guid.NewGuid();
        public FakeStore Store { get; }
        public FakeInventoryLifecycle Inventory { get; }
        public FakePickingPlan PickingPlan { get; }
        public OrderService Service { get; }
        public bool TerritoryAllowed { get => Store.TerritoryAllowed; set => Store.TerritoryAllowed = value; }

        public Fixture(decimal physicalQuantity = 50)
        {
            Store = new FakeStore(SalesUserId, physicalQuantity);
            Inventory = new FakeInventoryLifecycle(Store);
            PickingPlan = new FakePickingPlan();
            Service = new OrderService(Store, new FakePricing(), Inventory, PickingPlan, new FixedTimeProvider(Now));
        }

        public CreateAssistedOrderData Request(int quantity) => new(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Store.Retailer.Id,
            OrderPaymentMethods.Cod,
            new DateOnly(2026, 10, 20),
            null,
            "Giao trong giờ hành chính",
            [new CreateOrderLineData(Store.Product.Id, quantity)]);

        public Task<OrderDetailResult> CreateAsync(int quantity) =>
            Service.CreateAsync(Request(quantity), SalesUserId, default);
    }

    private sealed class FakePickingPlan : IPickingPlanService
    {
        public int Calls { get; private set; }
        public int CancelCalls { get; private set; }

        public Task CreateForApprovedOrderAsync(
            OrderView order, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.CompletedTask;
        }

        public Task CancelForOrderAsync(
            Guid orderId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            CancelCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStore : IOrderStore
    {
        private readonly Guid salesUserId;
        public Retailer Retailer { get; }
        public Product Product { get; }
        public Warehouse Warehouse { get; }
        public Batch Batch { get; }
        public InventoryBalance Balance { get; }
        public MapLocation RetailerLocation { get; }
        public bool TerritoryAllowed { get; set; } = true;
        public Dictionary<string, string> Configurations { get; } = new(StringComparer.Ordinal)
        {
            [BusinessConfigurationKeys.AllocationWeights] = "{\"expiry\":0.5,\"distance\":0.3,\"coverage\":0.2}",
            [BusinessConfigurationKeys.ServiceRadiusMeters] = "20000",
            [BusinessConfigurationKeys.DefaultMlorRatio] = "0.25",
            [BusinessConfigurationKeys.OrderBufferStockCartons] = "0",
            [BusinessConfigurationKeys.OrderStepCartons] = "1"
        };
        public List<SalesOrder> Orders { get; } = [];
        public List<SalesOrderLine> Lines { get; } = [];
        public List<DomainAllocation> Allocations { get; } = [];
        public List<OrderPromotion> Promotions { get; } = [];
        public List<OrderStatusHistory> History { get; } = [];
        public List<AuditLog> Audits { get; } = [];

        public FakeStore(Guid salesUserId, decimal physicalQuantity)
        {
            this.salesUserId = salesUserId;
            Retailer = new Retailer
            {
                Id = Guid.NewGuid(),
                Code = "10004127",
                Name = "Cửa hàng An Phú",
                Address = "Thành phố Hồ Chí Minh",
                Status = "Active",
                CreditTermDays = 14
            };
            Product = new Product
            {
                Id = Guid.NewGuid(),
                Sku = "SKU001",
                Name = "Sữa tươi 1 lít",
                Status = ProductStatuses.Published,
                ShelfLifeHours = 720
            };
            Warehouse = new Warehouse { Id = Guid.NewGuid(), Code = "WH-TD", Name = "Kho vùng Thủ Đức", IsActive = true };
            Batch = new Batch
            {
                Id = Guid.NewGuid(),
                WarehouseId = Warehouse.Id,
                ProductId = Product.Id,
                BatchCode = "WH-TD-SKU001-30112026",
                ExpirationDate = new DateOnly(2026, 11, 30),
                Quantity = physicalQuantity
            };
            Balance = new InventoryBalance
            {
                Id = Guid.NewGuid(),
                BatchId = Batch.Id,
                PhysicalQuantity = physicalQuantity,
                AvailableQuantity = physicalQuantity
            };
            RetailerLocation = new MapLocation
            {
                Id = Guid.NewGuid(),
                RetailerId = Retailer.Id,
                Latitude = 10.802m,
                Longitude = 106.743m
            };
        }

        public Task<SalesOrder?> FindByClientOrderKeyAsync(Guid clientOrderKey, CancellationToken cancellationToken) =>
            Task.FromResult(Orders.SingleOrDefault(item => item.ClientOrderKey == clientOrderKey));
        public Task<OrderRetailerContext?> FindRetailerContextAsync(Guid userId, Guid retailerId, CancellationToken cancellationToken) =>
            Task.FromResult<OrderRetailerContext?>(TerritoryAllowed && userId == salesUserId && retailerId == Retailer.Id
                ? new(Retailer, Guid.Parse("20000000-0000-0000-0000-000000000001")) : null);
        public Task<Product?> FindPublishedProductAsync(Guid productId, CancellationToken cancellationToken) =>
            Task.FromResult<Product?>(productId == Product.Id ? Product : null);
        public Task<IReadOnlyCollection<OrderInventoryCandidate>> ListCandidatesAsync(Guid productId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<OrderInventoryCandidate>>(
                [new(Batch, Balance, Warehouse, 10.802m, 106.743m)]);
        public Task<MapLocation?> FindRetailerLocationAsync(Guid retailerId, CancellationToken cancellationToken) =>
            Task.FromResult<MapLocation?>(retailerId == Retailer.Id ? RetailerLocation : null);
        public Task<OrderMlorSnapshot?> ResolveMlorAsync(
            Guid retailerId, Guid productId, DateTimeOffset at, CancellationToken cancellationToken) =>
            Task.FromResult<OrderMlorSnapshot?>(new(
                "Sku", Guid.Parse("30000000-0000-0000-0000-000000000001"), 2,
                null, 0.25m, null, at));
        public Task<string?> FindConfigurationAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult(Configurations.GetValueOrDefault(key));
        public Task<string> NextOrderCodeAsync(CancellationToken cancellationToken) => Task.FromResult("DH-001030");
        public Task<OrderView?> FindAsync(Guid orderId, bool forUpdate, CancellationToken cancellationToken) =>
            Task.FromResult(ToView(Orders.SingleOrDefault(item => item.Id == orderId)));
        public Task<OrderView?> FindByCodeAsync(string orderCode, bool forUpdate, CancellationToken cancellationToken) =>
            Task.FromResult(ToView(Orders.SingleOrDefault(item => item.OrderCode == orderCode)));
        public Task<(IReadOnlyCollection<OrderView> Items, int TotalCount)> ListAsync(
            Guid actorUserId, string actorRole, OrderListQuery query, CancellationToken cancellationToken)
        {
            var items = Orders.Where(item => !query.CreatedByMe || item.CreatedByUserId == actorUserId)
                .Select(item => ToView(item)!).ToArray();
            return Task.FromResult<(IReadOnlyCollection<OrderView>, int)>((items, items.Length));
        }
        public Task<bool> CanViewAsync(OrderView order, Guid actorUserId, string actorRole, CancellationToken cancellationToken) =>
            Task.FromResult(actorRole is SystemRoles.Admin or SystemRoles.Operator || order.Order.CreatedByUserId == actorUserId);
        public void AddOrder(SalesOrder order) => Orders.Add(order);
        public void AddLine(SalesOrderLine line) => Lines.Add(line);
        public void AddAllocation(DomainAllocation allocation) => Allocations.Add(allocation);
        public void AddPromotion(OrderPromotion promotion) => Promotions.Add(promotion);
        public void AddHistory(OrderStatusHistory history) => History.Add(history);
        public void AddAuditLog(AuditLog auditLog) => Audits.Add(auditLog);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken) => operation(cancellationToken);
        public void ClearTracking() { }

        private OrderView? ToView(SalesOrder? order)
        {
            if (order is null) return null;
            var lines = Lines.Where(item => item.SalesOrderId == order.Id)
                .Select(line => new OrderLineView(line,
                    Allocations.Where(item => item.SalesOrderLineId == line.Id).ToArray(),
                    Promotions.SingleOrDefault(item => item.SalesOrderLineId == line.Id))).ToArray();
            return new(order, lines, History.Where(item => item.SalesOrderId == order.Id).ToArray(), [],
                new Dictionary<Guid, string> { [Batch.Id] = Batch.BatchCode },
                new Dictionary<Guid, string> { [Warehouse.Id] = Warehouse.Code },
                new Dictionary<Guid, int> { [Batch.Id] = Balance.Version });
        }
    }

    private sealed class FakePricing : IOrderPricingService
    {
        public Task<OrderPriceSnapshot> CalculateAsync(Guid productId, int quantity,
            Guid retailerId, DateTimeOffset pricedAt, CancellationToken cancellationToken)
        {
            var gross = quantity * 100m;
            return Task.FromResult(new OrderPriceSnapshot(100m, gross, 10m, gross * 0.1m,
                gross * 0.9m, Guid.Parse("50000000-0000-0000-0000-000000000001"),
                Now.AddDays(-30), null, Guid.NewGuid(), "Campaign", "KM-001",
                "Khuyến mãi tháng mười", 5m, Now.AddDays(-1), Now.AddDays(10)));
        }
    }

    private sealed class FakeInventoryLifecycle(FakeStore store) : IInventoryLifecycleService
    {
        public int ReserveCalls { get; private set; }
        public int ReleaseCalls { get; private set; }

        public Task<InventoryLifecycleResult> ReserveAsync(Guid allocationId, Guid actorUserId, int expectedVersion, CancellationToken cancellationToken)
        {
            ReserveCalls++;
            var allocation = store.Allocations.Single(item => item.Id == allocationId);
            store.Balance.ReservedQuantity += allocation.AllocatedQuantity;
            store.Balance.AvailableQuantity = store.Balance.PhysicalQuantity - store.Balance.ReservedQuantity;
            store.Balance.Version++;
            allocation.Status = AllocationInventoryStatuses.Reserved;
            return Task.FromResult(Result(allocation));
        }

        public Task<InventoryLifecycleResult> ReleaseAsync(Guid allocationId, Guid actorUserId, int expectedVersion, CancellationToken cancellationToken)
        {
            ReleaseCalls++;
            var allocation = store.Allocations.Single(item => item.Id == allocationId);
            if (allocation.Status == AllocationInventoryStatuses.Reserved)
            {
                store.Balance.ReservedQuantity -= allocation.AllocatedQuantity;
                store.Balance.AvailableQuantity = store.Balance.PhysicalQuantity - store.Balance.ReservedQuantity;
                store.Balance.Version++;
            }
            allocation.Status = AllocationInventoryStatuses.Released;
            return Task.FromResult(Result(allocation));
        }

        public Task<InventoryLifecycleResult> IssueAsync(Guid allocationId, Guid actorUserId, int expectedVersion, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private InventoryLifecycleResult Result(DomainAllocation allocation) => new(
            allocation.Id, allocation.BatchId, allocation.WarehouseId, allocation.AllocatedQuantity,
            allocation.Status, store.Balance.PhysicalQuantity, store.Balance.ReservedQuantity,
            store.Balance.AvailableQuantity, store.Balance.Version);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
