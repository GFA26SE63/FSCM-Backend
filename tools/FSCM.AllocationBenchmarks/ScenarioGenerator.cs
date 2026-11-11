using FSCM.Application.Features.AllocationRuns;
using FSCM.Domain.Allocation;

namespace FSCM.AllocationBenchmarks;

internal static class ScenarioGenerator
{
    private static Guid Id(int value) { var bytes=new byte[16];BitConverter.GetBytes(value).CopyTo(bytes,0);bytes[15]=83;return new(bytes); }
    public static BenchmarkScenario Create(int seed,int orderCount,int stressBatches)
    {
        if(orderCount is <1 or >10000 || stressBatches is <0 or >2000) throw new ArgumentException("Orders: 1..10000; stress batches: 0..2000.");
        var random=new Random(seed);var at=DateTimeOffset.Parse("2026-11-11T02:00:00Z");
        var weights=new AllocationWeights();var pool=new List<PlannerBatch>();var orders=new List<PlannerOrder>();var routes=new List<BenchmarkRoute>();
        var thresholds=new Dictionary<Guid,int>();
        if(stressBatches>0) {
            var product=Id(1);var warehouse=Id(10);var retailer=Id(100);thresholds[product]=72;
            for(var i=0;i<stressBatches;i++) pool.Add(new(Id(1000+i),$"STRESS-{i:D5}",warehouse,"WH-STRESS",product,at.AddHours(100+i%20),1));
            routes.Add(new(warehouse,retailer,new(5000,at.AddHours(1),DateOnly.FromDateTime(at.AddHours(8).DateTime))));
            orders.Add(new(Id(20000),"STRESS-ORDER",retailer,1,null,[new(Id(30000),product,"SKU-STRESS",1,stressBatches,168,.25m)]));
            return new($"stress-{stressBatches}",at,weights,pool,orders,routes,thresholds,["Synthetic worst case: every eligible batch must be scored and selected. Runtime includes production score-trace creation."]);
        }
        var lives=new[]{168,720,48};var near=new[]{80,300,36};var counter=1000;
        for(var p=0;p<3;p++) {
            var product=Id(p+1);thresholds[product]=near[p];
            for(var w=0;w<6;w++) for(var b=0;b<3;b++) {
                var hours=lives[p]*(new[]{.22,.45,.85}[b]) + random.Next(-3,4);
                pool.Add(new(Id(counter++),$"SKU-{p+1}-WH-{w+1}-B-{b+1}",Id(10+w),$"WH-{w+1}",product,at.AddHours(hours),random.Next(10,36)));
            }
        }
        for(var r=0;r<3;r++) for(var w=0;w<6;w++) {
            var meters=w==5?22000:1500+((w+r*2)%5)*3900;
            var arrival=at.AddHours(1+w*.4+r*.2);
            routes.Add(new(Id(10+w),Id(100+r),w==4&&r==2?null:new(meters,arrival,DateOnly.FromDateTime(arrival.AddHours(7).DateTime))));
        }
        for(var o=0;o<orderCount;o++) {
            var lines=new List<PlannerLine>();var count=random.Next(1,4);
            for(var p=0;p<count;p++) lines.Add(new(Id(30000+o*3+p),Id(p+1),$"SKU-{p+1}",p+1,random.Next(8,31),lives[p],.25m,p==1?240:null));
            orders.Add(new(Id(20000+o),$"BENCH-{o+1:D4}",Id(100+o%3),o+1,new(2026,11,11),lines));
        }
        return new($"synthetic-seed-{seed}",at,weights,pool,orders,routes,thresholds,
            ["Synthetic shared stock with multiple SKUs, retailer routes, shortages and multi-line rollback. Fixed arrival order; no future replenishment or quote-release simulation."]);
    }
}
