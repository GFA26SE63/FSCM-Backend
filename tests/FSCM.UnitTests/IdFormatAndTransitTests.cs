using FSCM.AlgoDemo;
using FSCM.Modules.Logistics.Domain.Transit;
using FSCM.SharedKernel.Ids;
using Xunit;

namespace FSCM.UnitTests;

/// <summary>ID rules decided on 06/10/2026 and the shared transit table (CR-02).</summary>
public class IdFormatAndTransitTests
{
    [Fact] public void Ids_follow_the_agreed_formats()
    {
        Assert.Equal("10004288", IdFormat.Retailer(4288));
        Assert.Equal("KB-0012", IdFormat.RetailerRequest(12));
        Assert.Equal("Q12-B", IdFormat.SalesGroup("Q12", 1));
        Assert.Equal("CAT-03", IdFormat.Category(3));
        Assert.Equal("SKU002", IdFormat.Sku(2));
        Assert.Equal("TEM-000201", IdFormat.Label(201));
        Assert.Equal("DH-001030", IdFormat.Order(1030));
        Assert.Equal("OFF-S01-0007", IdFormat.OfflineOrder("S01", 7));
        Assert.Equal("DH-001024B", IdFormat.Picking("DH-001024", 1));
        Assert.Equal("WH-GV-SKU002-12102026", IdFormat.Batch("WH-GV", "SKU002", new DateOnly(2026, 10, 12)));
        Assert.Equal("DC-261013-01", IdFormat.Document(DocumentType.Transfer, new DateOnly(2026, 10, 13), 1));
        Assert.Equal("DQ-261013-02", IdFormat.GiftRequest(new DateOnly(2026, 10, 13), 2));
        Assert.Equal("KM-004", IdFormat.Promotion(4));
        Assert.Equal("LP-001", IdFormat.LoyaltyProgram(1));
        Assert.Equal("QUA-010", IdFormat.Gift(10));
        Assert.Equal("NG-003", IdFormat.Driver(3));
    }

    [Theory]
    [InlineData(3.2, RoutePurpose.WarehouseToRetailer, 1, 300_000)]
    [InlineData(17.4, RoutePurpose.WarehouseToRetailer, 2, 500_000)]
    [InlineData(25, RoutePurpose.WarehouseToWarehouse, 3, 800_000)]
    public void Same_distance_gives_same_transit_for_every_purpose(double km, RoutePurpose purpose, int days, int cost)
    {
        var q = Scenarios.Transit.Quote((decimal)km, purpose);
        Assert.NotNull(q);
        Assert.Equal(days, q!.TransitDays);
        Assert.Equal(cost, q.TripCost);
    }

    [Fact] public void Limits_depend_on_purpose()
    {
        Assert.Null(Scenarios.Transit.Quote(25, RoutePurpose.WarehouseToRetailer));   // retailer > 20 km
        Assert.Null(Scenarios.Transit.Quote(45, RoutePurpose.WarehouseToWarehouse));  // warehouse > 40 km
    }
}
