using System.Text.Json;
using FSCM.Application;
using FSCM.Application.Features.AllocationRuns;
using FSCM.Application.Features.DeliveryEta;
using FSCM.Application.Features.Orders;
using FSCM.Persistence;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.AllocationBenchmarks;

internal static class SnapshotExporter
{
    public static async Task<BenchmarkScenario> Read(string configurationFile,string retailerCode,string sku,decimal quantity)
    {
        if(quantity<=0) throw new ArgumentException("Quantity must be positive.");
        var connection=Environment.GetEnvironmentVariable("FSCM_BENCHMARK_CONNECTION");
        if(string.IsNullOrWhiteSpace(connection)) {
            using var source=JsonDocument.Parse(await File.ReadAllTextAsync(configurationFile));
            connection=source.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString();
        }
        var target=new Npgsql.NpgsqlConnectionStringBuilder(connection);
        target.Options=$"{target.Options} -c default_transaction_read_only=on".Trim();
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["ConnectionStrings:DefaultConnection"]=target.ConnectionString }).Build();
        var services=new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(config);
        services.AddApplicationServices().AddPersistenceServices(config);
        await using var provider=services.BuildServiceProvider();await using var scope=provider.CreateAsyncScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var store=scope.ServiceProvider.GetRequiredService<IOrderStore>();
        var retailer=await db.RetailerRecords.AsNoTracking().SingleOrDefaultAsync(r=>r.Code==retailerCode)
            ?? throw new ArgumentException("Retailer code was not found.");
        if(retailer.Status!="Active") throw new ArgumentException("Choose an active Retailer.");
        var product=await db.ProductRecords.AsNoTracking().SingleOrDefaultAsync(p=>p.Sku==sku && p.Status=="Published")
            ?? throw new ArgumentException("Published SKU was not found.");
        var at=DateTimeOffset.UtcNow;var today=DateOnly.FromDateTime(at.AddHours(7).DateTime);
        var mlor=await store.ResolveMlorAsync(retailer.Id,product.Id,at,default)??throw new ArgumentException("MLOR is not configured.");
        // Omit MapService deliberately: an expired/missing route stays unavailable. Snapshotting
        // must not call a paid provider or refresh a cache in the shared business database.
        var policy=new AllocationPolicyService(store,scope.ServiceProvider.GetRequiredService<DeliveryEtaService>());
        var weights=await policy.WeightsAsync(default);
        var positions=await store.ListCandidatesAsync(product.Id,default);
        var inventory=positions.Select(p=>new RealTimeInventoryCandidate(p.Batch,p.Balance,p.Warehouse,product,p.WarehouseLatitude,p.WarehouseLongitude,p.ActiveLockedQuantity)).ToArray();
        var candidates=await policy.CandidatesAsync(retailer.Id,inventory,at,default);
        var pool=inventory.Select(p=>new PlannerBatch(p.Batch.Id,p.Batch.BatchCode,p.Warehouse.Id,p.Warehouse.Code,product.Id,
            FSCM.Domain.Allocation.BatchExpiration.ExclusiveEnd(p.Batch.ExpirationDate),p.IssuableQuantity)).ToArray();
        var routes=candidates.GroupBy(c=>c.WarehouseId).Select(g=> {
            var c=g.First();return new BenchmarkRoute(c.WarehouseId,retailer.Id,c.EstimatedArrivalAt.HasValue
                ?new(c.DistanceMeters,c.EstimatedArrivalAt.Value,c.EstimatedArrivalDate!.Value):null);
        }).ToArray();
        var notes=new List<string> {
            "Frozen current free stock and cached ETA for one hypothetical NEW order. Existing reservations/locks remain excluded; this is not a replay of historical stock or a new business order.",
            $"{routes.Count(r=>r.Route==null)} warehouse routes are unavailable in this snapshot. Weights cannot make an ineligible route or batch eligible."
        };
        var future=inventory.Count(p=>p.Batch.ProductionDate>today && p.IssuableQuantity>0);
        if(future>0) notes.Add($"Data quality: {future} free batch positions have a future production date. Inspect their receipt/quality hold before interpreting results.");
        return new($"current-{retailer.Code}-{product.Sku}",at,weights,pool,
            [new(Guid.NewGuid(),"HYPOTHETICAL-NEW-ORDER",retailer.Id,1,null,
                [new(Guid.NewGuid(),product.Id,product.Sku,1,quantity,product.ShelfLifeHours,mlor.MinimumLifeRatio,mlor.MinimumLifeHours)])],routes,
            new Dictionary<Guid,int> { [product.Id]=product.NearDateThresholdHours },notes);
    }
}
