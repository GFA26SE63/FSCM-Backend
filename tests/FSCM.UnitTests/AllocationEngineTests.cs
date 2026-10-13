using FSCM.AlgoDemo;
using Xunit;

namespace FSCM.UnitTests;

/// <summary>SPEC §5 – weighted allocation, example DH-001030 of the documents.</summary>
public class AllocationEngineTests
{
    private readonly FSCM.Modules.Ordering.Domain.Allocation.LineAllocationResult _r = Scenarios.AllocationDh001030();

    [Fact] public void Expired_and_empty_batches_fail_B1()
    {
        Assert.False(_r.B1.Single(x => x.BatchCode == "WH-TA-SKU001-25092026").Pass);
        Assert.False(_r.B1.Single(x => x.BatchCode == "WH-TCH-SKU001-09112026").Pass);
    }

    [Fact] public void Central_warehouse_beyond_20km_fails_B2a() =>
        Assert.False(_r.B2Distance.Single(x => x.BatchCode == "WH-CC-SKU001-12102026").Pass);

    [Fact] public void Batch_that_arrives_below_MLOR_fails_B2() =>
        Assert.False(_r.B2Eta.Single(x => x.BatchCode == "WH-GV-SKU001-06102026").Pass);   // 7 days left < 13.5

    [Fact] public void B3_ranks_Thoi_An_first()
    {
        Assert.Equal(new[] { "WH-TA-SKU001-18102026", "WH-TCH-SKU001-21102026", "WH-TD-SKU001-18102026" }, _r.B3.Select(x => x.BatchCode));
        Assert.Equal(0.798m, _r.B3[0].Score);
    }

    [Fact] public void Greedy_takes_40_from_TA_then_20_from_TCH()
    {
        Assert.True(_r.Enough);
        Assert.Equal(new[] { ("WH-TA", 40), ("WH-TCH", 20) }, _r.Takes.Select(t => (t.Warehouse, t.Quantity)));
    }
}
