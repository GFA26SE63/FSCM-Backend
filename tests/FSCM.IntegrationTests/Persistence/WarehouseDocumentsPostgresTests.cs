using FSCM.Application;
using System.Net.Http.Json;
using FactoryDbContext = Fscm.Traceability.Data.FactoryDbContext;
using FactoryProductUnit = Fscm.Traceability.Models.FactoryProductUnit;
using FactoryCarton = Fscm.Traceability.Models.FactoryCarton;
using FactoryPallet = Fscm.Traceability.Models.FactoryPallet;
using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.Picking;
using FSCM.Application.Features.QrLabels;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Quotes;
using FSCM.Domain.Authorization;
using FSCM.Infrastructure;
using FSCM.Persistence;
using FSCM.Persistence.DbContexts;
using FSCM.Persistence.Seeding;
using FSCM.Persistence.WarehouseWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.IntegrationTests.Persistence;

public sealed class WarehouseDocumentsPostgresTests
{
    [PostgresWorkflowFact]
    public async Task Cartons_receive_transfer_and_grouped_order_issue_are_atomic_and_traceable()
    {
        var target = new Npgsql.NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("FSCM_TEST_POSTGRES"));
        // Remote runs require the runner to explicitly allow this host AND a fresh test database.
        var allowedRemoteHost = Environment.GetEnvironmentVariable("FSCM_TEST_POSTGRES_ALLOWED_HOST");
        Assert.True(target.Host is "127.0.0.1" or "localhost" ||
            !string.IsNullOrWhiteSpace(allowedRemoteHost) && target.Host == allowedRemoteHost,
            "Remote tests require FSCM_TEST_POSTGRES_ALLOWED_HOST matching the test server.");
        Assert.StartsWith("fscm_e2e_", target.Database);
        Assert.Matches("^fscm_e2e_[a-z0-9_]+$", target.Database!);
        target.Database += "_warehouse";
        var traceTarget = new Npgsql.NpgsqlConnectionStringBuilder(target.ConnectionString) { Database = target.Database + "_trace" };
        foreach (var database in new[] { target, traceTarget })
        {
            Assert.Matches("^fscm_e2e_[a-z0-9_]+$", database.Database!);
            var maintenanceTarget = new Npgsql.NpgsqlConnectionStringBuilder(database.ConnectionString) { Database = "postgres", Pooling = false };
            await using var maintenance = new Npgsql.NpgsqlConnection(maintenanceTarget.ConnectionString);
            await maintenance.OpenAsync();
            await using var exists = new Npgsql.NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @name)", maintenance);
            exists.Parameters.AddWithValue("name", database.Database!);
            Assert.False((bool)(await exists.ExecuteScalarAsync())!, "Workflow tests require fresh databases; existing databases are never reused/deleted.");
            await using var create = new Npgsql.NpgsqlCommand($"CREATE DATABASE \"{database.Database}\"", maintenance) { CommandTimeout = 120 };
            await create.ExecuteNonQueryAsync();
        }
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:DefaultConnection"] = target.ConnectionString,
            ["ConnectionStrings:TraceabilityConnection"] = traceTarget.ConnectionString,
            ["SeedData:DefaultPassword"] = "Local-E2e-Only!2049", ["Jwt:Secret"] = new string('w', 64),
            ["Jwt:Issuer"] = "FSCM.API", ["Jwt:Audience"] = "FSCM.Clients"
        }).Build();
        var services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(config);
        services.AddApplicationServices().AddPersistenceServices(config).AddInfrastructureServices(config);
        // Source service runs in-process through its real HTTP pipeline and its own database.
        IServiceProvider? providerForFactory = null;
        await using var factoryApp = new StandaloneFactoryFixture(traceTarget.ConnectionString, new TestFactoryFscmGateway(() => providerForFactory!));
        var integrationClient = factoryApp.CreateClient();
        await using (var sourceScope = factoryApp.Services.CreateAsyncScope())
            Assert.Equal(traceTarget.Database, sourceScope.ServiceProvider.GetRequiredService<FactoryDbContext>().Database.GetDbConnection().Database);
        integrationClient.DefaultRequestHeaders.Add("X-Traceability-Key", StandaloneFactoryFixture.IntegrationKey);
        services.AddSingleton<IFactoryGateway>(new FactoryHttpGateway(integrationClient));
        await using var provider = services.BuildServiceProvider();
        providerForFactory = provider;
        await using (var scope = provider.CreateAsyncScope())
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.CanConnectAsync(),
                "The freshly created test database must be reachable before migration/seeding.");
            await using var sourceContext = new FactoryDbContext(new DbContextOptionsBuilder<FactoryDbContext>().UseNpgsql(traceTarget.ConnectionString).Options);
            await sourceContext.Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>().SeedAsync();
        }
        async Task<T> Call<T>(Func<WarehouseWorkService, Task<T>> action)
        { await using var scope = provider.CreateAsyncScope(); return await action(scope.ServiceProvider.GetRequiredService<WarehouseWorkService>()); }
        async Task<T> Db<T>(Func<AppDbContext, Task<T>> action)
        { await using var scope = provider.CreateAsyncScope(); return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>()); }
        async Task<T> FactoryDb<T>(Func<FactoryDbContext, Task<T>> action)
        { await using var context = new FactoryDbContext(new DbContextOptionsBuilder<FactoryDbContext>().UseNpgsql(traceTarget.ConnectionString).Options); return await action(context); }
        var keeper = await Db(db => db.UserAccountRecords.Where(u => u.Email == "warehouse.keeper@fscm.local").Select(u => u.Id).SingleAsync());
        var admin = await Db(db => db.UserAccountRecords.Where(u => u.Email == "admin@fscm.local").Select(u => u.Id).SingleAsync());
        var product = await Db(db => db.ProductRecords.Where(p => p.Sku == "SKU001").Select(p => p.Id).SingleAsync());
        var source = await Db(db => db.WarehouseRecords.Where(w => w.Code == "WH-HCM-01").Select(w => w.Id).SingleAsync());
        var destination = await Db(db => db.WarehouseRecords.Where(w => w.Id != source).Select(w => w.Id).FirstAsync());
        using var factoryUi = factoryApp.CreateClient();
        factoryUi.DefaultRequestHeaders.Add("X-Requested-With", "Traceability");
        using var login = await factoryUi.PostAsJsonAsync("/api/session/login", new { identifier = "admin-test", password = "test-only" });
        login.EnsureSuccessStatusCode();
        var factoryClient = new FactoryTestClient(factoryUi, provider);
        Task<T> Factory<T>(Func<FactoryTestClient, Task<T>> action) => action(factoryClient);
        await Db(async db => {
            if (!await db.UserWarehouseAssignmentRecords.AnyAsync(a => a.UserAccountId == keeper && a.WarehouseId == destination && a.IsActive))
            {
                var assignment = await db.UserWarehouseAssignmentRecords.SingleOrDefaultAsync(a => a.UserAccountId == keeper && a.WarehouseId == destination);
                if (assignment is null) db.UserWarehouseAssignmentRecords.Add(new() { Id = Guid.NewGuid(), UserAccountId = keeper,
                    WarehouseId = destination, AssignedAt = DateTimeOffset.UtcNow, IsActive = true });
                else assignment.IsActive = true;
                await db.SaveChangesAsync();
            }
            return 0;
        });
        var today = FactoryTraceabilityService.VietnamDate(DateTimeOffset.UtcNow);
        var productionData = new SimulateProductionRequest("F01", "A", product, today.AddDays(-1), today.AddDays(6), 2, 3, 12);
        var productionId = await Factory(s => s.SimulateAsync(productionData, "production-key", admin, default));
        Assert.Equal(productionId, await Factory(s => s.SimulateAsync(productionData, "production-key", admin, default)));
        var produced = (await Factory(s => s.ListAsync(default))).Single(x => x.Batch.Id == productionId);
        Assert.Equal(6, produced.CartonCount); Assert.Equal(72, produced.UnitCount);
        var batchDetail = await Factory(s => s.DetailAsync(productionId, default));
        Assert.Equal(6, batchDetail.CartonCount); Assert.Equal(72, batchDetail.UnitCount);
        Assert.All(batchDetail.Pallets, p => { Assert.Equal(3, p.CartonCount); Assert.Equal(36, p.UnitCount); });
        var cartonPage1 = await Factory(s => s.MappingAsync(productionId, "cartons", 1, 2));
        var cartonPage2 = await Factory(s => s.MappingAsync(productionId, "cartons", 2, 2));
        Assert.Equal(6, cartonPage1.TotalCount); Assert.Equal(2, cartonPage1.Items.Length);
        Assert.Empty(cartonPage1.Items.Select(x => x.CartonCode).Intersect(cartonPage2.Items.Select(x => x.CartonCode)));
        var cartonForSerials = cartonPage1.Items[0];
        var serialPage = await Factory(s => s.MappingAsync(productionId, "units", 1, 50, cartonForSerials.PalletCode, cartonForSerials.CartonCode));
        Assert.Equal(12, serialPage.TotalCount); Assert.Equal(12, serialPage.Items.Length);
        Assert.All(serialPage.Items, x => { Assert.Equal(cartonForSerials.CartonCode, x.CartonCode); Assert.Equal(produced.Batch.Code, x.BatchCode); Assert.Equal("SKU001", x.Sku); Assert.NotNull(x.UnitCode); });
        Assert.Equal(0, await Db(db => db.BatchRecords.CountAsync(b => b.FactoryProductionBatchId == productionId)));
        var factoryData = new FactoryReceiptRequest(source, Note: "Nhà máy test");
        var receiptId = await Call(s => s.CreateFactoryAsync(factoryData, "factory-key", keeper, SystemRoles.WarehouseKeeper, default));
        Assert.Equal(receiptId, await Call(s => s.CreateFactoryAsync(factoryData, "factory-key", keeper, SystemRoles.WarehouseKeeper, default)));
        var receipt = await Call(s => s.GetAsync(receiptId, keeper, SystemRoles.WarehouseKeeper, default));
        Assert.Empty(receipt.Cartons); Assert.Empty(receipt.Lines);
        await Assert.ThrowsAsync<PickingOperationException>(() => Call(s => s.PostAsync(receiptId, receipt.Document.Version, keeper, SystemRoles.WarehouseKeeper, default)));
        await Assert.ThrowsAsync<PickingOperationException>(() => Call(s => s.ScanAsync(receiptId, "WRONG", receipt.Document.Version, keeper, SystemRoles.WarehouseKeeper, default)));
        await Assert.ThrowsAsync<PickingOperationException>(() => Call(s => s.ScanAsync(receiptId, produced.Pallets[0].Code, receipt.Document.Version, keeper, SystemRoles.WarehouseKeeper, default)));
        await Factory(s => s.DispatchAsync(productionId, source, default));
        var pendingFactory = await Call(s => s.PendingFactoryAsync(keeper, SystemRoles.WarehouseKeeper, default));
        Assert.Equal(2, pendingFactory.Count(p => produced.Pallets.Any(x => x.Id == p.PalletId)));
        Assert.All(pendingFactory, p => Assert.Equal(source, p.DestinationWarehouseId));
        Assert.Empty(await Call(s => s.PendingFactoryAsync(Guid.NewGuid(), SystemRoles.WarehouseKeeper, default)));
        var prepareData = new PrepareFactoryReceiptRequest(produced.Pallets[0].Code);
        await Assert.ThrowsAsync<PickingOperationException>(() => Call(s => s.PrepareFactoryAsync(prepareData, Guid.NewGuid(), SystemRoles.WarehouseKeeper, default)));
        var preparedReceipt = await Call(s => s.PrepareFactoryAsync(prepareData, keeper, SystemRoles.WarehouseKeeper, default));
        Assert.Equal(preparedReceipt, await Call(s => s.PrepareFactoryAsync(prepareData, keeper, SystemRoles.WarehouseKeeper, default)));
        var preparedWork = await Call(s => s.GetAsync(preparedReceipt, keeper, SystemRoles.WarehouseKeeper, default));
        Assert.Equal(source, preparedWork.Document.WarehouseId);
        Assert.Empty(preparedWork.Cartons);
        Assert.Equal(0, await Db(db => db.BatchRecords.CountAsync(b => b.FactoryProductionBatchId == productionId)));
        Assert.Equal(preparedReceipt, (await Call(s => s.PendingFactoryAsync(keeper, SystemRoles.WarehouseKeeper, default)))
            .Single(p => p.PalletId == produced.Pallets[0].Id).ReceiptDocumentId);
        await Assert.ThrowsAsync<PickingOperationException>(() => Call(s => s.PreviewPalletAsync(preparedReceipt, produced.Pallets[1].Code, keeper, SystemRoles.WarehouseKeeper, default)));
        await Assert.ThrowsAsync<PickingOperationException>(() => Call(s => s.ScanReceiptPalletAsync(preparedReceipt,
            new(produced.Pallets[1].Code, 1, "Camera"), keeper, SystemRoles.WarehouseKeeper, "Integration phone", default)));
        Assert.Empty(await Call(s => s.ReceiptScansAsync(preparedReceipt, keeper, SystemRoles.WarehouseKeeper, default)));
        await Assert.ThrowsAsync<PickingOperationException>(() => Call(s => s.ScanAsync(preparedReceipt, produced.Pallets[1].Code, 1, keeper, SystemRoles.WarehouseKeeper, default)));
        var wrongWarehouseReceipt = await Call(s => s.CreateFactoryAsync(new(destination), "wrong-wh-receipt", keeper, SystemRoles.WarehouseKeeper, default));
        await Assert.ThrowsAsync<PickingOperationException>(() => Call(s => s.ScanReceiptPalletAsync(wrongWarehouseReceipt,
            new(produced.Pallets[0].Code, 1, "Camera"), keeper, SystemRoles.WarehouseKeeper, "Integration phone", default)));
        await Assert.ThrowsAsync<PickingOperationException>(() => Call(s => s.ScanAsync(wrongWarehouseReceipt, produced.Pallets[0].Code, 1, keeper, SystemRoles.WarehouseKeeper, default)));
        foreach (var pallet in produced.Pallets)
        {
            receipt = await Call(s => s.GetAsync(receiptId, keeper, SystemRoles.WarehouseKeeper, default));
            var scan = await Call(s => s.ScanReceiptPalletAsync(receiptId, new(pallet.Code, receipt.Document.Version, "Camera"), keeper, SystemRoles.WarehouseKeeper, "Integration phone", default));
            var request = new InspectedPalletRequest(pallet.Code, scan.DocumentVersion,
                scan.Manifest!.Cartons.Select(c => new FSCM.Domain.Entities.ReceiptCartonInspection(c.Code, "Accepted", null, null)).ToArray(), scan.Id);
            await Call(s => s.ReceiveInspectedPalletAsync(receiptId, request, keeper, SystemRoles.WarehouseKeeper, default));
            await Call(s => s.ReceiveInspectedPalletAsync(receiptId, request, keeper, SystemRoles.WarehouseKeeper, default));
        }
        receipt = await Call(s => s.GetAsync(receiptId, keeper, SystemRoles.WarehouseKeeper, default));
        Assert.Equal(6, receipt.Cartons.Count);
        // Local receipt is authoritative while the external acknowledgement is still pending.
        Assert.Empty(await Call(s => s.PendingFactoryAsync(keeper, SystemRoles.WarehouseKeeper, default)));
        Assert.Equal(72, await FactoryDb(db => db.Set<FactoryProductUnit>().CountAsync()));
        Assert.Equal(0, await Db(db => db.Set<FSCM.Domain.Entities.FactoryProductUnit>().CountAsync()));
        Assert.Equal(2, await Db(db => db.Set<FSCM.Domain.Entities.FactoryReceiptSync>().CountAsync(x => x.SyncedAt == null)));
        var stockBeforeAck = await Db(db => new FactoryQrInventoryService(db).ListAsync(keeper, SystemRoles.WarehouseKeeper,
            source, produced.Batch.Code.ToLowerInvariant(), null, "Pending", 1, 2, default));
        Assert.Equal(6, stockBeforeAck.TotalCount);
        Assert.Equal(2, stockBeforeAck.Items.Length);
        Assert.All(stockBeforeAck.Items, row => {
            Assert.Equal(produced.Batch.Code, row.ProductionBatchCode);
            Assert.Equal(12, row.UnitsPerCarton);
            Assert.True(row.HasAcknowledgement);
            Assert.Null(row.SyncedAt);
            Assert.Equal(receipt.Document.Code, row.ReceiptCode);
        });
        Assert.Equal(0, (await Db(db => new FactoryQrInventoryService(db).ListAsync(Guid.NewGuid(), SystemRoles.WarehouseKeeper,
            null, null, null, null, 1, 50, default))).TotalCount);
        Assert.All(await FactoryDb(db => db.Set<FactoryPallet>().Where(x => x.ProductionBatchId == productionId).ToArrayAsync()), p => Assert.Equal("Dispatched", p.Status));
        await using (var syncScope = provider.CreateAsyncScope())
        {
            var dispatcher = syncScope.ServiceProvider.GetRequiredService<FactoryReceiptSyncDispatcher>();
            Assert.Equal(2, await dispatcher.DispatchAsync(default));
            Assert.Equal(0, await dispatcher.DispatchAsync(default));
        }
        Assert.All(await FactoryDb(db => db.Set<FactoryPallet>().Where(x => x.ProductionBatchId == productionId).ToArrayAsync()), p => Assert.Equal("Received", p.Status));
        Assert.Equal(6, (await Db(db => new FactoryQrInventoryService(db).ListAsync(admin, SystemRoles.Admin,
            source, produced.Batch.Code, null, "Synced", 1, 50, default))).TotalCount);
        var receivedBatchId = receipt.Lines.Single().BatchId!.Value;
        Assert.Equal(6, await Db(db => db.InventoryBalanceRecords.Where(b => b.BatchId == receivedBatchId).Select(b => b.PhysicalQuantity).SingleAsync()));
        await Call(s => s.PostAsync(receiptId, receipt.Document.Version, keeper, SystemRoles.WarehouseKeeper, default));
        await Call(s => s.PostAsync(receiptId, receipt.Document.Version, keeper, SystemRoles.WarehouseKeeper, default));
        // Another production batch with identical SKU and dates must remain distinct.
        var secondProductionId = await Factory(s => s.SimulateAsync(productionData with { PalletCount = 1, CartonsPerPallet = 1 }, "production-key-2", admin, default));
        var second = (await Factory(s => s.ListAsync(default))).Single(x => x.Batch.Id == secondProductionId);
        Assert.NotEqual(produced.Batch.Code, second.Batch.Code);
        await Factory(s => s.DispatchAsync(secondProductionId, source, default));
        var receipt2 = await Call(s => s.CreateFactoryAsync(new(source), "factory-key-2", keeper, SystemRoles.WarehouseKeeper, default));
        var secondScan = await Call(s => s.ScanReceiptPalletAsync(receipt2, new(second.Pallets[0].Code, 1, "Camera"), keeper, SystemRoles.WarehouseKeeper, "Integration phone", default));
        await Call(s => s.ReceiveInspectedPalletAsync(receipt2, new(second.Pallets[0].Code, secondScan.DocumentVersion,
            secondScan.Manifest!.Cartons.Select(c => new FSCM.Domain.Entities.ReceiptCartonInspection(c.Code, "Accepted", null, null)).ToArray(), secondScan.Id), keeper, SystemRoles.WarehouseKeeper, default));
        Assert.Equal(2, await Db(db => db.BatchRecords.CountAsync(b => b.FactoryProductionBatchId == productionId || b.FactoryProductionBatchId == secondProductionId)));
        var traceUnit = await FactoryDb(db => db.Set<FactoryProductUnit>().Where(u => db.Set<FactoryCarton>()
            .Any(c => c.Id == u.CartonId && db.Set<FactoryPallet>().Any(p => p.Id == c.PalletId && p.ProductionBatchId == productionId))).FirstAsync());
        var trace = await Factory(s => s.TraceAsync(traceUnit.Code, admin, SystemRoles.Admin, default));
        Assert.Equal(productionId, trace.Batch.Id); Assert.Equal(12, trace.Units.Length);
        Assert.Single(trace.CurrentStock); Assert.Equal(source, trace.CurrentStock[0].WarehouseId);
        receipt = await Call(s => s.GetAsync(receiptId, keeper, SystemRoles.WarehouseKeeper, default));
        var batchId = receipt.Lines.Single().BatchId!.Value;
        Assert.Equal(6, await Db(db => db.InventoryBalanceRecords.Where(b => b.BatchId == batchId).Select(b => b.PhysicalQuantity).SingleAsync()));
        var req = await Call(s => s.CreateRequestAsync(new(source, destination, "Transfer", [], [new(batchId, 2)], null), "transfer-key", admin, SystemRoles.Admin, default));
        Assert.Equal(2, await Db(db => db.InventoryBalanceRecords.Where(b => b.BatchId == batchId).Select(b => b.ReservedQuantity).SingleAsync()));
        var issueId = await Call(s => s.CreateChildAsync(req, "issue-key", keeper, SystemRoles.WarehouseKeeper, default));
        await Assert.ThrowsAsync<PickingOperationException>(() => Call(s => s.CreateChildAsync(req, "issue-other", keeper, SystemRoles.WarehouseKeeper, default)));
        foreach (var carton in receipt.Cartons.Take(2))
        {
            var issue = await Call(s => s.GetAsync(issueId, keeper, SystemRoles.WarehouseKeeper, default));
            await Call(s => s.ScanAsync(issueId, carton.CartonId, issue.Document.Version, keeper, SystemRoles.WarehouseKeeper, default));
        }
        var ready = await Call(s => s.GetAsync(issueId, keeper, SystemRoles.WarehouseKeeper, default));
        issueId = await Call(s => s.PostAsync(issueId, ready.Document.Version, keeper, SystemRoles.WarehouseKeeper, default));
        Assert.Equal(4, await Db(db => db.InventoryBalanceRecords.Where(b => b.BatchId == batchId).Select(b => b.PhysicalQuantity).SingleAsync()));
        var inboundId = await Call(s => s.CreateChildAsync(issueId, "inbound-key", keeper, SystemRoles.WarehouseKeeper, default));
        var inbound = await Call(s => s.GetAsync(inboundId, keeper, SystemRoles.WarehouseKeeper, default));
        foreach (var carton in inbound.Cartons)
        {
            inbound = await Call(s => s.GetAsync(inboundId, keeper, SystemRoles.WarehouseKeeper, default));
            await Call(s => s.ScanAsync(inboundId, carton.CartonId, inbound.Document.Version, keeper, SystemRoles.WarehouseKeeper, default));
        }
        inbound = await Call(s => s.GetAsync(inboundId, keeper, SystemRoles.WarehouseKeeper, default));
        await Call(s => s.PostAsync(inboundId, inbound.Document.Version, keeper, SystemRoles.WarehouseKeeper, default));
        inbound = await Call(s => s.GetAsync(inboundId, keeper, SystemRoles.WarehouseKeeper, default));
        Assert.Equal(today.AddDays(-1), inbound.Lines.Single().ProductionDate);
        var movedId = inbound.Lines.Single().BatchId!.Value;
        Assert.Equal(2, await Db(db => db.InventoryBalanceRecords.Where(b => b.BatchId == movedId).Select(b => b.PhysicalQuantity).SingleAsync()));
        Assert.All(await Db(db => db.BatchQrLabelRecords.Where(l => l.BatchId == movedId && l.QrCode.StartsWith("CID-")).ToArrayAsync()), l => Assert.Equal(1, l.Quantity));
        Assert.Equal(productionId, await Db(db => db.BatchRecords.Where(b => b.Id == movedId).Select(b => b.FactoryProductionBatchId).SingleAsync()));
        var movedCarton = inbound.Cartons.First().CartonId;
        var movedTrace = await Factory(s => s.TraceAsync(movedCarton, admin, SystemRoles.Admin, default));
        Assert.Equal(destination, movedTrace.CurrentStock.Single().WarehouseId);
        Assert.Equal(4, movedTrace.History.Length); // receipt -> picking -> posted issue -> destination receipt
        var movedInventory = await Db(db => new FactoryQrInventoryService(db).ListAsync(admin, SystemRoles.Admin,
            destination, movedCarton, "Available", null, 1, 50, default));
        var movedRow = Assert.Single(movedInventory.Items);
        Assert.Equal(movedId, movedRow.StockBatchId);
        Assert.Equal(produced.Batch.Code, movedRow.ProductionBatchCode);
        Assert.Contains(movedRow.PalletCode, produced.Pallets.Select(p => p.Code));
        // Use real allocation/quote/PO creation for two orders; isolate this product to the source warehouse.
        await Db(async db => {
            foreach (var b in await db.InventoryBalanceRecords.Where(b => db.BatchRecords.Any(x => x.Id == b.BatchId && x.ProductId == product)).ToArrayAsync())
            { b.PhysicalQuantity = b.BatchId == batchId ? 4 : 0; b.ReservedQuantity = 0; b.AvailableQuantity = b.PhysicalQuantity; b.Version++; }
            var retailer = await db.RetailerRecords.SingleAsync(r => r.Code == "10004127");
            retailer.CreditLimit = 100_000_000m;
            var destinationMap = await db.MapLocationRecords.SingleAsync(l => l.RetailerId == retailer.Id);
            var sourceMap = await db.MapLocationRecords.SingleAsync(l => l.WarehouseId == source);
            sourceMap.Latitude = destinationMap.Latitude + 0.001m; sourceMap.Longitude = destinationMap.Longitude + 0.001m;
            foreach (var route in await db.MapDistanceCacheRecords.Where(r => r.RetailerId == retailer.Id && r.WarehouseId == source && r.IsActive).ToArrayAsync())
                route.DistanceMeters = 1000;
            await db.SaveChangesAsync(); return 0;
        });
        var orderIds = new List<Guid>();
        for (var i = 0; i < 2; i++)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sales = await db.UserAccountRecords.SingleAsync(u => u.Email == "sales@fscm.local");
            var retailer = await db.RetailerRecords.SingleAsync(r => r.Code == "10004127");
            var order = await scope.ServiceProvider.GetRequiredService<OrderService>().CreateAsync(new(Guid.NewGuid(), retailer.Id,
                OrderPaymentMethods.Credit, null, null, "Grouped work test", [new(product, 1)]), sales.Id, default);
            Assert.Equal(OrderStatuses.Quoted, order.Status);
            var quoteService = scope.ServiceProvider.GetRequiredService<QuoteService>();
            var quote = (await quoteService.ListForOrderAsync(order.Id, retailer.UserAccountId!.Value, SystemRoles.Retailer, default)).Single();
            await Assert.ThrowsAsync<PickingOperationException>(() => Call(s => s.CreateRequestAsync(
                new(source, null, "Order", [order.Id], [], null), $"before-accept-{i}", admin, SystemRoles.Admin, default)));
            await quoteService.AcceptAsync(quote.Id, quote.Version, OrderPaymentMethods.Credit, retailer.UserAccountId.Value, SystemRoles.Retailer, default);
            orderIds.Add(order.Id);
        }
        var grouped = await Call(s => s.CreateRequestAsync(new(source, null, "Order", orderIds.ToArray(), [], null), "orders-key", admin, SystemRoles.Admin, default));
        var groupedIssueId = await Call(s => s.CreateChildAsync(grouped, "grouped-issue", keeper, SystemRoles.WarehouseKeeper, default));
        foreach (var carton in receipt.Cartons.Skip(2).Take(2))
        {
            var issue = await Call(s => s.GetAsync(groupedIssueId, keeper, SystemRoles.WarehouseKeeper, default));
            await Call(s => s.ScanAsync(groupedIssueId, carton.CartonId, issue.Document.Version, keeper, SystemRoles.WarehouseKeeper, default));
        }
        var prepared = await Call(s => s.GetAsync(groupedIssueId, keeper, SystemRoles.WarehouseKeeper, default));
        Assert.Equal("Prepared", prepared.Document.Status);
        Assert.Equal("Picking", prepared.Document.Kind);
        var originalCarton = prepared.Cartons.First().CartonId;
        await Assert.ThrowsAsync<PickingOperationException>(() => Call(s => s.RemoveCartonAsync(groupedIssueId,
            originalCarton, "CID-NOT-FOUND", "Invalid replacement", prepared.Document.Version, keeper, SystemRoles.WarehouseKeeper, default)));
        await Call(s => s.RemoveCartonAsync(groupedIssueId, originalCarton, null, "Recount", prepared.Document.Version, keeper, SystemRoles.WarehouseKeeper, default));
        var reopened = await Call(s => s.GetAsync(groupedIssueId, keeper, SystemRoles.WarehouseKeeper, default));
        Assert.Equal("Open", reopened.Document.Status);
        Assert.DoesNotContain(reopened.Cartons, c => c.CartonId == originalCarton);
        await using (var readScope = provider.CreateAsyncScope())
        {
            var listId = prepared.Lines.Single(l => l.Id == prepared.Cartons.Single(c => c.CartonId == originalCarton).LineId).PickingListId;
            var shipmentId = await Db(db => db.ShipmentRecords.Where(x => x.PickingListId == listId).Select(x => x.Id).SingleAsync());
            var shipmentStore = readScope.ServiceProvider.GetRequiredService<FSCM.Application.Features.Shipments.IShipmentStore>();
            var labels = await shipmentStore.ListLabelsAsync(shipmentId, false, default);
            Assert.DoesNotContain(labels, x => x.Label.QrCode == originalCarton);
        }

        await Call(s => s.ScanAsync(groupedIssueId, originalCarton, reopened.Document.Version, keeper, SystemRoles.WarehouseKeeper, default));
        prepared = await Call(s => s.GetAsync(groupedIssueId, keeper, SystemRoles.WarehouseKeeper, default));
        Assert.Equal("Prepared", prepared.Document.Status);

        var replacementCode = receipt.Cartons.Skip(4).First().CartonId;
        await Call(s => s.RemoveCartonAsync(groupedIssueId, originalCarton, replacementCode, "Replace carton",
            prepared.Document.Version, keeper, SystemRoles.WarehouseKeeper, default));
        prepared = await Call(s => s.GetAsync(groupedIssueId, keeper, SystemRoles.WarehouseKeeper, default));
        Assert.Equal("Prepared", prepared.Document.Status);
        Assert.DoesNotContain(prepared.Cartons, c => c.CartonId == originalCarton);
        Assert.Contains(prepared.Cartons, c => c.CartonId == replacementCode);
        Assert.Equal("Available", await Db(db => db.BatchQrLabelRecords.Where(l => l.QrCode == originalCarton).Select(l => l.Status).SingleAsync()));
        Assert.Equal(4, await Db(db => db.InventoryBalanceRecords.Where(b => b.BatchId == batchId).Select(b => b.PhysicalQuantity).SingleAsync()));

        Assert.Equal(2, prepared.Lines.Select(l => l.SalesOrderId).Distinct().Count());
        var postedIssue = await Call(s => s.PostAsync(groupedIssueId, prepared.Document.Version, keeper, SystemRoles.WarehouseKeeper, default));
        Assert.NotEqual(groupedIssueId, postedIssue);
        Assert.Equal("Issue", (await Call(s => s.GetAsync(postedIssue, keeper, SystemRoles.WarehouseKeeper, default))).Document.Kind);
        await Assert.ThrowsAsync<PickingOperationException>(() => Call(s => s.RemoveCartonAsync(groupedIssueId,
            originalCarton, null, "Too late", prepared.Document.Version, keeper, SystemRoles.WarehouseKeeper, default)));

        await Call(s => s.PostAsync(groupedIssueId, prepared.Document.Version, keeper, SystemRoles.WarehouseKeeper, default));
        Assert.Equal(2, await Db(db => db.InventoryBalanceRecords.Where(b => b.BatchId == batchId).Select(b => b.PhysicalQuantity).SingleAsync()));
        Assert.All(await Db(db => db.SalesOrderRecords.Where(o => orderIds.Contains(o.Id)).ToArrayAsync()), o => Assert.Equal(OrderStatuses.Issued, o.Status));
        // Receive mixed quality and an entirely missing pallet without creating sellable stock for exceptions.
        var inspectedProduction = await Factory(s => s.SimulateAsync(productionData with { PalletCount = 2, CartonsPerPallet = 3 }, "inspection-production", admin, default));
        await Factory(s => s.DispatchAsync(inspectedProduction, source, default));
        var inspectionBatch = (await Factory(s => s.ListAsync(default))).Single(x => x.Batch.Id == inspectedProduction);
        var inspectionReceipt = await Call(s => s.CreateFactoryAsync(new(source), "inspection-receipt", keeper, SystemRoles.WarehouseKeeper, default));
        var manifest = await Call(s => s.PreviewPalletAsync(inspectionReceipt, inspectionBatch.Pallets[0].Code, keeper, SystemRoles.WarehouseKeeper, default));
        FSCM.Domain.Entities.ReceiptCartonInspection[] inspected = [
            new(manifest.Cartons[0].Code, "Accepted", null, null),
            new(manifest.Cartons[1].Code, "Damaged", "Wet carton", "QUARANTINE-01"),
            new(manifest.Cartons[2].Code, "Missing", "Not on truck", null)];
        await Assert.ThrowsAsync<PickingOperationException>(() => Call(s => s.ReceiveInspectedPalletAsync(inspectionReceipt,
            new(manifest.Pallet.Code, 1, inspected), keeper, SystemRoles.WarehouseKeeper, default)));
        await Assert.ThrowsAsync<PickingOperationException>(() => Call(s => s.ScanReceiptPalletAsync(inspectionReceipt,
            new(manifest.Pallet.Code, 1, "Manual"), keeper, SystemRoles.WarehouseKeeper, null, default)));
        var inspectionScan = await Call(s => s.ScanReceiptPalletAsync(inspectionReceipt, new(manifest.Pallet.Code, 1, "Camera"), keeper, SystemRoles.WarehouseKeeper, "Integration phone", default));
        Assert.Equal(keeper, inspectionScan.ScannedByUserId);
        Assert.Equal(manifest.Pallet.Code, inspectionScan.PalletCode);
        Assert.Empty((await Call(s => s.GetAsync(inspectionReceipt, keeper, SystemRoles.WarehouseKeeper, default))).Cartons);
        Assert.Single(await Call(s => s.ReceiptScansAsync(inspectionReceipt, keeper, SystemRoles.WarehouseKeeper, default)));
        Assert.Equal(0, await Db(db => db.BatchRecords.CountAsync(b => b.FactoryProductionBatchId == inspectedProduction)));
        var inspectedRequest = new InspectedPalletRequest(manifest.Pallet.Code, inspectionScan.DocumentVersion, inspected, inspectionScan.Id);
        await Assert.ThrowsAsync<PickingOperationException>(() => Call(s => s.ReceiveInspectedPalletAsync(inspectionReceipt,
            inspectedRequest with { ScanId = Guid.NewGuid() }, keeper, SystemRoles.WarehouseKeeper, default)));
        await Assert.ThrowsAsync<PickingOperationException>(() => Call(s => s.ReceiveInspectedPalletAsync(receipt2,
            inspectedRequest, keeper, SystemRoles.WarehouseKeeper, default)));
        await Call(s => s.ReceiveInspectedPalletAsync(inspectionReceipt, inspectedRequest, keeper, SystemRoles.WarehouseKeeper, default));
        await Call(s => s.ReceiveInspectedPalletAsync(inspectionReceipt, inspectedRequest, keeper, SystemRoles.WarehouseKeeper, default));
        await Assert.ThrowsAsync<PickingOperationException>(() => Call(s => s.ReceiveInspectedPalletAsync(inspectionReceipt,
            inspectedRequest with { Cartons = inspected.Select(x => x with { Disposition = "Accepted" }).ToArray() }, keeper, SystemRoles.WarehouseKeeper, default)));
        var receiptView = await Call(s => s.GetAsync(inspectionReceipt, keeper, SystemRoles.WarehouseKeeper, default));
        var missingManifest = await Call(s => s.PreviewPalletAsync(inspectionReceipt, inspectionBatch.Pallets[1].Code, keeper, SystemRoles.WarehouseKeeper, default));
        var missingScan = await Call(s => s.ScanReceiptPalletAsync(inspectionReceipt, new(missingManifest.Pallet.Code, receiptView.Document.Version, "Camera"), keeper, SystemRoles.WarehouseKeeper, "Integration phone", default));
        await Call(s => s.ReceiveInspectedPalletAsync(inspectionReceipt, new(missingManifest.Pallet.Code, missingScan.DocumentVersion,
            missingManifest.Cartons.Select(c => new FSCM.Domain.Entities.ReceiptCartonInspection(c.Code, "Missing", "Pallet absent", null)).ToArray(), missingScan.Id), keeper, SystemRoles.WarehouseKeeper, default));
        receiptView = await Call(s => s.GetAsync(inspectionReceipt, keeper, SystemRoles.WarehouseKeeper, default));
        Assert.Single(receiptView.Cartons);
        var inspectedBatchId = receiptView.Lines.Single().BatchId;
        Assert.Equal(1, await Db(db => db.InventoryBalanceRecords.Where(b => b.BatchId == inspectedBatchId).Select(b => b.AvailableQuantity).SingleAsync()));
        Assert.Equal(0, await Db(db => db.BatchQrLabelRecords.CountAsync(l => l.QrCode == inspected[1].CartonCode || l.QrCode == inspected[2].CartonCode)));
        Assert.Equal(2, (await Call(s => s.ReceiptReportsAsync(inspectionReceipt, keeper, SystemRoles.WarehouseKeeper, default))).Length);
        await Call(s => s.PostAsync(inspectionReceipt, receiptView.Document.Version, keeper, SystemRoles.WarehouseKeeper, default));
        await using (var syncScope = provider.CreateAsyncScope())
            await syncScope.ServiceProvider.GetRequiredService<FactoryReceiptSyncDispatcher>().DispatchAsync(default);
        var acknowledged = await FactoryDb(db => db.Set<FactoryPallet>().SingleAsync(p => p.Id == manifest.Pallet.Id));
        Assert.Equal("Received", acknowledged.Status);
        Assert.Contains("Damaged", acknowledged.ReceiptInspectionJson!);
        Assert.Contains("Missing", acknowledged.ReceiptInspectionJson!);
        var splitProduction = await Factory(s => s.SimulateAsync(productionData with { PalletCount = 2, CartonsPerPallet = 1 }, "split-dispatch-production", admin, default));
        var split = (await Factory(s => s.ListAsync(default))).Single(x => x.Batch.Id == splitProduction);
        await Factory(s => s.DispatchAsync(splitProduction, source, default, [split.Pallets[0].Id]));
        await Factory(s => s.DispatchAsync(splitProduction, source, default, [split.Pallets[0].Id]));
        await Factory(s => s.DispatchAsync(splitProduction, destination, default, [split.Pallets[1].Id]));
        await Assert.ThrowsAsync<HttpRequestException>(() => Factory(s => s.DispatchAsync(splitProduction, destination, default, [split.Pallets[0].Id])));
        Assert.Equal(source, await FactoryDb(db => db.Set<FactoryPallet>().Where(p => p.Id == split.Pallets[0].Id).Select(p => p.DestinationWarehouseId).SingleAsync()));
        Assert.Equal(destination, await FactoryDb(db => db.Set<FactoryPallet>().Where(p => p.Id == split.Pallets[1].Id).Select(p => p.DestinationWarehouseId).SingleAsync()));


    }
}
