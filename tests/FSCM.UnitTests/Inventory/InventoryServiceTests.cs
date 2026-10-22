using FSCM.Application.Features.Inventory;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Inventory;

public sealed class InventoryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Creating_receipt_keeps_inventory_unchanged_until_approval()
    {
        var fixture = new Fixture();

        var result = await fixture.CreateAsync("receipt-key-0001", 25, new DateOnly(2027, 4, 30));

        Assert.Equal("PN-261013-01", result.ReceiptCode);
        Assert.Equal(GoodsReceiptStatuses.Pending, result.Status);
        Assert.Null(result.Lines.Single().BatchId);
        Assert.Empty(fixture.Store.Balances);
        Assert.Empty(fixture.Store.Transactions);
    }

    [Fact]
    public async Task Approval_creates_business_batch_and_inventory_ledger()
    {
        var fixture = new Fixture();
        var pending = await fixture.CreateAsync("receipt-key-0001", 25, new DateOnly(2027, 4, 30));

        var result = await fixture.ApproveAsync(pending.Id);

        Assert.Equal(GoodsReceiptStatuses.Received, result.Status);
        Assert.Equal("WH-TA-SKU001-30042027", result.Lines.Single().BatchCode);
        Assert.Equal(25, fixture.Store.Balances.Single().PhysicalQuantity);
        Assert.Equal(25, fixture.Store.Balances.Single().AvailableQuantity);
        Assert.Single(fixture.Store.Transactions);
    }

    [Fact]
    public async Task Same_warehouse_sku_and_expiry_merges_into_existing_batch_on_approval()
    {
        var fixture = new Fixture();
        var first = await fixture.CreateAsync("receipt-key-0001", 25, new DateOnly(2027, 4, 30));
        await fixture.ApproveAsync(first.Id);

        var second = await fixture.CreateAsync("receipt-key-0002", 15, new DateOnly(2027, 4, 30));
        await fixture.ApproveAsync(second.Id);

        Assert.Single(fixture.Store.Batches);
        Assert.Equal(40, fixture.Store.Balances.Single().PhysicalQuantity);
        Assert.Equal(2, fixture.Store.Transactions.Count);
    }

    [Fact]
    public async Task Repeated_create_and_approval_are_idempotent_without_adding_stock_twice()
    {
        var fixture = new Fixture();
        var first = await fixture.CreateAsync("receipt-key-0001", 25, new DateOnly(2027, 4, 30));

        var second = await fixture.CreateAsync("receipt-key-0001", 25, new DateOnly(2027, 4, 30));
        await fixture.ApproveAsync(first.Id);
        await fixture.ApproveAsync(first.Id);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(25, fixture.Store.Balances.Single().PhysicalQuantity);
    }

    [Fact]
    public async Task Reusing_idempotency_key_for_changed_payload_is_rejected()
    {
        var fixture = new Fixture();
        await fixture.CreateAsync("receipt-key-0001", 25, new DateOnly(2027, 4, 30));

        var exception = await Assert.ThrowsAsync<InventoryOperationException>(() =>
            fixture.CreateAsync("receipt-key-0001", 30, new DateOnly(2027, 4, 30)));

        Assert.Equal("conflict", exception.Code);
    }

    [Fact]
    public async Task Receipt_retry_revalidates_current_warehouse_scope()
    {
        var fixture = new Fixture();
        await fixture.CreateAsync("receipt-key-0001", 25, new DateOnly(2027, 4, 30));
        fixture.Store.CanAccessWarehouse = false;

        var exception = await Assert.ThrowsAsync<InventoryOperationException>(() =>
            fixture.CreateAsync("receipt-key-0001", 25, new DateOnly(2027, 4, 30)));

        Assert.Equal("forbidden", exception.Code);
        Assert.Single(fixture.Store.Receipts);
    }

    [Theory]
    [InlineData("2.5", 2027, 4, 30)]
    [InlineData("2", 2026, 10, 13)]
    public async Task Receipt_rejects_fractional_cartons_and_nonfuture_expiry(
        string quantity, int year, int month, int day)
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<InventoryOperationException>(() =>
            fixture.CreateAsync("receipt-key-0001", decimal.Parse(quantity), new DateOnly(year, month, day)));

        Assert.Equal("validation_error", exception.Code);
        Assert.Empty(fixture.Store.Receipts);
    }

    [Theory]
    [InlineData(2026, 10, 14, 2027, 4, 30)]
    [InlineData(2027, 4, 30, 2027, 4, 30)]
    public async Task Receipt_rejects_future_or_nonchronological_production_date(
        int productionYear, int productionMonth, int productionDay,
        int expiryYear, int expiryMonth, int expiryDay)
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<InventoryOperationException>(() =>
            fixture.Service.CreateReceiptAsync(fixture.UserId, SystemRoles.WarehouseKeeper,
                fixture.Store.Warehouse.Id, "receipt-key-0001",
                [new ReceiveGoodsLineData(fixture.Store.Product.Id, "LÔ-THỬ-NGHIỆM", 2,
                    new DateOnly(productionYear, productionMonth, productionDay),
                    new DateOnly(expiryYear, expiryMonth, expiryDay))], CancellationToken.None));

        Assert.Equal("validation_error", exception.Code);
        Assert.Empty(fixture.Store.Receipts);
    }

    private sealed class Fixture
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public FakeStore Store { get; } = new();
        public InventoryService Service { get; }

        public Fixture()
        {
            Service = new InventoryService(Store, new FixedTimeProvider(Now));
        }

        public Task<GoodsReceiptResult> CreateAsync(string key, decimal quantity, DateOnly expirationDate) =>
            Service.CreateReceiptAsync(UserId, SystemRoles.WarehouseKeeper, Store.Warehouse.Id, key,
                [new ReceiveGoodsLineData(Store.Product.Id, "LÔ-THỬ-NGHIỆM", quantity,
                    new DateOnly(2026, 10, 1), expirationDate)],
                CancellationToken.None);

        public Task<GoodsReceiptResult> ApproveAsync(Guid receiptId) =>
            Service.ApproveReceiptAsync(receiptId, UserId, SystemRoles.WarehouseKeeper, CancellationToken.None);
    }

    private sealed class FakeStore : IInventoryStore
    {
        private int receiptNumber;
        public Warehouse Warehouse { get; } = new() { Id = Guid.NewGuid(), Code = "WH-TA", Name = "Kho thử nghiệm", IsActive = true };
        public Product Product { get; } = new() { Id = Guid.NewGuid(), Sku = "SKU001", Name = "Sản phẩm thử nghiệm", Status = "Published", NearDateThresholdHours = 168 };
        public List<GoodsReceipt> Receipts { get; } = [];
        public List<GoodsReceiptLine> Lines { get; } = [];
        public List<Batch> Batches { get; } = [];
        public List<InventoryBalance> Balances { get; } = [];
        public List<InventoryTransaction> Transactions { get; } = [];
        public bool CanAccessWarehouse { get; set; } = true;

        public Task<bool> CanAccessWarehouseAsync(Guid userId, string role, Guid warehouseId, CancellationToken cancellationToken) =>
            Task.FromResult(CanAccessWarehouse);
        public Task<Warehouse?> FindWarehouseAsync(Guid warehouseId, CancellationToken cancellationToken) => Task.FromResult<Warehouse?>(Warehouse.Id == warehouseId ? Warehouse : null);
        public Task<Product?> FindProductAsync(Guid productId, CancellationToken cancellationToken) => Task.FromResult<Product?>(Product.Id == productId ? Product : null);
        public Task<GoodsReceiptView?> FindReceiptByIdempotencyKeyAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult(ToReceiptView(Receipts.SingleOrDefault(item => item.IdempotencyKey == key)));
        public Task<GoodsReceiptView?> FindReceiptAsync(Guid receiptId, CancellationToken cancellationToken) =>
            Task.FromResult(ToReceiptView(Receipts.SingleOrDefault(item => item.Id == receiptId)));
        public Task<GoodsReceiptApprovalView?> FindReceiptForApprovalAsync(Guid receiptId, CancellationToken cancellationToken)
        {
            var receipt = Receipts.SingleOrDefault(item => item.Id == receiptId);
            return Task.FromResult(receipt is null ? null : new GoodsReceiptApprovalView(receipt, Warehouse,
                Lines.Where(item => item.GoodsReceiptId == receiptId)
                    .Select(item => new GoodsReceiptApprovalLine(item, Product)).ToArray()));
        }
        public Task<IReadOnlyCollection<GoodsReceiptView>> ListReceiptsAsync(Guid actorUserId, string actorRole, Guid? warehouseId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<GoodsReceiptView>>(Receipts.Select(item => ToReceiptView(item)!).ToArray());
        public Task<Batch?> FindBatchForUpdateAsync(Guid warehouseId, Guid productId, DateOnly expirationDate, CancellationToken cancellationToken) =>
            Task.FromResult(Batches.SingleOrDefault(item => item.WarehouseId == warehouseId && item.ProductId == productId && item.ExpirationDate == expirationDate));
        public Task<InventoryBalance?> FindBalanceForUpdateAsync(Guid batchId, CancellationToken cancellationToken) =>
            Task.FromResult(Balances.SingleOrDefault(item => item.BatchId == batchId));
        public Task<IReadOnlyCollection<BatchInventoryView>> ListInventoryAsync(Guid actorUserId, string actorRole, Guid? warehouseId, Guid? productId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<BatchInventoryView>>([]);
        public Task<BatchInventoryView?> FindBatchInventoryAsync(Guid batchId, CancellationToken cancellationToken) => Task.FromResult<BatchInventoryView?>(null);
        public Task<IReadOnlyCollection<InventoryHistoryView>> ListHistoryAsync(Guid actorUserId, string actorRole, Guid? warehouseId, Guid? batchId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<InventoryHistoryView>>([]);
        public Task<ExpirySweepRun?> FindLatestExpirySweepRunAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ExpirySweepRun?>(null);
        public Task<int> CountOutboxMessagesAsync(string eventType, string status, CancellationToken cancellationToken) =>
            Task.FromResult(0);
        public Task<string> NextReceiptCodeAsync(DateOnly businessDate, CancellationToken cancellationToken) =>
            Task.FromResult($"PN-{businessDate:yyMMdd}-{++receiptNumber:00}");
        public void AddReceipt(GoodsReceipt receipt) => Receipts.Add(receipt);
        public void AddReceiptLine(GoodsReceiptLine line) => Lines.Add(line);
        public void AddBatch(Batch batch) => Batches.Add(batch);
        public void AddBalance(InventoryBalance balance) => Balances.Add(balance);
        public void AddTransaction(InventoryTransaction transaction) => Transactions.Add(transaction);
        public void ClearTracking() { }
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private GoodsReceiptView? ToReceiptView(GoodsReceipt? receipt)
        {
            if (receipt is null) return null;
            var lines = Lines.Where(item => item.GoodsReceiptId == receipt.Id).Select(line =>
            {
                var batch = Batches.SingleOrDefault(item => item.WarehouseId == receipt.WarehouseId &&
                    item.ProductId == line.ProductId && item.ExpirationDate == line.ExpirationDate);
                return new GoodsReceiptLineView(line, Product.Sku, Product.Name, batch?.Id, batch?.BatchCode);
            }).ToArray();
            return new GoodsReceiptView(receipt, Warehouse.Code, Warehouse.Name, lines);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
