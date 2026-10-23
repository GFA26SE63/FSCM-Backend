using FSCM.Application.Features.AllocationRuns;
using FSCM.Application.Features.Picking;
using FSCM.Application.Features.QrLabels;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Picking;

public sealed class PickingServiceTests
{
    [Fact]
    public async Task Scan_updates_gather_and_named_allocation_splits_without_issuing_stock()
    {
        var f = new Fixture();
        var result = await f.ScanAsync(10, [(f.First.AllocationRunAllocationId, 6), (f.Second.AllocationRunAllocationId, 4)]);

        Assert.Equal(RunPickingListStatuses.Picking, result.Status);
        Assert.Equal(10, Assert.Single(result.Gathers).PickedQuantity);
        Assert.Equal([4m, 6m], result.Gathers.Single().Splits.Select(x => x.PickedQuantity).Order());
        Assert.Equal(QrLabelStatuses.Picked, f.Label.Status);
        Assert.Equal(f.FirstPriceId, result.Gathers.Single().Splits
            .Single(x => x.AllocationRunAllocationId == f.First.AllocationRunAllocationId).AllocationPlanPriceId);
    }

    [Fact]
    public async Task Scan_rejects_wrong_warehouse_batch_and_duplicate_allocation()
    {
        var fixture = new Fixture(labelQuantity: 10);

        var result = await fixture.ScanAsync(quantity: null);

        Assert.Equal(PickingListStatuses.Completed, result.Status);
        Assert.Equal(OrderStatuses.Issued, fixture.Order.Status);
        Assert.Equal(AllocationInventoryStatuses.Reserved, fixture.Allocation.Status);
        Assert.Equal(QrLabelStatuses.Picked, fixture.Label.Status);
        Assert.Contains(fixture.Store.History, item => item.ToStatus == OrderStatuses.Picking);
        Assert.Contains(fixture.Store.History, item => item.ToStatus == OrderStatuses.Issued);
        // F92: completing the warehouse picking list creates the (order, warehouse) shipment part.
        var shipment = Assert.Single(fixture.Store.Shipments);
        Assert.Equal(fixture.List.Id, shipment.PickingListId);
        Assert.Equal(fixture.Warehouse.Id, shipment.WarehouseId);
        Assert.Equal(10, shipment.TotalQuantity);
    }

    [Fact]
    public async Task Scan_is_idempotent_by_operation_key()
    {
        var f = new Fixture();
        await f.ScanAsync(10, [(f.First.AllocationRunAllocationId, 6), (f.Second.AllocationRunAllocationId, 4)]);
        var repeated = await f.ScanAsync(10, [(f.First.AllocationRunAllocationId, 6), (f.Second.AllocationRunAllocationId, 4)]);
        Assert.Equal(10, repeated.Gathers.Single().PickedQuantity);
        Assert.Single(f.Events, x => x.EventType == QrLabelEventTypes.Picked);
    }

    [Fact]
    public async Task Complete_requires_every_gather_and_split_then_marks_warehouse_ready()
    {
        var f = new Fixture();
        var error = await Assert.ThrowsAsync<PickingOperationException>(() => f.CompleteAsync(0));
        Assert.Equal("conflict", error.Code);
        await f.ScanAsync(10, [(f.First.AllocationRunAllocationId, 6), (f.Second.AllocationRunAllocationId, 4)]);
        var ready = await f.CompleteAsync(1);
        Assert.Equal(RunPickingListStatuses.Ready, ready.Status);
        Assert.Equal(f.ActorId, ready.CompletedByUserId);
    }

    [Fact]
    public async Task Complete_records_shortage_as_needs_handling_without_faking_readiness()
    {
        var f = new Fixture();
        var result = await f.Service.CompleteAsync(f.List.PickingCode,
            new CompleteRunPickingData(0, "Shortage", "Thiếu hai thùng tại vị trí lấy hàng"),
            f.ActorId, SystemRoles.WarehouseKeeper, default);
        Assert.Equal(RunPickingListStatuses.NeedsHandling, result.Status);
        Assert.Equal("Shortage", result.ExceptionType);
        Assert.Equal(0, f.Gather.PickedQuantity);
    }

    private sealed class Fixture
    {
        public Guid ActorId { get; } = Guid.NewGuid();
        public Guid FirstPriceId { get; } = Guid.NewGuid();
        public RunPickingList List { get; }
        public RunPickingGather Gather { get; }
        public RunPickingSplit First { get; }
        public RunPickingSplit Second { get; }
        public BatchQrLabel Label { get; }
        public List<QrLabelEvent> Events { get; } = [];
        public FakeStore Store { get; }
        public PickingService Service { get; }

