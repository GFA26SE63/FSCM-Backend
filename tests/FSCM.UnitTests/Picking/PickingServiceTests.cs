using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Picking;
using FSCM.Application.Features.QrLabels;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using DomainAllocation = FSCM.Domain.Entities.Allocation;

namespace FSCM.UnitTests.Picking;

public sealed class PickingServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Approval_plan_creates_one_deterministic_list_per_warehouse()
    {
        var fixture = new Fixture();
        fixture.Store.Lists.Clear();
        fixture.Store.Items.Clear();
        var secondWarehouse = new Warehouse { Id = Guid.NewGuid(), Code = "WH-Q7", Name = "Kho Quận 7" };
        var secondBatch = new Batch
        {
            Id = Guid.NewGuid(),
            WarehouseId = secondWarehouse.Id,
            ProductId = fixture.Product.Id,
            BatchCode = "WH-Q7-SKU001-31032027"
        };
        var secondAllocation = new DomainAllocation
        {
            Id = Guid.NewGuid(),
            SalesOrderLineId = fixture.Line.Id,
            BatchId = secondBatch.Id,
            WarehouseId = secondWarehouse.Id,
            AllocatedQuantity = 3,
            AllocationSequence = 2,
            Status = AllocationInventoryStatuses.Reserved
        };
        var view = new OrderView(
            fixture.Order,
            [new OrderLineView(fixture.Line, [fixture.Allocation, secondAllocation])],
            [],
            [],
            new Dictionary<Guid, string>
            {
                [fixture.Batch.Id] = fixture.Batch.BatchCode,
                [secondBatch.Id] = secondBatch.BatchCode
            },
            new Dictionary<Guid, string>
            {
                [fixture.Warehouse.Id] = fixture.Warehouse.Code,
                [secondWarehouse.Id] = secondWarehouse.Code
            },
            new Dictionary<Guid, int>());

        var planner = new PickingPlanService(fixture.Store);
        await planner.CreateForApprovedOrderAsync(view, Now, default);

        Assert.Equal(2, fixture.Store.Lists.Count);
        Assert.Equal(["DH-001030A", "DH-001030B"],
            fixture.Store.Lists.OrderBy(item => item.PickingCode).Select(item => item.PickingCode));
        Assert.Equal(2, fixture.Store.Items.Count);

        await planner.CancelForOrderAsync(fixture.Order.Id, Now.AddMinutes(1), default);
        Assert.All(fixture.Store.Lists, item => Assert.Equal(PickingListStatuses.Cancelled, item.Status));
        Assert.All(fixture.Store.Items, item => Assert.Equal(PickingItemStatuses.Cancelled, item.Status));
    }

    [Fact]
    public async Task Full_label_scan_completes_list_without_issuing_inventory()
    {
        var fixture = new Fixture(labelQuantity: 10);

        var result = await fixture.ScanAsync(quantity: null);

        Assert.Equal(PickingListStatuses.Completed, result.Status);
        Assert.Equal(OrderStatuses.Issued, fixture.Order.Status);
        Assert.Equal(AllocationInventoryStatuses.Reserved, fixture.Allocation.Status);
        Assert.Equal(QrLabelStatuses.Picked, fixture.Label.Status);
        Assert.Contains(fixture.Store.History, item => item.ToStatus == OrderStatuses.Picking);
        Assert.Contains(fixture.Store.History, item => item.ToStatus == OrderStatuses.Issued);
    }

    [Fact]
    public async Task Partial_scan_splits_child_label_and_preserves_parent_lineage()
    {
        var fixture = new Fixture(labelQuantity: 20);

        var result = await fixture.ScanAsync(quantity: 4);

        Assert.Equal(PickingListStatuses.InProgress, result.Status);
        Assert.Equal(4m, Assert.Single(result.Items).PickedQuantity);
        Assert.Equal(16m, fixture.Label.Quantity);
        var child = Assert.Single(fixture.Store.Labels, item => item.Id != fixture.Label.Id);
        Assert.Equal(fixture.Label.Id, child.ParentLabelId);
        Assert.Equal(fixture.Label.RootLabelId, child.RootLabelId);
        Assert.Equal(QrLabelStatuses.Picked, child.Status);
    }

    [Fact]
    public async Task Repeated_operation_key_is_idempotent()
    {
        var fixture = new Fixture(labelQuantity: 10);
        await fixture.ScanAsync(quantity: null);

        var repeated = await fixture.ScanAsync(quantity: null);

        Assert.Equal(PickingListStatuses.Completed, repeated.Status);
        Assert.Single(fixture.Store.Events, item => item.EventType == QrLabelEventTypes.Picked);
    }

    [Fact]
    public async Task Scan_rejects_label_from_another_batch()
    {
        var fixture = new Fixture(labelQuantity: 10);
        fixture.Label.BatchId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<PickingOperationException>(() => fixture.ScanAsync(null));

        Assert.Equal("conflict", exception.Code);
    }

    [Fact]
    public async Task Scan_rejects_stale_picking_version()
    {
        var fixture = new Fixture(labelQuantity: 10);

        var exception = await Assert.ThrowsAsync<PickingOperationException>(() =>
            fixture.Service.ScanAsync(
                fixture.List.PickingCode,
                new ScanPickingLabelData(fixture.Label.QrCode, null, 9, fixture.Label.Version),
                fixture.OperationKey,
                fixture.WarehouseKeeperUserId,
                SystemRoles.WarehouseKeeper,
                default));

        Assert.Equal("conflict", exception.Code);
    }

    [Fact]
    public async Task Warehouse_keeper_cannot_scan_unassigned_warehouse()
    {
        var fixture = new Fixture(labelQuantity: 10) { HasWarehouseAccess = false };

        var exception = await Assert.ThrowsAsync<PickingOperationException>(() => fixture.ScanAsync(null));

        Assert.Equal("forbidden", exception.Code);
    }

    private sealed class Fixture
    {
        public Guid WarehouseKeeperUserId { get; } = Guid.NewGuid();
        public string OperationKey { get; } = "pick-operation-001";
        public Product Product { get; }
        public Warehouse Warehouse { get; }
        public Batch Batch { get; }
        public SalesOrder Order { get; }
        public SalesOrderLine Line { get; }
        public DomainAllocation Allocation { get; }
        public PickingList List { get; }
        public PickingItem Item { get; }
        public BatchQrLabel Label { get; }
        public FakePickingStore Store { get; }
        public PickingService Service { get; }
        public bool HasWarehouseAccess { set => Store.HasWarehouseAccess = value; }

        public Fixture(decimal labelQuantity = 10)
        {
            Product = new Product { Id = Guid.NewGuid(), Sku = "SKU001", Name = "Sữa tươi 1 lít" };
            Warehouse = new Warehouse { Id = Guid.NewGuid(), Code = "WH-TD", Name = "Kho Thủ Đức" };
            Batch = new Batch
            {
                Id = Guid.NewGuid(),
                WarehouseId = Warehouse.Id,
                ProductId = Product.Id,
                BatchCode = "WH-TD-SKU001-31032027",
                Quantity = 50,
                Version = 1
            };
            Order = new SalesOrder
            {
                Id = Guid.NewGuid(),
                OrderCode = "DH-001030",
                Status = OrderStatuses.Approved,
                Version = 1,
                UpdatedAt = Now
            };
            Line = new SalesOrderLine
            {
                Id = Guid.NewGuid(),
                SalesOrderId = Order.Id,
                ProductId = Product.Id,
                LineNumber = 1,
                SkuSnapshot = Product.Sku,
                ProductNameSnapshot = Product.Name
            };
            Allocation = new DomainAllocation
            {
                Id = Guid.NewGuid(),
                SalesOrderLineId = Line.Id,
                BatchId = Batch.Id,
                WarehouseId = Warehouse.Id,
                AllocatedQuantity = 10,
                AllocationSequence = 1,
                Status = AllocationInventoryStatuses.Reserved
            };
            List = new PickingList
            {
                Id = Guid.NewGuid(),
                PickingCode = "DH-001030A",
                SalesOrderId = Order.Id,
                WarehouseId = Warehouse.Id,
                Status = PickingListStatuses.Open,
                Version = 0,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            Item = new PickingItem
            {
                Id = Guid.NewGuid(),
                PickingListId = List.Id,
                AllocationId = Allocation.Id,
                SourceBatchId = Batch.Id,
                RequestedQuantity = 10,
                Status = PickingItemStatuses.Pending
            };
            Label = new BatchQrLabel
            {
                Id = Guid.NewGuid(),
                BatchId = Batch.Id,
                CurrentWarehouseId = Warehouse.Id,
                QrCode = "TEM-000300",
                Quantity = labelQuantity,
                Status = QrLabelStatuses.Available,
                Version = 1,
                GeneratedAt = Now,
                UpdatedAt = Now
            };
            Label.RootLabelId = Label.Id;
            Store = new FakePickingStore(this);
            Service = new PickingService(Store, new FixedTimeProvider(Now));
        }

        public Task<PickingListResult> ScanAsync(decimal? quantity) =>
            Service.ScanAsync(
                List.PickingCode,
                new ScanPickingLabelData(Label.QrCode, quantity, 0, 1),
                OperationKey,
                WarehouseKeeperUserId,
                SystemRoles.WarehouseKeeper,
                default);
    }

    private sealed class FakePickingStore(Fixture fixture) : IPickingStore
    {
        public bool HasWarehouseAccess { get; set; } = true;
        public List<PickingList> Lists { get; } = [fixture.List];
        public List<PickingItem> Items { get; } = [fixture.Item];
        public List<BatchQrLabel> Labels { get; } = [fixture.Label];
        public List<QrLabelEvent> Events { get; } = [];
        public List<OrderStatusHistory> History { get; } = [];

        public Task<bool> CanAccessWarehouseAsync(Guid userId, string role, Guid warehouseId,
            CancellationToken cancellationToken) => Task.FromResult(HasWarehouseAccess);

        public Task<IReadOnlyCollection<PickingView>> ListAsync(Guid userId, string role, Guid? warehouseId,
            string? status, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<PickingView>>([ToView(fixture.List)]);

        public Task<PickingView?> FindAsync(string pickingCode, bool forUpdate,
            CancellationToken cancellationToken) =>
            Task.FromResult<PickingView?>(Lists.SingleOrDefault(item => item.PickingCode == pickingCode) is { } list
                ? ToView(list)
                : null);

        public Task<IReadOnlyCollection<PickingList>> ListByOrderAsync(Guid orderId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<PickingList>>(
                Lists.Where(item => item.SalesOrderId == orderId).ToArray());

        public Task<IReadOnlyCollection<PickingItem>> ListItemsByOrderAsync(Guid orderId,
            CancellationToken cancellationToken)
        {
            var listIds = Lists.Where(item => item.SalesOrderId == orderId).Select(item => item.Id).ToHashSet();
            return Task.FromResult<IReadOnlyCollection<PickingItem>>(
                Items.Where(item => listIds.Contains(item.PickingListId)).ToArray());
        }

        public Task<QrLabelEvent?> FindOperationAsync(string operationKey, CancellationToken cancellationToken) =>
            Task.FromResult(Events.SingleOrDefault(item => item.OperationKey == operationKey));

        public Task<BatchQrLabel?> FindLabelForUpdateAsync(string qrCode, CancellationToken cancellationToken) =>
            Task.FromResult(Labels.SingleOrDefault(item => item.QrCode == qrCode));

        public Task<BatchQrLabel?> FindLabelByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Labels.SingleOrDefault(item => item.Id == id));

        public Task<string> NextQrCodeAsync(CancellationToken cancellationToken) =>
            Task.FromResult($"TEM-{300 + Labels.Count:000000}");

        public void AddList(PickingList list) => Lists.Add(list);
        public void AddItem(PickingItem item) => Items.Add(item);
        public void AddLabel(BatchQrLabel label) => Labels.Add(label);
        public void AddLabelEvent(QrLabelEvent labelEvent) => Events.Add(labelEvent);
        public void AddOrderHistory(OrderStatusHistory history) => History.Add(history);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken) => operation(cancellationToken);
        public void ClearTracking() { }

        private PickingView ToView(PickingList list)
        {
            var itemViews = Items.Where(item => item.PickingListId == list.Id)
                .Select(item => new PickingItemView(item, fixture.Allocation, fixture.Line, fixture.Batch))
                .ToArray();
            var scans = Events.Where(item => item.EventType == QrLabelEventTypes.Picked &&
                                             item.ReferenceCode == list.PickingCode)
                .Select(item => new PickingScanView(item,
                    Labels.Single(label => label.Id == item.BatchQrLabelId).QrCode)).ToArray();
            return new PickingView(
                list, fixture.Order, fixture.Warehouse, itemViews, scans,
                new Dictionary<Guid, int> { [fixture.Batch.Id] = 3 });
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
