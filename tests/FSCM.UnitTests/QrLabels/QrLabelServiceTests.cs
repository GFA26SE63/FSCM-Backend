using FSCM.Application.Features.QrLabels;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.QrLabels;

public sealed class QrLabelServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Generate_creates_code_and_is_idempotent()
    {
        var fixture = new Fixture();
        var first = await fixture.Service.GenerateAsync(new(fixture.SourceBatch.Id, 40m, null),
            "sinh-tem-01", fixture.UserId, SystemRoles.WarehouseKeeper, default);
        var repeated = await fixture.Service.GenerateAsync(new(fixture.SourceBatch.Id, 40m, null),
            "sinh-tem-01", fixture.UserId, SystemRoles.WarehouseKeeper, default);

        Assert.Equal("TEM-001000", first.QrCode);
        Assert.Equal(first.Id, repeated.Id);
        Assert.Single(fixture.Store.Labels);
        Assert.Single(fixture.Store.Events);
    }

    [Fact]
    public async Task Generate_rejects_quantity_above_unlabelled_physical_stock()
    {
        var fixture = new Fixture();
        fixture.Store.Labels.Add(fixture.CreateLabel("TEM-000204", 80m));

        var exception = await Assert.ThrowsAsync<QrLabelOperationException>(() => fixture.Service.GenerateAsync(
            new(fixture.SourceBatch.Id, 21m, null), "sinh-tem-vuot-ton", fixture.UserId,
            SystemRoles.WarehouseKeeper, default));

        Assert.Equal("conflict", exception.Code);
    }

    [Fact]
    public async Task Generate_counts_picked_stock_but_excludes_already_issued_labels()
    {
        var fixture = new Fixture();
        var picked = fixture.CreateLabel("TEM-000204", 80m);
        picked.Status = QrLabelStatuses.Picked;
        var delivered = fixture.CreateLabel("TEM-000205", 100m);
        delivered.Status = QrLabelStatuses.Delivered;
        fixture.Store.Labels.AddRange([picked, delivered]);

        var result = await fixture.Service.GenerateAsync(
            new(fixture.SourceBatch.Id, 20m, null), "sinh-tem-ton-moi", fixture.UserId,
            SystemRoles.WarehouseKeeper, default);

        Assert.Equal(20m, result.Quantity);
        Assert.Equal(200m, fixture.Store.Labels.Sum(item => item.Quantity));
    }

    [Fact]
    public async Task Split_reduces_parent_and_links_child_to_root()
    {
        var fixture = new Fixture();
        var parent = fixture.CreateLabel("TEM-000204", 80m);
        fixture.Store.Labels.Add(parent);

        var child = await fixture.Service.SplitAsync(parent.QrCode, new(30m, 1), "tach-tem-01",
            fixture.UserId, SystemRoles.WarehouseKeeper, default);

        Assert.Equal(50m, parent.Quantity);
        Assert.Equal(30m, child.Quantity);
        Assert.Equal(parent.Id, child.ParentLabelId);
        Assert.Equal(parent.Id, child.RootLabelId);
        Assert.Equal(80m, fixture.Store.Labels.Sum(item => item.Quantity));
    }

    [Fact]
    public async Task Duplicate_inspection_scan_returns_same_state_without_second_event()
    {
        var fixture = new Fixture();
        var label = fixture.CreateLabel("TEM-000204", 80m);
        fixture.Store.Labels.Add(label);
        var request = new ScanQrLabelData("Inspect", fixture.SourceWarehouse.Id, null, null, "Kiểm tra tem", 1);

        var first = await fixture.Service.ScanAsync(label.QrCode, request, "quet-xuat-01",
            fixture.UserId, SystemRoles.WarehouseKeeper, default);
        var repeated = await fixture.Service.ScanAsync(label.QrCode, request, "quet-xuat-01",
            fixture.UserId, SystemRoles.WarehouseKeeper, default);

        Assert.Equal(QrLabelStatuses.Available, first.Status);
        Assert.Equal(first.Version, repeated.Version);
        Assert.Single(fixture.Store.Events);
    }

    [Fact]
    public async Task Generic_issue_scan_is_rejected_without_changing_label()
    {
        var fixture = new Fixture();
        var label = fixture.CreateLabel("TEM-000204", 80m);
        fixture.Store.Labels.Add(label);

        var exception = await Assert.ThrowsAsync<QrLabelOperationException>(() => fixture.Service.ScanAsync(
            label.QrCode, new("Issue", fixture.SourceWarehouse.Id, null,
                "DC-0001", "Xuất kho", 1), "quet-xuat-01", fixture.UserId,
            SystemRoles.WarehouseKeeper, default));

        Assert.Equal("conflict", exception.Code);
        Assert.Equal(QrLabelStatuses.Available, label.Status);
        Assert.Empty(fixture.Store.Events);
    }

    [Fact]
    public async Task Generic_receive_scan_is_rejected_without_rebinding_label()
    {
        var fixture = new Fixture();
        var label = fixture.CreateLabel("TEM-000204", 80m);
        fixture.Store.Labels.Add(label);

        var exception = await Assert.ThrowsAsync<QrLabelOperationException>(() => fixture.Service.ScanAsync(
            label.QrCode, new("Receive", fixture.DestinationWarehouse.Id, fixture.DestinationBatch.Id,
                "DC-0001", null, 1), "quet-nhan", fixture.UserId,
            SystemRoles.WarehouseKeeper, default));

        Assert.Equal("conflict", exception.Code);
        Assert.Equal(fixture.SourceBatch.Id, label.BatchId);
        Assert.Equal(fixture.SourceWarehouse.Id, label.CurrentWarehouseId);
    }

    private sealed class Fixture
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Warehouse SourceWarehouse { get; } = new() { Id = Guid.NewGuid(), Code = "WH-HCM-01", Name = "Kho trung tâm Hồ Chí Minh" };
        public Warehouse DestinationWarehouse { get; } = new() { Id = Guid.NewGuid(), Code = "WH-TD", Name = "Kho Thủ Đức" };
        public Product Product { get; } = new() { Id = Guid.NewGuid(), Sku = "SKU001", Name = "Sữa tươi tiệt trùng" };
        public Batch SourceBatch { get; }
        public Batch DestinationBatch { get; }
        public FakeStore Store { get; }
        public QrLabelService Service { get; }

        public Fixture()
        {
            SourceBatch = new Batch { Id = Guid.NewGuid(), WarehouseId = SourceWarehouse.Id, ProductId = Product.Id, BatchCode = "WH-HCM-01-SKU001-30042027" };
            DestinationBatch = new Batch { Id = Guid.NewGuid(), WarehouseId = DestinationWarehouse.Id, ProductId = Product.Id, BatchCode = "WH-TD-SKU001-30042027" };
            Store = new FakeStore(UserId, SourceWarehouse, DestinationWarehouse, Product, SourceBatch, DestinationBatch);
            Service = new QrLabelService(Store, new FakeQrCodeRenderer(), new FixedTimeProvider(Now));
        }

        public BatchQrLabel CreateLabel(string code, decimal quantity)
        {
            var id = Guid.NewGuid();
            return new BatchQrLabel
            {
                Id = id,
                BatchId = SourceBatch.Id,
                CurrentWarehouseId = SourceWarehouse.Id,
                RootLabelId = id,
                QrCode = code,
                Quantity = quantity,
                Status = QrLabelStatuses.Available,
                Version = 1,
                GeneratedAt = Now.AddDays(-1),
                UpdatedAt = Now.AddDays(-1)
            };
        }

        public QrLabelEvent CreateEvent(BatchQrLabel label, string type, string key, string reference) => new()
        {
            Id = Guid.NewGuid(),
            BatchQrLabelId = label.Id,
            WarehouseId = label.CurrentWarehouseId,
            PerformedByUserId = UserId,
            OperationKey = key,
            EventType = type,
            FromStatus = QrLabelStatuses.Available,
            ToStatus = label.Status,
            Quantity = label.Quantity,
            ReferenceCode = reference,
            OccurredAt = Now.AddMinutes(-1)
        };
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeQrCodeRenderer : IQrCodeRenderer
    {
        public string RenderSvg(string payload) => $"<svg data-payload=\"{payload}\"></svg>";
        public byte[] RenderPng(string payload, int pixelsPerModule = 10) => System.Text.Encoding.UTF8.GetBytes(payload);
    }

    private sealed class FakeStore(
        Guid userId, Warehouse sourceWarehouse, Warehouse destinationWarehouse, Product product,
        Batch sourceBatch, Batch destinationBatch) : IQrLabelStore
    {
        public List<BatchQrLabel> Labels { get; } = [];
        public List<QrLabelEvent> Events { get; } = [];
        private readonly Batch[] _batches = [sourceBatch, destinationBatch];
        private readonly Warehouse[] _warehouses = [sourceWarehouse, destinationWarehouse];
        private int _nextCode = 1000;

        public Task<bool> CanAccessWarehouseAsync(Guid actorId, string role, Guid warehouseId, CancellationToken cancellationToken) =>
            Task.FromResult(actorId == userId && _warehouses.Any(item => item.Id == warehouseId));
        public Task<QrLabelView?> FindByCodeAsync(string qrCode, CancellationToken cancellationToken) =>
            Task.FromResult(ToView(Labels.SingleOrDefault(item => item.QrCode == qrCode)));
        public Task<BatchQrLabel?> FindForUpdateAsync(string qrCode, CancellationToken cancellationToken) =>
            Task.FromResult(Labels.SingleOrDefault(item => item.QrCode == qrCode));
        public Task<QrLabelView?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(ToView(Labels.SingleOrDefault(item => item.Id == id)));
        public Task<IReadOnlyCollection<QrLabelView>> ListAsync(Guid actorId, string role, Guid? warehouseId, Guid? batchId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<QrLabelView>>(Labels.Where(item => (!warehouseId.HasValue || item.CurrentWarehouseId == warehouseId) && (!batchId.HasValue || item.BatchId == batchId)).Select(item => ToView(item)!).ToArray());
        public Task<IReadOnlyCollection<QrLabelView>> ListLineageAsync(Guid rootLabelId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<QrLabelView>>(Labels.Where(item => item.Id == rootLabelId || item.RootLabelId == rootLabelId).Select(item => ToView(item)!).ToArray());
        public Task<IReadOnlyCollection<QrLabelEventView>> ListEventsAsync(Guid rootLabelId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<QrLabelEventView>>(Events.Select(item => new QrLabelEventView(item, _warehouses.Single(w => w.Id == item.WarehouseId).Code)).ToArray());
        public Task<QrLabelEvent?> FindEventAsync(string operationKey, CancellationToken cancellationToken) =>
            Task.FromResult(Events.SingleOrDefault(item => item.OperationKey == operationKey));
        public Task<QrLabelEvent?> FindLatestEventAsync(Guid labelId, CancellationToken cancellationToken) =>
            Task.FromResult(Events.Where(item => item.BatchQrLabelId == labelId).OrderByDescending(item => item.OccurredAt).FirstOrDefault());
        public Task<Batch?> FindBatchAsync(Guid batchId, CancellationToken cancellationToken) => Task.FromResult(_batches.SingleOrDefault(item => item.Id == batchId));
        public Task<Guid?> FindGoodsReceiptIdForBatchAsync(Guid batchId, CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);
        public Task<InventoryBalance?> FindBalanceAsync(Guid batchId, CancellationToken cancellationToken) => Task.FromResult<InventoryBalance?>(new() { BatchId = batchId, PhysicalQuantity = 100m });
        public Task<decimal> SumActiveQuantityAsync(Guid batchId, CancellationToken cancellationToken) =>
            Task.FromResult(Labels.Where(item => item.BatchId == batchId &&
                item.Status is QrLabelStatuses.Available or QrLabelStatuses.Picked).Sum(item => item.Quantity));
        public Task<string> NextQrCodeAsync(CancellationToken cancellationToken) => Task.FromResult($"TEM-{_nextCode++:000000}");
        public void AddLabel(BatchQrLabel label) => Labels.Add(label);
        public void AddEvent(QrLabelEvent labelEvent) => Events.Add(labelEvent);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private QrLabelView? ToView(BatchQrLabel? label)
        {
            if (label is null) return null;
            var batch = _batches.Single(item => item.Id == label.BatchId);
            var warehouse = _warehouses.Single(item => item.Id == label.CurrentWarehouseId);
            var parent = Labels.SingleOrDefault(item => item.Id == label.ParentLabelId)?.QrCode;
            var root = Labels.SingleOrDefault(item => item.Id == label.RootLabelId)?.QrCode ?? label.QrCode;
            return new(label, batch, warehouse, product, parent, root, null);
        }
    }
}
