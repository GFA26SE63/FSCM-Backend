using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.Reports;

namespace FSCM.UnitTests.Reports;

public sealed class WarehouseLogisticsReportBuilderTests
{
    private static readonly DateOnly Today = new(2026, 11, 20);
    private static readonly Guid RegionId = Guid.NewGuid();
    private static readonly Guid WarehouseA = Guid.NewGuid();
    private static readonly Guid WarehouseB = Guid.NewGuid();
    private static readonly Guid MilkId = Guid.NewGuid();
    private static readonly Guid YogurtId = Guid.NewGuid();
    private static readonly Guid JuiceId = Guid.NewGuid();

    private static readonly WarehouseLogisticsWarehouseRow[] Warehouses =
    [
        new(WarehouseB, "WH-B", "Kho B", RegionId, "Miền Nam", 1, 2),
        new(WarehouseA, "WH-A", "Kho A", RegionId, "Miền Nam", 3, 4),
    ];

    private static readonly WarehouseLogisticsProductRow[] Products =
    [
        new(MilkId, "MILK", "Sữa", 72),
        new(YogurtId, "YOG", "Sữa chua", 48),
        new(JuiceId, "JUICE", "Nước ép", 24),
    ];

    private static WarehouseLogisticsReportQuery Query(int top = 10) => new(Today.AddDays(-30), Today, null, top);

    [Fact]
    public void Ranks_best_sellers_by_sales_issues_across_all_warehouses_and_honours_top()
    {
        WarehouseLogisticsMovementRow[] moves =
        [
            new(WarehouseA, MilkId, InventoryTransactionTypes.SalesIssue, -10),
            new(WarehouseB, MilkId, InventoryTransactionTypes.SalesIssue, -15),
            new(WarehouseA, YogurtId, InventoryTransactionTypes.SalesIssue, -30),
            new(WarehouseA, JuiceId, InventoryTransactionTypes.PurchaseReceipt, 100),
        ];

        var report = WarehouseLogisticsReportBuilder.Build(Query(top: 1), Today, Warehouses, Products, [], moves, []);

        var only = Assert.Single(report.TopProducts);
        Assert.Equal("YOG", only.Sku);
        Assert.Equal(30, only.SoldQuantity);
        Assert.Equal(1, only.Rank);

        var full = WarehouseLogisticsReportBuilder.Build(Query(), Today, Warehouses, Products, [], moves, []);
        // Juice was only received: no sales and no stock, so it is not a best seller.
        Assert.Equal(["YOG", "MILK"], full.TopProducts.Select(p => p.Sku));
        Assert.Equal(["WH-A", "WH-B"], full.Warehouses.Select(w => w.Code));
        Assert.All(full.Warehouses, w => Assert.Equal(["YOG", "MILK"], w.Products.Select(p => p.Sku)));
    }

    [Fact]
    public void Splits_current_stock_into_locked_then_valid_near_and_expired()
    {
        WarehouseLogisticsMovementRow[] moves = [new(WarehouseA, MilkId, InventoryTransactionTypes.SalesIssue, -1)];
        WarehouseLogisticsBatchRow[] batches =
        [
            new(WarehouseA, MilkId, Today.AddDays(30), 50, 5),  // valid, 5 locked
            new(WarehouseA, MilkId, Today.AddDays(2), 20, 0),   // 72h threshold => near expiry
            new(WarehouseA, MilkId, Today.AddDays(-1), 8, 20),  // expired, lock larger than on-hand is capped
            new(WarehouseB, MilkId, Today.AddDays(30), 7, 0),
        ];

        var report = WarehouseLogisticsReportBuilder.Build(Query(), Today, Warehouses, Products, batches, moves, []);

        var a = report.Warehouses.Single(w => w.Id == WarehouseA).Products.Single();
        Assert.Equal(78, a.OnHand);
        Assert.Equal(45, a.Valid);
        Assert.Equal(20, a.NearExpiry);
        Assert.Equal(0, a.Expired);
        Assert.Equal(13, a.Locked);
        Assert.Equal(a.OnHand, a.Valid + a.NearExpiry + a.Expired + a.Locked);

        var b = report.Warehouses.Single(w => w.Id == WarehouseB).Products.Single();
        Assert.Equal(7, b.Valid);
        Assert.Equal(3, report.Warehouses.Single(w => w.Id == WarehouseA).DriverCount);
        Assert.Equal(4, report.Warehouses.Single(w => w.Id == WarehouseA).VehicleCount);
    }

