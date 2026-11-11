using System.Diagnostics;
using FSCM.Domain.Allocation;

namespace FSCM.Application.Features.AllocationRuns;

public sealed record BenchmarkRoute(Guid WarehouseId, Guid RetailerId, PlannerRoute? Route);
public sealed record BenchmarkScenario(string Name, DateTimeOffset AsOf, AllocationWeights PublishedWeights,
    IReadOnlyList<PlannerBatch> Pool, IReadOnlyList<PlannerOrder> Orders, IReadOnlyList<BenchmarkRoute> Routes,
    IReadOnlyDictionary<Guid, int> NearExpiryHours, IReadOnlyList<string> Notes);
public sealed record BenchmarkProfile(string Name, AllocationWeights Weights, bool StrictFefo = false);
public sealed record BenchmarkMetrics(string Profile, AllocationWeights Weights, bool StrictFefo,
    int TotalOrders, int FulfilledOrders, decimal CommittedQuantity, decimal UnfulfilledDemand,
    decimal NearExpiryQuantity, int WarehouseLegs, int SplitOrders, decimal RouteKilometers,
    decimal CartonKilometers, decimal AverageLifePercent, long? ScoreTraceRows,
    double? MedianMilliseconds = null, double? P95Milliseconds = null, long? MedianAllocatedBytes = null,
    bool Pareto = false);

/// <summary>
/// Deterministic, sequential replay. Every profile receives an independent copy of the same stock.
/// Failed orders release all tentative draws. This does not replay DB contention, pricing or truck routing.
/// </summary>
public static class AllocationBenchmarkSuite
{
    public static IReadOnlyList<BenchmarkMetrics> Run(BenchmarkScenario scenario,
        IReadOnlyList<BenchmarkProfile> profiles, int repetitions = 5)
    {
        if (repetitions is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(repetitions));
        if (scenario.Orders.Count == 0 || scenario.Orders.Any(o => o.Lines.Count == 0))
            throw new ArgumentException("A benchmark needs non-empty orders.");
        if (scenario.Pool.Any(b => b.RemainingQuantity < 0) || scenario.Pool.Select(b => b.BatchId).Distinct().Count() != scenario.Pool.Count)
            throw new ArgumentException("Pool quantities must be non-negative and batch IDs unique.");
        if (scenario.Orders.Select(o => o.RunOrderId).Distinct().Count() != scenario.Orders.Count ||
            scenario.Orders.SelectMany(o => o.Lines).Select(l => l.RunLineId).Distinct().Count() != scenario.Orders.Sum(o => o.Lines.Count))
            throw new ArgumentException("Order and line IDs must be unique.");
        var metrics = new List<BenchmarkMetrics>();
        foreach (var profile in profiles)
        {
            _ = new AllocationEngine(profile.Weights);
            if (profile.Weights.MaximumDistanceMeters != scenario.PublishedWeights.MaximumDistanceMeters)
                throw new ArgumentException("Keep the service radius fixed when comparing weights.");
            var warmup = Play(scenario, profile);
            // The FEFO comparison helper shares production filtering by invoking the weighted
            // engine. Its extra work must not be presented as the runtime of a pure FEFO algorithm.
            if (profile.StrictFefo) { metrics.Add(warmup); continue; }
            var times = new List<double>(); var bytes = new List<long>();
            for (var i = 0; i < repetitions; i++)
            {
                var allocated = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
                var result = Play(scenario, profile);
                watch.Stop(); bytes.Add(GC.GetAllocatedBytesForCurrentThread() - allocated); times.Add(watch.Elapsed.TotalMilliseconds);
                if (result != warmup) throw new InvalidOperationException("Non-deterministic allocation replay.");
            }
            times.Sort(); bytes.Sort();
            metrics.Add(warmup with { MedianMilliseconds = times[times.Count / 2],
                P95Milliseconds = times[(int)Math.Ceiling(times.Count * .95) - 1], MedianAllocatedBytes = bytes[bytes.Count / 2] });
        }
        var bestService = metrics.Max(row => row.FulfilledOrders);
        return metrics.Select(row => row with { Pareto = row.FulfilledOrders == bestService &&
            !metrics.Where(other => other.FulfilledOrders == bestService).Any(other => Dominates(other, row)) }).ToArray();
    }

    public static IReadOnlyList<BenchmarkProfile> Profiles(AllocationWeights published, int? gridStep = null)
    {
        var profiles = new List<BenchmarkProfile> {
            new("published", published),
            new("expiry-first", published with { ShelfLife = .7m, Distance = .2m, Coverage = .1m }),
            new("balanced", published with { ShelfLife = .5m, Distance = .3m, Coverage = .2m }),
            new("distance-first", published with { ShelfLife = .3m, Distance = .5m, Coverage = .2m }),
            new("coverage-first", published with { ShelfLife = .3m, Distance = .2m, Coverage = .5m }),
            new("strict-FEFO", published, true)
        };
        if (gridStep is { } step)
        {
            if (step <= 0 || step > 100 || 100 % step != 0) throw new ArgumentException("Grid step must divide 100 (for example 5, 10 or 20).");
            for (var expiry = 0; expiry <= 100; expiry += step)
                for (var distance = 0; distance <= 100 - expiry; distance += step)
                {
                    var coverage = 100 - expiry - distance;
                    var weights = published with { ShelfLife = expiry / 100m, Distance = distance / 100m, Coverage = coverage / 100m };
                    if (!profiles.Any(p => !p.StrictFefo && p.Weights == weights))
                        profiles.Add(new($"grid-{expiry:D3}-{distance:D3}-{coverage:D3}", weights));
                }
        }
        return profiles;
    }

