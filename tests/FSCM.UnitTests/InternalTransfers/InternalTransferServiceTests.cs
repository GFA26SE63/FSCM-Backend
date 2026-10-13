using FSCM.Application.Features.InternalTransfers;
using FSCM.Application.Features.QrLabels;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.InternalTransfers;

public sealed class InternalTransferServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Suggestions_use_configured_threshold_and_fefo_batches()
    {
        var fixture = new Fixture();
        fixture.Store.AddPosition(fixture.Central, fixture.Product, new DateOnly(2026, 11, 1), 20m, "LO-SOM");
        fixture.Store.AddPosition(fixture.Central, fixture.Product, new DateOnly(2026, 12, 1), 30m, "LO-MUON");
        fixture.Store.AddPosition(fixture.Regional, fixture.Product, new DateOnly(2026, 12, 1), 10m, "LO-DICH");

        var suggestion = Assert.Single(await fixture.Service.GetSuggestionsAsync(fixture.Regional.Id, fixture.Product.Id, default));

        Assert.Equal(25m, suggestion.SuggestedQuantity);
        Assert.Equal([20m, 5m], suggestion.FefoBatches.Select(item => item.SuggestedQuantity));
        Assert.True(suggestion.FefoBatches.First().ExpirationDate < suggestion.FefoBatches.Last().ExpirationDate);
    }

    [Fact]
    public async Task Create_rejects_non_central_source_direction()
    {
        var fixture = new Fixture();
        var position = fixture.Store.AddPosition(fixture.Regional, fixture.Product, new DateOnly(2026, 12, 1), 20m, "LO-VUNG");

        var exception = await Assert.ThrowsAsync<InternalTransferOperationException>(() => fixture.Service.CreateAsync(
            new(fixture.Regional.Id, fixture.Central.Id, [new(position.Batch.Id, 10m, null)], null),
            "tao-phieu-sai-huong", fixture.OperatorId, SystemRoles.Operator, default));

        Assert.Equal("validation_error", exception.Code);
    }

    [Fact]
    public async Task Create_is_idempotent_and_generates_requested_document()
    {
        var fixture = new Fixture();
        var position = fixture.Store.AddPosition(fixture.Central, fixture.Product, new DateOnly(2026, 12, 1), 50m, "LO-NGUON");
        var request = new CreateInternalTransferData(fixture.Central.Id, fixture.Regional.Id,
            [new(position.Batch.Id, 20m, "Bổ sung tồn kho")], "Phiếu điều chuyển mẫu");

        var first = await fixture.Service.CreateAsync(request, "tao-phieu-01", fixture.OperatorId, SystemRoles.Operator, default);
        var repeated = await fixture.Service.CreateAsync(request, "tao-phieu-01", fixture.OperatorId, SystemRoles.Operator, default);

        Assert.Equal(first.Id, repeated.Id);
        Assert.Equal(InternalTransferStatuses.Requested, first.Status);
        Assert.Single(fixture.Store.Transfers);
    }

    [Fact]
    public async Task Create_rejects_reused_key_with_different_quantity()
    {
        var fixture = new Fixture();
        var position = fixture.Store.AddPosition(fixture.Central, fixture.Product, new DateOnly(2026, 12, 1), 50m, "LO-NGUON");
        await fixture.Service.CreateAsync(new(fixture.Central.Id, fixture.Regional.Id,
            [new(position.Batch.Id, 20m, null)], null), "tao-phieu-trung-khoa",
            fixture.OperatorId, SystemRoles.Operator, default);

        var exception = await Assert.ThrowsAsync<InternalTransferOperationException>(() => fixture.Service.CreateAsync(
            new(fixture.Central.Id, fixture.Regional.Id, [new(position.Batch.Id, 10m, null)], null),
            "tao-phieu-trung-khoa", fixture.OperatorId, SystemRoles.Operator, default));

        Assert.Equal("conflict", exception.Code);
    }

    [Fact]
    public async Task Create_rejects_later_expiry_when_earlier_batch_is_available()
    {
        var fixture = new Fixture();
        fixture.Store.AddPosition(fixture.Central, fixture.Product, new DateOnly(2026, 11, 1), 20m, "LO-SOM");
        var later = fixture.Store.AddPosition(fixture.Central, fixture.Product, new DateOnly(2026, 12, 1), 20m, "LO-MUON");

        var exception = await Assert.ThrowsAsync<InternalTransferOperationException>(() => fixture.Service.CreateAsync(
            new(fixture.Central.Id, fixture.Regional.Id, [new(later.Batch.Id, 10m, null)], null),
            "tao-phieu-khong-fefo", fixture.OperatorId, SystemRoles.Operator, default));

        Assert.Equal("conflict", exception.Code);
    }

    [Fact]
    public async Task Dispatch_and_receive_conserve_quantity_and_preserve_qr_code()
    {
        var fixture = new Fixture();
        var source = fixture.Store.AddPosition(fixture.Central, fixture.Product, new DateOnly(2026, 12, 1), 100m, "LO-NGUON");
        var transfer = fixture.Store.AddApprovedTransfer(source.Batch, fixture.Central, fixture.Regional, fixture.OperatorId, 10m);
        var item = fixture.Store.Items.Single();
        var label = fixture.Store.AddLabel(source.Batch, fixture.Central, "TEM-000300", 10m);

        var dispatched = await fixture.Service.DispatchAsync(transfer.Id,
            new(transfer.Version, [new(item.Id, [label.QrCode])]), "xuat-phieu-01",
            fixture.KeeperId, SystemRoles.WarehouseKeeper, default);

        Assert.Equal(InternalTransferStatuses.InTransit, dispatched.Status);
        Assert.Equal(90m, source.Balance.PhysicalQuantity);
        Assert.Equal(QrLabelStatuses.InTransit, label.Status);

        var received = await fixture.Service.ReceiveAsync(transfer.Id,
            new(dispatched.Version, [label.QrCode]), "nhan-phieu-01",
            fixture.KeeperId, SystemRoles.WarehouseKeeper, default);
        var destination = fixture.Store.Positions.Single(position => position.Batch.WarehouseId == fixture.Regional.Id);

        Assert.Equal(InternalTransferStatuses.Received, received.Status);
        Assert.Equal(10m, destination.Balance.PhysicalQuantity);
        Assert.Equal(100m, source.Balance.PhysicalQuantity + destination.Balance.PhysicalQuantity);
        Assert.Equal("TEM-000300", label.QrCode);
        Assert.Equal(destination.Batch.Id, label.BatchId);
        Assert.Equal(QrLabelStatuses.Available, label.Status);
    }

    [Fact]
    public async Task Dispatch_rejects_scanned_quantity_that_differs_from_document()
    {
        var fixture = new Fixture();
        var source = fixture.Store.AddPosition(fixture.Central, fixture.Product, new DateOnly(2026, 12, 1), 100m, "LO-NGUON");
        var transfer = fixture.Store.AddApprovedTransfer(source.Batch, fixture.Central, fixture.Regional, fixture.OperatorId, 10m);
        var item = fixture.Store.Items.Single();
        var label = fixture.Store.AddLabel(source.Batch, fixture.Central, "TEM-000301", 9m);

        var exception = await Assert.ThrowsAsync<InternalTransferOperationException>(() => fixture.Service.DispatchAsync(
            transfer.Id, new(transfer.Version, [new(item.Id, [label.QrCode])]), "xuat-sai-so-luong",
            fixture.KeeperId, SystemRoles.WarehouseKeeper, default));

        Assert.Equal("conflict", exception.Code);
        Assert.Equal(100m, source.Balance.PhysicalQuantity);
    }

    private sealed class Fixture
    {
        public Guid OperatorId { get; } = Guid.NewGuid();
        public Guid KeeperId { get; } = Guid.NewGuid();
        public Warehouse Central { get; } = new() { Id = Guid.NewGuid(), Code = "WH-HCM-01", Name = "Kho trung tâm", Type = WarehouseTypes.Central, IsActive = true };
        public Warehouse Regional { get; } = new() { Id = Guid.NewGuid(), Code = "WH-TD", Name = "Kho vùng", Type = WarehouseTypes.Regional, IsActive = true };
        public Product Product { get; } = new() { Id = Guid.NewGuid(), Sku = "SKU001", Name = "Sữa tiệt trùng" };
        public FakeStore Store { get; }
        public InternalTransferService Service { get; }

        public Fixture()
        {
            Store = new FakeStore(KeeperId, Central, Regional);
            Service = new(Store, new FixedTimeProvider(Now));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeStore(Guid keeperId, Warehouse central, Warehouse regional) : IInternalTransferStore
    {
        public List<TransferInventoryView> Positions { get; } = [];
        public List<WarehouseTransfer> Transfers { get; } = [];
        public List<WarehouseTransferItem> Items { get; } = [];
        public List<WarehouseTransferEvent> Events { get; } = [];
        public List<WarehouseTransferLabel> Links { get; } = [];
        public List<BatchQrLabel> Labels { get; } = [];
        public List<QrLabelEvent> QrEvents { get; } = [];
        public List<InventoryTransaction> Transactions { get; } = [];
        private readonly Warehouse[] _warehouses = [central, regional];
        private int _sequence;

        public TransferInventoryView AddPosition(Warehouse warehouse, Product product, DateOnly expiration, decimal quantity, string suffix)
        {
            var batch = new Batch
            {
                Id = Guid.NewGuid(),
                WarehouseId = warehouse.Id,
                ProductId = product.Id,
                BatchCode = $"{warehouse.Code}-{product.Sku}-{suffix}",
                ExpirationDate = expiration,
                ExpiryStatus = "Valid",
                StockStatus = "InStock",
                Quantity = quantity,
                Version = 1
            };
            var position = new TransferInventoryView(batch,
                new InventoryBalance { Id = Guid.NewGuid(), BatchId = batch.Id, PhysicalQuantity = quantity, AvailableQuantity = quantity, Version = 1 },
                warehouse, product);
            Positions.Add(position);
            return position;
        }

        public WarehouseTransfer AddApprovedTransfer(Batch batch, Warehouse source, Warehouse destination, Guid userId, decimal quantity)
        {
            var transfer = new WarehouseTransfer
            {
                Id = Guid.NewGuid(),
                TransferCode = "DC-261013-01",
                SourceWarehouseId = source.Id,
                DestinationWarehouseId = destination.Id,
                CreatedByUserId = userId,
                ApprovedByUserId = userId,
                Status = InternalTransferStatuses.Approved,
                Version = 2,
                RequestedAt = Now,
                ApprovedAt = Now
            };
            Transfers.Add(transfer);
            Items.Add(new WarehouseTransferItem
            {
                Id = Guid.NewGuid(),
                WarehouseTransferId = transfer.Id,
                SourceBatchId = batch.Id,
                RequestedQuantity = quantity,
                Status = InternalTransferStatuses.Approved,
                CreatedAt = Now,
                UpdatedAt = Now
            });
            return transfer;
        }

        public BatchQrLabel AddLabel(Batch batch, Warehouse warehouse, string code, decimal quantity)
        {
            var label = new BatchQrLabel
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                CurrentWarehouseId = warehouse.Id,
                QrCode = code,
                Quantity = quantity,
                Status = QrLabelStatuses.Available,
                Version = 1
            };
            Labels.Add(label);
            return label;
        }

        public Task<bool> CanAccessWarehouseAsync(Guid userId, string role, Guid warehouseId, CancellationToken cancellationToken) =>
            Task.FromResult(role is SystemRoles.Admin or SystemRoles.Operator || userId == keeperId && _warehouses.Any(item => item.Id == warehouseId));
        public Task<Warehouse?> FindWarehouseAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(_warehouses.SingleOrDefault(item => item.Id == id));
        public Task<TransferInventoryView?> FindInventoryForUpdateAsync(Guid batchId, CancellationToken cancellationToken) => Task.FromResult(Positions.SingleOrDefault(item => item.Batch.Id == batchId));
        public Task<IReadOnlyCollection<TransferInventoryView>> ListSuggestionInventoryAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<TransferInventoryView>>(Positions);
        public Task<IReadOnlyCollection<Warehouse>> ListActiveRegionalWarehousesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<Warehouse>>([regional]);
        public Task<int> GetRegionalMinimumStockAsync(CancellationToken cancellationToken) => Task.FromResult(35);
        public Task<WarehouseTransferEvent?> FindEventAsync(string operationKey, CancellationToken cancellationToken) => Task.FromResult(Events.SingleOrDefault(item => item.OperationKey == operationKey));
        public Task<TransferView?> FindTransferAsync(Guid transferId, bool tracking, CancellationToken cancellationToken) => Task.FromResult(ToView(Transfers.SingleOrDefault(item => item.Id == transferId)));
        public Task<IReadOnlyCollection<TransferView>> ListTransfersAsync(Guid userId, string role, Guid? warehouseId, string? status, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<TransferView>>(Transfers.Where(item => status is null || item.Status == status).Select(item => ToView(item)!).ToArray());
        public Task<IReadOnlyCollection<BatchQrLabel>> FindLabelsForUpdateAsync(IReadOnlyCollection<string> qrCodes, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<BatchQrLabel>>(Labels.Where(item => qrCodes.Contains(item.QrCode)).ToArray());
        public Task<Batch?> FindDestinationBatchForUpdateAsync(Guid warehouseId, Guid productId, DateOnly expirationDate, CancellationToken cancellationToken) =>
            Task.FromResult(Positions.Select(item => item.Batch).SingleOrDefault(item => item.WarehouseId == warehouseId && item.ProductId == productId && item.ExpirationDate == expirationDate));
        public Task<InventoryBalance?> FindBalanceForUpdateAsync(Guid batchId, CancellationToken cancellationToken) =>
            Task.FromResult(Positions.SingleOrDefault(item => item.Batch.Id == batchId)?.Balance);
        public Task<string> NextTransferCodeAsync(DateOnly businessDate, CancellationToken cancellationToken) => Task.FromResult($"DC-{businessDate:yyMMdd}-{++_sequence:00}");
        public void AddTransfer(WarehouseTransfer transfer) => Transfers.Add(transfer);
        public void AddTransferItem(WarehouseTransferItem item) => Items.Add(item);
        public void AddTransferEvent(WarehouseTransferEvent transferEvent) => Events.Add(transferEvent);
        public void AddTransferLabel(WarehouseTransferLabel link) => Links.Add(link);
        public void AddQrLabelEvent(QrLabelEvent labelEvent) => QrEvents.Add(labelEvent);
        public void AddBatch(Batch batch)
        {
            var product = Positions.Select(item => item.Product).First(item => item.Id == batch.ProductId);
            var warehouse = _warehouses.Single(item => item.Id == batch.WarehouseId);
            Positions.Add(new(batch, new InventoryBalance { BatchId = batch.Id }, warehouse, product));
        }
        public void AddBalance(InventoryBalance balance)
        {
            var position = Positions.FindIndex(item => item.Batch.Id == balance.BatchId);
            Positions[position] = Positions[position] with { Balance = balance };
        }
        public void AddInventoryTransaction(InventoryTransaction transaction) => Transactions.Add(transaction);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private TransferView? ToView(WarehouseTransfer? transfer)
        {
            if (transfer is null) return null;
            var itemViews = Items.Where(item => item.WarehouseTransferId == transfer.Id).Select(item =>
            {
                var position = Positions.Single(candidate => candidate.Batch.Id == item.SourceBatchId);
                var labelViews = Links.Where(link => link.WarehouseTransferItemId == item.Id)
                    .Select(link => new TransferLabelView(link, Labels.Single(label => label.Id == link.BatchQrLabelId))).ToArray();
                var destinationCode = item.DestinationBatchId.HasValue
                    ? Positions.Single(candidate => candidate.Batch.Id == item.DestinationBatchId).Batch.BatchCode : null;
                return new TransferItemView(item, position.Batch, position.Product, destinationCode, labelViews);
            }).ToArray();
            return new(transfer, _warehouses.Single(item => item.Id == transfer.SourceWarehouseId),
                _warehouses.Single(item => item.Id == transfer.DestinationWarehouseId), itemViews,
                Events.Where(item => item.WarehouseTransferId == transfer.Id).ToArray());
        }
    }
}
