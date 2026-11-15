using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FSCM.Application.Features.AllocationRuns;
using FSCM.AllocationBenchmarks;

Console.OutputEncoding=Encoding.UTF8;
var json=new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented=true,PropertyNameCaseInsensitive=true };
try
{
    if(args.Length==0 || args.Contains("--help")) {
        Console.WriteLine("""
            Allocation benchmark (run from the Backend repository)
              --generate <scenario.json> [--seed 83] [--orders 60] [--stress-batches 200]
              --snapshot <scenario.json> --retailer <code> --sku <sku> --quantity 25 [--config src/FSCM.API/appsettings.json]
              --run <scenario.json> [--grid-step 10] [--weights 0.5,0.3,0.2] [--repetitions 5] [--output artifacts/allocation-benchmark]
            Snapshot uses a PostgreSQL read-only connection and cached ETA only. Connection can also
            come from FSCM_BENCHMARK_CONNECTION. No orders, reservations, routes or weights are written.
            """);
        return 0;
    }
    var options=new Dictionary<string,string>();
    var supported=new HashSet<string>(["--generate","--seed","--orders","--stress-batches","--snapshot","--retailer","--sku","--quantity","--config","--run","--grid-step","--weights","--repetitions","--output"]);
    for(var i=0;i<args.Length;i+=2) {
        if(!supported.Contains(args[i]) || i+1>=args.Length || !options.TryAdd(args[i],args[i+1])) throw new ArgumentException($"Invalid or repeated option: {args[i]}");
    }
    int Int(string key,int fallback)=>options.TryGetValue(key,out var value)?int.Parse(value,CultureInfo.InvariantCulture):fallback;
    async Task Save(string path,object value) {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path,JsonSerializer.Serialize(value,json),new UTF8Encoding(false));
    }
    if(options.TryGetValue("--generate",out var generate)) {
        var fixture=ScenarioGenerator.Create(Int("--seed",83),Int("--orders",60),Int("--stress-batches",0));
        await Save(generate,fixture);Console.WriteLine($"Scenario: {Path.GetFullPath(generate)}");
    }
    if(options.TryGetValue("--snapshot",out var snapshot)) {
        string Required(string key)=>options.GetValueOrDefault(key)??throw new ArgumentException($"{key} is required for a DB snapshot.");
        var fixture=await SnapshotExporter.Read(options.GetValueOrDefault("--config","src/FSCM.API/appsettings.json"),
            Required("--retailer"),Required("--sku"),decimal.Parse(Required("--quantity"),CultureInfo.InvariantCulture));
        await Save(snapshot,fixture);Console.WriteLine($"Read-only snapshot: {Path.GetFullPath(snapshot)}");
        foreach(var note in fixture.Notes) Console.WriteLine(note);
    }
    if(!options.TryGetValue("--run",out var input)) {
        if(!options.ContainsKey("--generate") && !options.ContainsKey("--snapshot")) throw new ArgumentException("Choose --generate, --snapshot or --run.");
        return 0;
    }
    var contents=await File.ReadAllBytesAsync(input);
    var scenario=JsonSerializer.Deserialize<BenchmarkScenario>(contents,json)??throw new ArgumentException("Empty scenario.");
    var profiles=AllocationBenchmarkSuite.Profiles(scenario.PublishedWeights,
        options.ContainsKey("--grid-step")?Int("--grid-step",10):null).ToList();
    if(options.TryGetValue("--weights",out var custom)) {
        var values=custom.Split(',').Select(v=>decimal.Parse(v,CultureInfo.InvariantCulture)).ToArray();
        if(values.Length!=3) throw new ArgumentException("Weights must be expiry,distance,coverage.");
        profiles.Add(new("custom",scenario.PublishedWeights with { ShelfLife=values[0],Distance=values[1],Coverage=values[2] }));
    }
    var repetitions=Int("--repetitions",5);
    var results=AllocationBenchmarkSuite.Run(scenario,profiles,repetitions);
    var output=Path.GetFullPath(options.GetValueOrDefault("--output","artifacts/allocation-benchmark"));
    Directory.CreateDirectory(output);
    await Save(Path.Combine(output,"results.json"),new { scenario.Name,scenario.AsOf,InputSha256=Convert.ToHexString(SHA256.HashData(contents)),
        Framework=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,Environment.ProcessorCount,Repetitions=repetitions,
        Timing="Warm in-memory sequential planning + audit/metrics; excludes database, ETA/network, pricing and concurrent transactions. FEFO timing is intentionally unavailable.",
        MetricMeaning="Near-expiry quantities use SKU hours at arrival. Route km sums one one-way warehouse-to-retailer leg per served order/warehouse; it is not vehicle routing or delivery cost. Pareto first requires the maximum fulfilled-order count, then compares committed quantity, near-expiry quantity, legs and km; it does not choose production weights.",
        scenario.Notes,Results=results });
    var csv=new List<string> { "profile,expiry,distance,coverage,fulfilled,totalOrders,committedCartons,unfulfilledCartons,nearExpiryCartons,warehouseLegs,splitOrders,routeKm,cartonKm,averageLifePercent,scoreTraceRows,medianMs,p95Ms,medianAllocatedBytes,pareto" };
    string Cell(object? value)=>'"'+(value is IFormattable formattable?formattable.ToString(null,CultureInfo.InvariantCulture):value?.ToString()??"").Replace("\"","\"\"")+'"';
    foreach(var row in results) csv.Add(string.Join(',',new object?[] { row.Profile,row.Weights.ShelfLife,row.Weights.Distance,row.Weights.Coverage,row.FulfilledOrders,row.TotalOrders,row.CommittedQuantity,row.UnfulfilledDemand,row.NearExpiryQuantity,row.WarehouseLegs,row.SplitOrders,row.RouteKilometers,row.CartonKilometers,row.AverageLifePercent,row.ScoreTraceRows,row.MedianMilliseconds,row.P95Milliseconds,row.MedianAllocatedBytes,row.Pareto }.Select(Cell)));
    await File.WriteAllLinesAsync(Path.Combine(output,"results.csv"),csv,new UTF8Encoding(false));
    Console.WriteLine($"{scenario.Name} | {scenario.Orders.Count} orders | {scenario.Pool.Count} batches | {profiles.Count} profiles");
    Console.WriteLine("profile                  full     near    legs    km       median ms    Pareto");
    foreach(var row in results) Console.WriteLine(FormattableString.Invariant($"{row.Profile,-24} {row.FulfilledOrders,3}/{row.TotalOrders,-3} {row.NearExpiryQuantity,7:0.##} {row.WarehouseLegs,6} {row.RouteKilometers,8:0.##} {(row.MedianMilliseconds?.ToString("0.###",CultureInfo.InvariantCulture)??"n/a"),12}    {(row.Pareto?"yes":"")}"));
    Console.WriteLine($"Results: {output}");return 0;
}
catch(Exception e) when(e is ArgumentException or FormatException or JsonException or InvalidOperationException or IOException or Npgsql.NpgsqlException)
{ Console.Error.WriteLine($"Benchmark failed: {e.Message}");return 1; }