    private static BenchmarkMetrics Play(BenchmarkScenario scenario, BenchmarkProfile profile)
    {
        var engine = new AllocationEngine(profile.Weights);
        var remaining = scenario.Pool.ToDictionary(b => b.BatchId, b => b.RemainingQuantity);
        var batches = scenario.Pool.GroupBy(b => b.ProductId).ToDictionary(g => g.Key, g => g.ToArray());
        var routes = scenario.Routes.ToDictionary(r => (r.WarehouseId, r.RetailerId), r => r.Route);
        var fulfilled = 0; var legs = 0; var splits = 0; long traceRows = 0;
        decimal quantity = 0, near = 0, kilometers = 0, cartonKm = 0, life = 0;
        foreach (var order in scenario.Orders.OrderBy(o => o.PriorityRank))
        {
            var tentative = new List<(PlannerLine Line, AllocationDecision Decision)>();
            var full = true;
            foreach (var line in order.Lines.OrderBy(l => l.LineNumber))
            {
                var request = new AllocationRequest(line.Sku, line.OrderedQuantity, line.TotalShelfLifeHours,
                    line.EffectiveMlorRatio, order.DeliveryDeadline, line.MinimumLifeHours);
                var candidates = (batches.GetValueOrDefault(line.ProductId) ?? []).Select(b => {
                    var route = routes.GetValueOrDefault((b.WarehouseId, order.RetailerId));
                    return new BatchCandidate(b.BatchId,b.BatchCode,b.WarehouseId,b.WarehouseCode,b.ExpiresAt,
                        remaining[b.BatchId],route?.DistanceMeters ?? 0,route?.EstimatedArrivalAt,route?.EstimatedArrivalDate);
                }).ToArray();
                IReadOnlyList<AllocationDecision> decisions;
                if (profile.StrictFefo)
                {
                    var result = AllocationBenchmark.Compare(request,candidates,profile.Weights,
                        scenario.NearExpiryHours.GetValueOrDefault(line.ProductId));
                    full = result.FefoFullyAllocated; decisions = result.FefoAllocations;
                }
                else
                {
                    var result = engine.Allocate(request,candidates);
                    full = result.IsFullyAllocated; decisions = result.Allocations; traceRows += result.ScoreTrace.Count;
                }
                if (!full) break;
                foreach (var decision in decisions) { remaining[decision.BatchId] -= decision.Quantity; tentative.Add((line,decision)); }
            }
            if (!full) { foreach (var row in tentative) remaining[row.Decision.BatchId] += row.Decision.Quantity; continue; }
            fulfilled++;
            var orderLegs = tentative.GroupBy(r => r.Decision.WarehouseId).ToArray();
            legs += orderLegs.Length; if (orderLegs.Length > 1) splits++;
            kilometers += orderLegs.Sum(g => g.First().Decision.DistanceMeters) / 1000m;
            foreach (var (line,decision) in tentative)
            {
                quantity += decision.Quantity; cartonKm += decision.Quantity * decision.DistanceMeters / 1000m;
                life += decision.Quantity * Math.Clamp(decision.RemainingShelfLifeHours / line.TotalShelfLifeHours * 100m,0,100);
                if (decision.RemainingShelfLifeHours <= scenario.NearExpiryHours.GetValueOrDefault(line.ProductId)) near += decision.Quantity;
            }
        }
        return new(profile.Name,profile.Weights,profile.StrictFefo,scenario.Orders.Count,fulfilled,quantity,
            scenario.Orders.Sum(o=>o.Lines.Sum(l=>l.OrderedQuantity))-quantity,near,legs,splits,kilometers,
            cartonKm,quantity>0?life/quantity:0,profile.StrictFefo?null:traceRows);
    }

    private static bool Dominates(BenchmarkMetrics a, BenchmarkMetrics b) =>
        a.FulfilledOrders >= b.FulfilledOrders && a.CommittedQuantity >= b.CommittedQuantity &&
        a.NearExpiryQuantity >= b.NearExpiryQuantity && a.WarehouseLegs <= b.WarehouseLegs && a.RouteKilometers <= b.RouteKilometers &&
        (a.FulfilledOrders > b.FulfilledOrders || a.CommittedQuantity > b.CommittedQuantity ||
         a.NearExpiryQuantity > b.NearExpiryQuantity || a.WarehouseLegs < b.WarehouseLegs || a.RouteKilometers < b.RouteKilometers);
}
