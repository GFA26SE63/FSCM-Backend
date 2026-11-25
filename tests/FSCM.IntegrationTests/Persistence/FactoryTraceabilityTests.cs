using FSCM.Application.Features.Picking;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using FSCM.Persistence.WarehouseWork;
using Microsoft.EntityFrameworkCore;

namespace FSCM.IntegrationTests.Persistence;

public sealed partial class FactoryTraceabilityTests
{
    [Fact]
    public async Task Accepted_prepaid_quote_waits_for_full_payment_then_uses_exact_allocations_for_PO_and_picking()
    {
        var store = new GateStore("Prepaid", "Accepted", "Pending");
        var service = new FSCM.Application.Features.Orders.PurchaseOrderGateService(store, TimeProvider.System);
        await service.TryCreateAsync(store.Order.Id, default);
        Assert.Null(store.Po);
        Assert.Empty(store.Picking);
        store.Payment.Status = "Paid";
        await service.TryCreateAsync(store.Order.Id, default);
        Assert.NotNull(store.Po);
        Assert.Equal("ConvertedToPo", store.Order.Status);
        var item = Assert.Single(store.Items);
        Assert.Equal(store.Allocation.Id, item.AllocationId);
        Assert.Equal(store.Allocation.BatchId, item.SourceBatchId);
        Assert.Equal(store.Allocation.AllocatedQuantity, item.RequestedQuantity);
        Assert.Equal(store.Allocation.WarehouseId, Assert.Single(store.Picking).WarehouseId);
        await service.TryCreateAsync(store.Order.Id, default);
        Assert.Single(store.Picking);
        Assert.Single(store.Items);
    }

    [Fact]
    public async Task Credit_quote_requires_acceptance_before_PO_but_does_not_require_prepayment()
    {
        var store = new GateStore("Credit", "PendingRetailerAcceptance", "Pending");
        var service = new FSCM.Application.Features.Orders.PurchaseOrderGateService(store, TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TryCreateAsync(store.Order.Id, default));
        Assert.Null(store.Po);
        store.Quote.Status = "Accepted";
        await service.TryCreateAsync(store.Order.Id, default);
        Assert.NotNull(store.Po);
        Assert.Single(store.Picking);
    }

