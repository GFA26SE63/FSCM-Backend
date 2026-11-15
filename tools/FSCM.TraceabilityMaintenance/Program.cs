using System.Data;
using System.Text.Json;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using FSCM.Persistence.WarehouseWork;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var command = args.FirstOrDefault() ?? "verify";
if (command is not ("migrate" or "copy-legacy" or "sync" or "verify")) throw new ArgumentException("Use migrate, copy-legacy, sync or verify.");
var configPath = args.Length > 1 ? args[1] : "src/FSCM.API/appsettings.json";
using var config = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
string Connection(string name) => Environment.GetEnvironmentVariable("ConnectionStrings__" + name)
    ?? config.RootElement.GetProperty("ConnectionStrings").GetProperty(name).GetString()
    ?? throw new InvalidOperationException($"Missing ConnectionStrings:{name}.");
var warehouseConnection = Connection("DefaultConnection");
var factoryConnection = Connection("TraceabilityConnection");
TraceabilityDbContext.RequireSeparateDatabase(warehouseConnection, factoryConnection);
Console.WriteLine($"WMS database: {new NpgsqlConnectionStringBuilder(warehouseConnection).Database}");
Console.WriteLine($"Factory database: {new NpgsqlConnectionStringBuilder(factoryConnection).Database}");
await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(warehouseConnection).Options);
await using var factory = new TraceabilityDbContext(new DbContextOptionsBuilder<TraceabilityDbContext>().UseNpgsql(factoryConnection).Options);
if (command == "migrate")
{
    // Each context has its own migration history and transaction, entirely in its owning database.
    await db.Database.MigrateAsync();
    Console.WriteLine("WMS MIGRATION OK");
    await factory.Database.MigrateAsync();
    Console.WriteLine("TRACEABILITY MIGRATION OK");
}
else if (command == "copy-legacy")
{
    // Preserve identities and WMS history; copy only, never delete old tables/data.
    await using var transaction = await factory.Database.BeginTransactionAsync(IsolationLevel.Serializable);
    var batches = await db.Set<FactoryProductionBatch>().AsNoTracking().ToArrayAsync();
    var productIds = batches.Select(x => x.ProductId).Distinct().ToArray();
    var products = await db.ProductRecords.AsNoTracking().Where(x => productIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
    var existingBatches = await factory.Set<FactoryProductionBatch>().AsNoTracking().ToDictionaryAsync(x => x.Id);
    foreach (var batch in batches)
    {
        if (existingBatches.TryGetValue(batch.Id, out var existing))
        {
            if (existing.Code != batch.Code || existing.ProductId != batch.ProductId || existing.ProductionDate != batch.ProductionDate || existing.ExpirationDate != batch.ExpirationDate || existing.UnitsPerCarton != batch.UnitsPerCarton)
                throw new InvalidOperationException($"Conflicting factory identity {batch.Id}; no changes committed.");
            continue;
        }
        batch.Sku = products[batch.ProductId].Sku;
        batch.ProductName = products[batch.ProductId].Name;
        factory.Add(batch);
    }
    var pallets = await factory.Set<FactoryPallet>().AsNoTracking().ToDictionaryAsync(x => x.Id);
    foreach (var pallet in await db.Set<FactoryPallet>().AsNoTracking().ToArrayAsync())
    {
        if (pallets.TryGetValue(pallet.Id, out var existing)) {
            if (existing.Code != pallet.Code || existing.ProductionBatchId != pallet.ProductionBatchId)
                throw new InvalidOperationException($"Conflicting pallet identity {pallet.Id}; no changes committed.");
        } else factory.Add(pallet);
    }
    var cartons = await factory.Set<FactoryCarton>().AsNoTracking().ToDictionaryAsync(x => x.Id);
    foreach (var carton in await db.Set<FactoryCarton>().AsNoTracking().ToArrayAsync())
    {
        if (cartons.TryGetValue(carton.Id, out var existing)) {
            if (existing.Code != carton.Code || existing.PalletId != carton.PalletId)
                throw new InvalidOperationException($"Conflicting carton identity {carton.Id}; no changes committed.");
        } else factory.Add(carton);
    }
    var units = await factory.Set<FactoryProductUnit>().AsNoTracking().ToDictionaryAsync(x => x.Id);
    foreach (var unit in await db.Set<FactoryProductUnit>().AsNoTracking().ToArrayAsync())
    {
        if (units.TryGetValue(unit.Id, out var existing)) {
            if (existing.Code != unit.Code || existing.CartonId != unit.CartonId)
                throw new InvalidOperationException($"Conflicting product serial {unit.Id}; no changes committed.");
        } else factory.Add(unit);
    }
    var inserted = await factory.SaveChangesAsync();
    await transaction.CommitAsync();
    Console.WriteLine($"LEGACY COPY OK: {inserted} records inserted. Existing WMS records and all IDs retained.");
}
else if (command == "sync")
{
    using var http = new HttpClient { BaseAddress = new Uri(Environment.GetEnvironmentVariable("Traceability__BaseUrl")
        ?? config.RootElement.GetProperty("Traceability").GetProperty("BaseUrl").GetString()!), Timeout = TimeSpan.FromSeconds(20) };
    http.DefaultRequestHeaders.Add("X-Traceability-Key", Environment.GetEnvironmentVariable("Traceability__ApiKey")
        ?? config.RootElement.GetProperty("Traceability").GetProperty("ApiKey").GetString());
    var service = new FactoryTraceabilityService(db, new FactoryHttpGateway(http));
    var processed = await new FactoryReceiptSyncDispatcher(db, service, TimeProvider.System).DispatchAsync(default);
    Console.WriteLine($"Receipt acknowledgements delivered: {processed}");
    if (await db.Set<FactoryReceiptSync>().AnyAsync(x => x.SyncedAt == null))
        throw new InvalidOperationException("Receipt acknowledgements remain pending. Stock was not changed; retry sync.");
}
else
{
    if (!await db.Database.CanConnectAsync() || !await factory.Database.CanConnectAsync()) throw new InvalidOperationException("One of the databases cannot be reached.");
    Console.WriteLine($"Factory batches={await factory.Set<FactoryProductionBatch>().CountAsync()}, pallets={await factory.Set<FactoryPallet>().CountAsync()}, cartons={await factory.Set<FactoryCarton>().CountAsync()}, units={await factory.Set<FactoryProductUnit>().CountAsync()}");
    Console.WriteLine($"WMS receipt sync pending={await db.Set<FactoryReceiptSync>().CountAsync(x => x.SyncedAt == null)}");
    Console.WriteLine("BOTH DATABASES OK");
}
