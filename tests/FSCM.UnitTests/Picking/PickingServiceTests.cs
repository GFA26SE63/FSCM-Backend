using FSCM.Application.Features.AllocationRuns;
using FSCM.Application.Features.Picking;
using FSCM.Application.Features.QrLabels;
using FSCM.Application.Features.Shipments;
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
        var f = new Fixture();
        f.Label.BatchId = Guid.NewGuid();
        var error = await Assert.ThrowsAsync<PickingOperationException>(() =>
            f.ScanAsync(10, [(f.First.AllocationRunAllocationId, 10)]));
        Assert.Equal("conflict", error.Code);
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

    [Fact]
    public async Task Complete_ready_creates_one_shipment_per_order_in_the_run_picking_list()
    {
        // F92: the list spans two orders (First + Second splits). A fully-picked, Ready list creates
        // one (order, warehouse) shipment each, with the picked quantity of that order.
        var f = new Fixture();
        await f.ScanAsync(10, [(f.First.AllocationRunAllocationId, 6), (f.Second.AllocationRunAllocationId, 4)]);

        var result = await f.CompleteAsync(1);

        Assert.Equal(RunPickingListStatuses.Ready, result.Status);
        Assert.Equal(2, f.Store.Shipments.Count);
        Assert.All(f.Store.Shipments, s => Assert.Equal(ShipmentStatuses.Ready, s.Status));
        Assert.All(f.Store.Shipments, s => Assert.Equal(f.List.Id, s.RunPickingListId));
        Assert.All(f.Store.Shipments, s => Assert.Equal(f.List.WarehouseId, s.WarehouseId));
        Assert.Contains(f.Store.Shipments, s => s.SalesOrderId == f.First.SalesOrderId && s.TotalQuantity == 6);
        Assert.Contains(f.Store.Shipments, s => s.SalesOrderId == f.Second.SalesOrderId && s.TotalQuantity == 4);
    }

    [Fact]
    public async Task Complete_with_exception_creates_no_shipment()
    {
        var f = new Fixture();
        await f.Service.CompleteAsync(f.List.PickingCode,
            new CompleteRunPickingData(0, "Shortage", "Thiếu hàng"),
            f.ActorId, SystemRoles.WarehouseKeeper, default);
        Assert.Empty(f.Store.Shipments);
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
            Store = new FakeStore(this);
            Service = new PickingService(Store, new Clock());
        }

        public Task<PickingListResult> ScanAsync(decimal quantity,
            (Guid AllocationId, decimal Quantity)[] splits) => Service.ScanAsync(List.PickingCode,
            new ScanPickingLabelData(Label.QrCode, quantity, 0, 1,
                splits.Select(x => new RunPickingSplitQuantityData(x.AllocationId, x.Quantity)).ToArray()),
            "scan-001", ActorId, SystemRoles.WarehouseKeeper, default);
        public Task<PickingListResult> CompleteAsync(int version) => Service.CompleteAsync(List.PickingCode,
            new CompleteRunPickingData(version, null, null), ActorId, SystemRoles.WarehouseKeeper, default);
        private RunPickingSplit NewSplit(decimal quantity, Guid priceId) => new()
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
        public List<Shipment> Shipments { get; } = [];

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
        public Task<string> NextShipmentCodeAsync(CancellationToken ct) =>
            Task.FromResult($"SHIP-{1000 + Shipments.Count:000000}");
        public Task<bool> ShipmentExistsAsync(Guid salesOrderId, Guid warehouseId, CancellationToken ct) =>
            Task.FromResult(Shipments.Any(s => s.SalesOrderId == salesOrderId && s.WarehouseId == warehouseId));
        public void AddLabel(BatchQrLabel label) { }
        public void AddLabelEvent(QrLabelEvent labelEvent) => f.Events.Add(labelEvent);
        public void AddShipment(Shipment shipment) => Shipments.Add(shipment);
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct) => operation(ct);
        public void ClearTracking() { }
        private RunPickingWorkflowView View => new(f.List, [f.Gather], [f.First, f.Second],
            f.Events.Where(x => x.EventType == QrLabelEventTypes.Picked)
                .Select(x => new RunPickingScanView(x, f.Label.QrCode)).ToArray());
    }
}