        public Fixture()
        {
            var warehouseId = Guid.NewGuid();
            var batchId = Guid.NewGuid();
            List = new RunPickingList
            {
                Id = Guid.NewGuid(), PickingCode = "PS-DOT-20261023-01-01", AllocationRunId = Guid.NewGuid(),
                AllocationPlanVersionId = Guid.NewGuid(), WarehouseId = warehouseId,
                Status = RunPickingListStatuses.Issued, IssuedAt = Now, CreatedAt = Now, UpdatedAt = Now
            };
            Gather = new RunPickingGather
            {
                Id = Guid.NewGuid(), RunPickingListId = List.Id, ProductId = Guid.NewGuid(), SkuSnapshot = "SKU001",
                BatchId = batchId, BatchCodeSnapshot = "WH-TD-SKU001-31122026", RequiredQuantity = 10,
                Status = RunPickingGatherStatuses.Pending, CreatedAt = Now
            };
            First = NewSplit(6, FirstPriceId);
            Second = NewSplit(4, Guid.NewGuid());
            Label = new BatchQrLabel
            {
                Id = Guid.NewGuid(), BatchId = batchId, CurrentWarehouseId = warehouseId, QrCode = "TEM-000123",
                Quantity = 10, Status = QrLabelStatuses.Available, Version = 1, GeneratedAt = Now, UpdatedAt = Now
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
        public List<Shipment> Shipments { get; } = [];

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

        public Task<string> NextShipmentCodeAsync(CancellationToken cancellationToken) =>
            Task.FromResult($"SHIP-{1000 + Shipments.Count:000000}");

        public void AddList(PickingList list) => Lists.Add(list);
        public void AddItem(PickingItem item) => Items.Add(item);
        public void AddLabel(BatchQrLabel label) => Labels.Add(label);
        public void AddLabelEvent(QrLabelEvent labelEvent) => Events.Add(labelEvent);
        public void AddOrderHistory(OrderStatusHistory history) => History.Add(history);
        public void AddShipment(Shipment shipment) => Shipments.Add(shipment);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken) => operation(cancellationToken);
        public void ClearTracking() { }

        private PickingView ToView(PickingList list)
        {
            Id = Guid.NewGuid(), RunPickingGatherId = Gather.Id, AllocationRunAllocationId = Guid.NewGuid(),
            AllocationRunLineId = Guid.NewGuid(), AllocationPlanPriceId = priceId, SalesOrderId = Guid.NewGuid(),
            SalesOrderLineId = Guid.NewGuid(), OrderCodeSnapshot = $"DH-{Guid.NewGuid():N}"[..9],
            Quantity = quantity, CreatedAt = Now
        };
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 23, 3, 0, 0, TimeSpan.Zero);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }

    private sealed class FakeStore(Fixture f) : IRunPickingStore
    {
        public Task<bool> CanAccessWarehouseAsync(Guid userId, string role, Guid warehouseId, CancellationToken ct) =>
            Task.FromResult(true);
        public Task<IReadOnlyCollection<RunPickingWorkflowView>> ListAsync(Guid userId, string role,
            Guid? warehouseId, string? status, CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<RunPickingWorkflowView>>([View]);
        public Task<RunPickingWorkflowView?> FindAsync(string code, bool update, CancellationToken ct) =>
            Task.FromResult<RunPickingWorkflowView?>(code == f.List.PickingCode ? View : null);
        public Task<QrLabelEvent?> FindOperationAsync(string key, CancellationToken ct) =>
            Task.FromResult(f.Events.SingleOrDefault(x => x.OperationKey == key));
        public Task<BatchQrLabel?> FindLabelForUpdateAsync(string code, CancellationToken ct) =>
            Task.FromResult<BatchQrLabel?>(code == f.Label.QrCode ? f.Label : null);
        public Task<string> NextQrCodeAsync(CancellationToken ct) => Task.FromResult("TEM-000124");
        public void AddLabel(BatchQrLabel label) { }
        public void AddLabelEvent(QrLabelEvent labelEvent) => f.Events.Add(labelEvent);
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct) => operation(ct);
        public void ClearTracking() { }
        private RunPickingWorkflowView View => new(f.List, [f.Gather], [f.First, f.Second],
            f.Events.Where(x => x.EventType == QrLabelEventTypes.Picked)
                .Select(x => new RunPickingScanView(x, f.Label.QrCode)).ToArray());
    }
}
