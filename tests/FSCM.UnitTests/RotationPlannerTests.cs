using FSCM.AlgoDemo;
using Xunit;

namespace FSCM.UnitTests;

/// <summary>CR-01 – near-expiry rotation with the same weighted scoring as allocation (example of the 11/10 document).</summary>
public class RotationPlannerTests
{
    private readonly FSCM.Modules.Inventory.Domain.Rotation.RotationPlan _p = Scenarios.RotationGoVap();

    [Fact] public void Source_sells_45_so_excess_is_35()
    {
        Assert.Equal(15, _p.RemainingDays);
        Assert.Equal(45, _p.Sellable);
        Assert.Equal(35, _p.Excess);
    }

    [Fact] public void Hard_filter_rejects_far_warehouse_and_warehouse_without_sales()
    {
        Assert.False(_p.Candidates.Single(c => c.Warehouse == "WH-C").Passed);   // 45 km > Dmax_kho 40
        Assert.False(_p.Candidates.Single(c => c.Warehouse == "WH-D").Passed);   // v = 0
        Assert.Equal(66, _p.Candidates.Single(c => c.Warehouse == "WH-TA").ReceivingCapacity);
        Assert.Equal(36, _p.Candidates.Single(c => c.Warehouse == "WH-E").ReceivingCapacity);
    }

    [Fact] public void Scores_match_the_document()
    {
        Assert.Equal(new[] { ("WH-TA", 0.667m), ("WH-E", 0.475m), ("WH-B", 0.273m) }, _p.Scores.Select(s => (s.Warehouse, s.Score)));
        Assert.Equal(0.025m, _p.Scores.Single(s => s.Warehouse == "WH-E").SellThroughSafety); // nearly full after receiving
    }

    [Fact] public void Proposes_30_cases_to_Thoi_An_and_leaves_5_for_discount()
    {
        var a = Assert.Single(_p.Assignments);
        Assert.Equal("WH-TA", a.ToWarehouse);
        Assert.Equal(30, a.Quantity);
        Assert.Equal(5_400_000m, a.SalvageValue);
        Assert.Equal(300_000m, a.TripCost);
        Assert.Equal(5, _p.Remainder);
    }
}