    private sealed class GateStore : FSCM.Application.Features.Orders.IPurchaseOrderGateStore
    {
        public SalesOrder Order { get; } = new() { Id = Guid.NewGuid(), OrderCode = "SO-TEST", Status = "Quoted" };
        public SalesOrderQuote Quote { get; } = new() { Id = Guid.NewGuid(), RevisionNumber = 1, AcceptedByUserId = Guid.NewGuid() };
        public Payment Payment { get; } = new();
        public Allocation Allocation { get; } = new() { Id = Guid.NewGuid(), BatchId = Guid.NewGuid(), WarehouseId = Guid.NewGuid(), AllocatedQuantity = 3 };
        public PurchaseOrder? Po { get; private set; }
        public List<PickingList> Picking { get; } = [];
        public List<PickingItem> Items { get; } = [];
        public GateStore(string method, string quoteStatus, string paymentStatus)
        { Order.PaymentMethod = method; Quote.Status = quoteStatus; Payment.Status = paymentStatus; }
        public Task<FSCM.Application.Features.Orders.PurchaseOrderGateView?> LoadForUpdateAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<FSCM.Application.Features.Orders.PurchaseOrderGateView?>(new(Order, Quote, Payment, Po, [Allocation], new Dictionary<Guid, string> { [Allocation.WarehouseId] = "WH01" }));
        public void AddPurchaseOrder(PurchaseOrder value) => Po = value;
        public void AddPickingList(PickingList value) => Picking.Add(value);
        public void AddPickingItem(PickingItem value) => Items.Add(value);
        public void AddHistory(OrderStatusHistory value) { }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct) => action(ct);
        public void ClearTracking() { }
    }

    [Fact]
    public void QR_inventory_query_translates_current_location_scope_source_mapping_and_sync_filters()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unused;Username=unused").Options);
        var service = new FactoryQrInventoryService(db);
        var sql = service.Query(Guid.NewGuid(), "WarehouseKeeper", Guid.NewGuid(), "cid_%", "Available", "Pending")
            .OrderBy(x => x.CartonCode).Skip(50).Take(50).ToQueryString();
        Assert.Contains("FACTORY_CARTON", sql);
        Assert.Contains("FACTORY_RECEIPT_SYNC", sql);
        Assert.Contains("WAREHOUSE_DOCUMENT", sql);
        Assert.Contains("USER_WAREHOUSE_ASSIGNMENT", sql);
        Assert.Contains("ILIKE", sql);
        Assert.Contains("LIMIT", sql);
        Assert.Contains("LEFT JOIN", sql);
        Assert.Contains("IS NULL", sql);
        var adminSql = service.Query(Guid.NewGuid(), "Admin", null, null, null, "Synced").ToQueryString();
        Assert.DoesNotContain("USER_WAREHOUSE_ASSIGNMENT", adminSql);
        Assert.Contains("IS NOT NULL", adminSql);
        Assert.Throws<FSCM.Application.Features.Inventory.InventoryOperationException>(() =>
            service.Query(Guid.NewGuid(), "Retailer", null, null, null, null));
    }

    [Fact]
    public async Task Current_pallet_count_counts_distinct_pallets_of_available_cartons_and_rejects_other_roles()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unused;Username=unused").Options);
        var service = new FactoryQrInventoryService(db);
        var sql = service.CurrentPalletIds(Guid.NewGuid()).ToQueryString();
        Assert.Contains("DISTINCT", sql);
        Assert.Contains("FACTORY_CARTON", sql);
        Assert.Contains("Available", sql);
        Assert.DoesNotContain("FACTORY_PALLET\"", sql); // pallet status (always Received) is not used
        await Assert.ThrowsAsync<FSCM.Application.Features.Inventory.InventoryOperationException>(() =>
            service.CurrentPalletCountAsync(Guid.NewGuid(), "Retailer", Guid.NewGuid(), default));
    }

    [Fact]
    public async Task QR_inventory_rejects_invalid_pages_before_accessing_database()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unused;Username=unused").Options);
        var service = new FactoryQrInventoryService(db);
        foreach (var (page, size) in new[] { (0, 50), (1, 101), (int.MaxValue, 100) })
            await Assert.ThrowsAsync<FSCM.Application.Features.Inventory.InventoryOperationException>(() =>
                service.ListAsync(Guid.NewGuid(), "Admin", null, null, null, null, page, size, default));
    }

    [Fact]
    public void Factory_database_has_only_factory_tables_and_no_cross_database_foreign_keys()
    {
        using var factory = new TraceabilityDbContext(new DbContextOptionsBuilder<TraceabilityDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unused_trace;Username=unused").Options);
        var entities = factory.Model.GetEntityTypes().ToArray();
        Assert.Equal(new[] { "FACTORY_CARTON", "FACTORY_PALLET", "FACTORY_PRODUCTION_BATCH", "FACTORY_PRODUCT_UNIT" },
            entities.Select(x => x.GetTableName()).Order(StringComparer.Ordinal).ToArray());
        Assert.All(entities.SelectMany(x => x.GetForeignKeys()), fk => Assert.Contains(fk.PrincipalEntityType, entities));
        Assert.DoesNotContain(entities, x => x.ClrType == typeof(Product) || x.ClrType == typeof(Batch) || x.ClrType == typeof(WarehouseDocument));
    }

    [Fact]
    public void Factory_connection_cannot_silently_fall_back_to_warehouse_database()
    {
        const string warehouse = "Host=127.0.0.1;Database=postgres;Username=unused";
        Assert.Throws<InvalidOperationException>(() => TraceabilityDbContext.RequireSeparateDatabase(warehouse, ""));
        Assert.Throws<InvalidOperationException>(() => TraceabilityDbContext.RequireSeparateDatabase(warehouse, warehouse));
        TraceabilityDbContext.RequireSeparateDatabase(warehouse, "Host=127.0.0.1;Database=fscm_traceability;Username=unused");
    }

    [Fact]
    public void Batch_identity_distinguishes_multiple_runs_on_same_line_and_date()
    {
        var day = new DateOnly(2026, 8, 27);
        Assert.Equal("BID-F01-20260827-A-0001", FactoryTraceabilityService.BatchCode("F01", day, "A", 1));
        Assert.NotEqual(FactoryTraceabilityService.BatchCode("F01", day, "A", 1), FactoryTraceabilityService.BatchCode("F01", day, "A", 2));
        Assert.NotEqual(FactoryTraceabilityService.BatchCode("F01", day, "A", 1), FactoryTraceabilityService.BatchCode("F02", day, "A", 1));
    }

    [Fact]
    public void Warehouse_stock_code_preserves_external_batch_and_receipt_date()
    {
        var day = new DateOnly(2026, 8, 28); var expiry = new DateOnly(2027, 2, 28);
        var lot = "BID-F01-20260827-A-0001";
        var code = FactoryTraceabilityService.StockCode("WH01", "SKU001", lot, day, expiry);
        Assert.Equal("WH01-SKU001-BID-F01-20260827-A-0001-20260828-20270228", code);
        Assert.NotEqual(code, FactoryTraceabilityService.StockCode("WH02", "SKU001", lot, day, expiry));
        Assert.NotEqual(code, FactoryTraceabilityService.StockCode("WH01", "SKU001", lot, day.AddDays(1), expiry));
    }

    [Fact]
    public void Vietnam_day_boundary_is_used_for_receiving()
    {
        Assert.Equal(new DateOnly(2026, 11, 14), FactoryTraceabilityService.VietnamDate(new DateTimeOffset(2026, 11, 13, 18, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Invalid_production_dates_lines_and_unbounded_serial_generation_are_rejected()
    {
        var today = new DateOnly(2026, 11, 13);
        var valid = new SimulateProductionRequest("F01", "A", Guid.NewGuid(), today, today.AddMonths(6), 2, 10, 12);
        FactoryTraceabilityService.Validate(valid, today);
        foreach (var data in new[] { valid with { FactoryCode = "F-01" }, valid with { ProductionLine = "E" },
            valid with { ProductionDate = today.AddDays(1) }, valid with { ExpirationDate = today },
            valid with { PalletCount = 0 }, valid with { UnitsPerCarton = 0 },
            valid with { PalletCount = 100, CartonsPerPallet = 100, UnitsPerCarton = 1000 } })
            Assert.Throws<PickingOperationException>(() => FactoryTraceabilityService.Validate(data, today));
    }

    [Fact]
    public void Schema_keeps_factory_identity_unique_and_does_not_merge_different_factory_batches_by_dates()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=127.0.0.1;Database=unused;Username=unused").Options);
        var stock = db.Model.FindEntityType(typeof(Batch))!;
        var legacyIndex = stock.GetIndexes().Single(x => x.Properties.Select(p => p.Name).SequenceEqual(new[] { "WarehouseId", "ProductId", "ProductionDate", "ExpirationDate" }));
        Assert.Equal("\"FactoryProductionBatchId\" IS NULL", legacyIndex.GetFilter());
        Assert.Contains(stock.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(new[] { "WarehouseId", "FactoryProductionBatchId", "WarehouseReceiptDate" }));
        foreach (var type in new[] { typeof(FactoryProductionBatch), typeof(FactoryPallet), typeof(FactoryCarton), typeof(FactoryProductUnit) })
            Assert.Contains(db.Model.FindEntityType(type)!.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(new[] { "Code" }));
        Assert.True(db.Model.FindEntityType(typeof(FactoryPallet))!.FindProperty("Version")!.IsConcurrencyToken);
        // Allocation snapshots must accept the full warehouse management code.
        Assert.Equal(240, db.Model.FindEntityType(typeof(AllocationTraceCandidate))!.FindProperty("BatchCodeSnapshot")!.GetMaxLength());
    }
}
