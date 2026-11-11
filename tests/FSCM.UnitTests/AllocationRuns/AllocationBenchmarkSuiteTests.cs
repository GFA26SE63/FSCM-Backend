using FSCM.Application.Features.AllocationRuns;
using FSCM.Domain.Allocation;

namespace FSCM.UnitTests.AllocationRuns;

public sealed class AllocationBenchmarkSuiteTests
{
    private static readonly DateTimeOffset At=DateTimeOffset.Parse("2026-11-11T00:00:00Z");
    private static readonly Guid Product=Guid.NewGuid(),Retailer=Guid.NewGuid(),Wa=Guid.NewGuid(),Wb=Guid.NewGuid(),Wc=Guid.NewGuid();
    private static PlannerLine Line(decimal quantity,decimal mlor=.25m,Guid? product=null) => new(Guid.NewGuid(),product??Product,"SKU",1,quantity,720,mlor);
    private static PlannerOrder Order(int rank,params PlannerLine[] lines) => new(Guid.NewGuid(),$"ORDER-{rank}",Retailer,rank,null,lines);
    private static PlannerBatch Batch(string code,Guid warehouse,decimal quantity,int hours) => new(Guid.NewGuid(),code,warehouse,code,Product,At.AddHours(hours),quantity);
    private static BenchmarkScenario Scenario(IReadOnlyList<PlannerBatch> pool,IReadOnlyList<PlannerOrder> orders) => new("test",At,new(),pool,orders,
        [new(Wa,Retailer,new(2000,At,new(2026,11,11))),new(Wb,Retailer,new(4000,At,new(2026,11,11))),new(Wc,Retailer,new(6000,At,new(2026,11,11)))],
        new Dictionary<Guid,int> { [Product]=220 },[]);

    [Fact]
    public void Coverage_is_batch_coverage_and_can_split_warehouses_and_starve_the_next_order()
    {
        var fixture=Scenario([Batch("A-OLD",Wa,10,200),Batch("B-OLD",Wa,10,200),Batch("C-FRESH",Wb,15,400),Batch("D-FRESH",Wc,5,400)],
            [Order(1,Line(20)),Order(2,Line(15,.5m))]);
        var results=AllocationBenchmarkSuite.Run(fixture,[new("coverage",new(0,0,1)),new("FEFO",new(),true)],1);
        var greedy=results[0];var fefo=results[1];
        Assert.Equal(1,greedy.FulfilledOrders);Assert.Equal(1,greedy.SplitOrders);Assert.Equal(5,greedy.NearExpiryQuantity);
        Assert.Equal(2,fefo.FulfilledOrders);Assert.Equal(0,fefo.SplitOrders);Assert.Equal(20,fefo.NearExpiryQuantity);
        Assert.Equal(35,fefo.CommittedQuantity);Assert.False(greedy.Pareto);Assert.True(fefo.Pareto);
        Assert.NotNull(greedy.MedianMilliseconds);Assert.NotNull(greedy.ScoreTraceRows);
        Assert.Null(fefo.MedianMilliseconds);Assert.Null(fefo.ScoreTraceRows);
        Assert.Equal(new decimal[]{10,10,15,5},fixture.Pool.Select(b=>b.RemainingQuantity));
    }

    [Fact]
    public void Multi_line_failure_releases_the_pool_and_matches_the_production_planner()
    {
        var fixture=Scenario([Batch("ONLY",Wa,10,200)],
            [Order(1,Line(10),Line(1,product:Guid.NewGuid())),Order(2,Line(10))]);
        var profile=new BenchmarkProfile("weighted",new());
        var actual=Assert.Single(AllocationBenchmarkSuite.Run(fixture,[profile],1));
        var expected=new AllocationPlanner(new()).Plan(fixture.Orders,fixture.Pool,
            (wh,retailer)=>fixture.Routes.Single(r=>r.WarehouseId==wh && r.RetailerId==retailer).Route);
        Assert.Equal(expected.Fulfilled.Count(),actual.FulfilledOrders);
        Assert.Equal(1,actual.FulfilledOrders);Assert.Equal(10,actual.CommittedQuantity);Assert.Equal(11,actual.UnfulfilledDemand);
        Assert.Equal(10,actual.NearExpiryQuantity);Assert.Equal(1,actual.WarehouseLegs);Assert.Equal(2,actual.RouteKilometers);
    }

    [Fact]
    public void A_grid_changes_only_weights_and_rejects_an_invalid_step_or_radius_change()
    {
        var baseline=new AllocationWeights(MaximumDistanceMeters:30000);
        var profiles=AllocationBenchmarkSuite.Profiles(baseline,10);
        Assert.All(profiles,p=> { Assert.Equal(1,p.Weights.ShelfLife+p.Weights.Distance+p.Weights.Coverage);Assert.Equal(30000,p.Weights.MaximumDistanceMeters); });
        Assert.Contains(profiles,p=>p.Weights.ShelfLife==1 && p.Weights.Distance==0 && p.Weights.Coverage==0);
        Assert.Contains(profiles,p=>p.Weights.ShelfLife==0 && p.Weights.Distance==0 && p.Weights.Coverage==1);
        Assert.Throws<ArgumentException>(()=>AllocationBenchmarkSuite.Profiles(baseline,7));
        Assert.Throws<ArgumentException>(()=>AllocationBenchmarkSuite.Run(Scenario([Batch("B",Wa,10,200)],[Order(1,Line(1))]),[new("wrong-radius",baseline)],1));
    }
}