    [Fact]
    public void Reports_period_movements_as_positive_quantities_per_kind()
    {
        WarehouseLogisticsMovementRow[] moves =
        [
            new(WarehouseA, MilkId, InventoryTransactionTypes.SalesIssue, -12),
            new(WarehouseA, MilkId, InventoryTransactionTypes.PurchaseReceipt, 40),
            new(WarehouseA, MilkId, InventoryTransactionTypes.TransferReceipt, 6),
            new(WarehouseA, MilkId, InventoryTransactionTypes.TransferDispatch, -9),
            // A write-off inside the period rows is ignored: "Hủy" comes from the warehouse's whole history.
            new(WarehouseA, MilkId, InventoryTransactionTypes.WriteOff, -99),
        ];
        WarehouseLogisticsMovementRow[] lifetime = [new(WarehouseA, MilkId, InventoryTransactionTypes.WriteOff, -3)];

        var row = WarehouseLogisticsReportBuilder.Build(Query(), Today, Warehouses, Products, [], moves, lifetime)
            .Warehouses.Single(w => w.Id == WarehouseA).Products.Single();

        Assert.Equal(40, row.FactoryReceipt);
        Assert.Equal(6, row.TransferReceipt);
        Assert.Equal(12, row.SalesIssue);
        Assert.Equal(9, row.TransferIssue);
        Assert.Equal(3, row.Disposed);
    }

    [Fact]
    public void Without_movements_in_the_period_best_sellers_and_current_stock_still_show()
    {
        // Nothing moved in the period: ranking falls back to all-time sales, then current stock.
        WarehouseLogisticsMovementRow[] lifetime =
        [
            new(WarehouseA, YogurtId, InventoryTransactionTypes.SalesIssue, -50),
            new(WarehouseA, MilkId, InventoryTransactionTypes.SalesIssue, -20),
            new(WarehouseB, MilkId, InventoryTransactionTypes.WriteOff, -4),
        ];
        WarehouseLogisticsBatchRow[] batches =
        [
            new(WarehouseA, MilkId, Today.AddDays(30), 10, 2),
            new(WarehouseB, JuiceId, Today.AddDays(-2), 6, 0),
        ];

        var report = WarehouseLogisticsReportBuilder.Build(Query(), Today, Warehouses, Products, batches, [], lifetime);

        Assert.Equal(["YOG", "MILK", "JUICE"], report.TopProducts.Select(p => p.Sku));
        Assert.All(report.TopProducts, p => Assert.Equal(0, p.SoldQuantity));
        var milkA = report.Warehouses.Single(w => w.Id == WarehouseA).Products.Single(p => p.Sku == "MILK");
        Assert.Equal((8m, 2m), (milkA.Valid, milkA.Locked));
        Assert.Equal(0, milkA.FactoryReceipt + milkA.TransferReceipt + milkA.SalesIssue + milkA.TransferIssue);
        var b = report.Warehouses.Single(w => w.Id == WarehouseB).Products;
        Assert.Equal(4, b.Single(p => p.Sku == "MILK").Disposed);
        Assert.Equal(6, b.Single(p => p.Sku == "JUICE").Expired);
    }

    [Fact]
    public void Rejects_more_than_50_selected_warehouses()
    {
        WarehouseLogisticsReportBuilder.Validate(Query() with { WarehouseIds = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToArray() });
        Assert.Throws<ReportOperationException>(() =>
            WarehouseLogisticsReportBuilder.Validate(Query() with { WarehouseIds = Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToArray() }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void Rejects_top_outside_range(int top) =>
        Assert.Throws<ReportOperationException>(() => WarehouseLogisticsReportBuilder.Validate(Query(top)));

    [Fact]
    public void Rejects_inverted_or_too_long_period()
    {
        Assert.Throws<ReportOperationException>(() =>
            WarehouseLogisticsReportBuilder.Validate(new(Today, Today.AddDays(-1))));
        Assert.Throws<ReportOperationException>(() =>
            WarehouseLogisticsReportBuilder.Validate(new(Today.AddDays(-367), Today)));
        WarehouseLogisticsReportBuilder.Validate(new(Today.AddDays(-366), Today));
    }
}
