using FSCM.Application;
using FSCM.Application.Features.AllocationRuns;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Payments;
using FSCM.Application.Features.Picking;
using FSCM.Application.Features.QrLabels;
using FSCM.Application.Features.Quotes;
using FSCM.Application.Features.Shipments;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Infrastructure;
using FSCM.Persistence;
using FSCM.Persistence.DbContexts;
using FSCM.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.IntegrationTests.Persistence;

public sealed class PostgresWorkflowFactAttribute : FactAttribute
{
    public PostgresWorkflowFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FSCM_TEST_POSTGRES")))
            Skip = "Set FSCM_TEST_POSTGRES to a disposable PostgreSQL database connection string.";
    }
}

public sealed class RealTimePostgresWorkflowTests
{
    [PostgresWorkflowFact]
    public async Task Real_time_order_quote_po_picking_receipt_and_collection_use_real_Postgres()
    {
        var connection = Environment.GetEnvironmentVariable("FSCM_TEST_POSTGRES")!;
        var target = new Npgsql.NpgsqlConnectionStringBuilder(connection);
        Assert.True(target.Host is "127.0.0.1" or "localhost");
        Assert.StartsWith("fscm_e2e_", target.Database);
        // This suite creates, migrates and seeds a new database. It never deletes a database.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connection,
            ["SeedData:DefaultPassword"] = "Local-E2e-Only!2049",
            ["Jwt:Secret"] = new string('e', 64),
            ["Jwt:Issuer"] = "FSCM.API",
            ["Jwt:Audience"] = "FSCM.Clients"
        }).Build();
        var services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(config);
        services.AddApplicationServices().AddPersistenceServices(config).AddInfrastructureServices(config);
        await using var provider = services.BuildServiceProvider();
        async Task<R> Call<T, R>(Func<T, Task<R>> action) where T : notnull
        { await using var scope = provider.CreateAsyncScope(); return await action(scope.ServiceProvider.GetRequiredService<T>()); }
        async Task Db(Func<AppDbContext, Task> action)
        { await using var scope = provider.CreateAsyncScope(); await action(scope.ServiceProvider.GetRequiredService<AppDbContext>()); }
        await using (var scope = provider.CreateAsyncScope())
        {
            Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.CanConnectAsync(),
                "Supply a new fscm_e2e_ database name; an existing database must not be modified.");
            await scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>().SeedAsync();
        }
        Guid sales = default, op = default, retailerUser = default, keeper = default, retailer = default, product = default;
        await Db(async db =>
        {
            var users = await db.UserAccountRecords.ToArrayAsync();
            sales = users.Single(u => u.Email == "sales@fscm.local").Id;
            op = users.Single(u => u.Email == "operator@fscm.local").Id;
            retailerUser = users.Single(u => u.Email == "retailer@fscm.local").Id;
            keeper = users.Single(u => u.Email == "warehouse.keeper@fscm.local").Id;
            retailer = (await db.RetailerRecords.SingleAsync(r => r.Code == "10004127")).Id;
            product = (await db.ProductRecords.SingleAsync(p => p.Sku == "SKU001")).Id;
            foreach (var order in await db.SalesOrderRecords.ToArrayAsync()) order.Status = OrderStatuses.Withdrawn;
            var batches = await db.BatchRecords.Where(b => b.ProductId == product).OrderByDescending(b => b.ExpirationDate).ToArrayAsync();
            var selected = batches.GroupBy(b => b.WarehouseId).Take(2).Select(g => g.First().Id).ToHashSet();
            Assert.Equal(2, selected.Count);
            foreach (var batch in batches.Where(b => selected.Contains(b.Id)))
            {
                batch.ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow);
                batch.ExpirationDate = batch.ProductionDate.AddDays(7);
            }
            var destination = await db.MapLocationRecords.SingleAsync(l => l.RetailerId == retailer);
            foreach (var warehouse in batches.Where(b => selected.Contains(b.Id)).Select(b => b.WarehouseId))
            {
                var origin = await db.MapLocationRecords.SingleAsync(l => l.WarehouseId == warehouse);
                // Real-time allocation uses coordinates and a 20 km service radius.
                origin.Latitude = destination.Latitude + 0.01m;
                origin.Longitude = destination.Longitude + 0.01m;
            }
            foreach (var balance in await db.InventoryBalanceRecords.Where(b => batches.Select(x => x.Id).Contains(b.BatchId)).ToArrayAsync())
            { balance.PhysicalQuantity = selected.Contains(balance.BatchId) ? 48m : 0m; balance.ReservedQuantity = 0; balance.AvailableQuantity = balance.PhysicalQuantity; }
            foreach (var label in await db.BatchQrLabelRecords.Where(l => selected.Contains(l.BatchId)).ToArrayAsync()) label.Status = QrLabelStatuses.Invalidated;
            await db.SaveChangesAsync();
        });
        var availability = await Call<OrderAvailabilityService, OrderAvailabilityResult>(s =>
            s.CheckAsync(retailer, [product], sales, SystemRoles.Sales, default));
        var availableProduct = Assert.Single(availability.Items);
        Assert.Equal(96m, availableProduct.EligibleQuantity);
        Assert.Equal(2, availableProduct.Warehouses.Count(w => w.EligibleQuantity > 0));
        Assert.Equal(availableProduct.EligibleQuantity, availableProduct.Warehouses.Sum(w => w.EligibleQuantity));
        var retailerAvailability = await Call<OrderAvailabilityService, OrderAvailabilityResult>(s =>
            s.CheckAsync(retailer, [product], retailerUser, SystemRoles.Retailer, default));
        Assert.Equal(96m, Assert.Single(retailerAvailability.Items).EligibleQuantity);
        Assert.Empty(Assert.Single(retailerAvailability.Items).Warehouses);

        async Task<OrderDetailResult> Create(int quantity, string expectedStatus = OrderStatuses.Quoted)
        {
            var order = await Call<OrderService, OrderDetailResult>(s => s.CreateAsync(new(Guid.NewGuid(), retailer, OrderPaymentMethods.Credit, null, null, "PostgreSQL E2E", [new(product, quantity)]), sales, default));
            Assert.Equal(expectedStatus, order.Status);
            return order;
        }
        var main = await Create(60); var late = await Create(12); var rejected = await Create(12); var expired = await Create(12);
        var shortage = await Create(1, OrderStatuses.OutOfStock);
        await Db(async db =>
        {
            Assert.Empty(await db.SalesOrderQuoteRecords.Where(x => x.SalesOrderId == shortage.Id).ToArrayAsync());
            Assert.Equal(96m, await db.InventoryBalanceRecords.Where(b => db.BatchRecords.Any(x => x.Id == b.BatchId && x.ProductId == product)).SumAsync(b => b.ReservedQuantity));
        });
        async Task<SalesOrderQuoteResult> Quote(Guid order) => (await Call<QuoteService, IReadOnlyCollection<SalesOrderQuoteResult>>(s => s.ListForOrderAsync(order, retailerUser, SystemRoles.Retailer, default))).Single();
        var q = await Quote(main.Id);
        await Call<QuoteService, SalesOrderQuoteResult>(s => s.AcceptAsync(q.Id, q.Version, OrderPaymentMethods.Credit, retailerUser, SystemRoles.Retailer, default));
        var qr = await Quote(rejected.Id);
        await Call<QuoteService, SalesOrderQuoteResult>(s => s.RejectAsync(qr.Id, qr.Version, "E2E reject", retailerUser, SystemRoles.Retailer, default));
        var qe = await Quote(expired.Id);
        await Db(async db => { var row = await db.SalesOrderQuoteRecords.SingleAsync(x => x.Id == qe.Id); row.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1); await db.SaveChangesAsync(); });
        await using (var scope = provider.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<OrderDeadlineService>().SweepAsync(1, default);
        Assert.Equal(SalesOrderQuoteStatuses.Expired, (await Quote(expired.Id)).Status);
        await Db(async db =>
        {
            Assert.Equal(72m, await db.InventoryBalanceRecords.Where(b => db.BatchRecords.Any(x => x.Id == b.BatchId && x.ProductId == product)).SumAsync(b => b.ReservedQuantity));
        });
        async Task Fulfill(Guid orderId)
        {
            var lists = await Call<CurrentPickingService, IReadOnlyCollection<CurrentPickingResult>>(s => s.ListAsync(keeper, SystemRoles.WarehouseKeeper, null, null, default));
            foreach (var list in lists.Where(l => l.OrderId == orderId))
            {
                var picking = await Call<CurrentPickingService, CurrentPickingResult>(s => s.GetAsync(list.PickingCode, keeper, SystemRoles.WarehouseKeeper, default));
                foreach (var item in picking.Items.Where(i => i.RemainingQuantity > 0))
                {
                    var label = await Call<QrLabelService, QrLabelResult>(s => s.GenerateAsync(new(item.BatchId, item.RemainingQuantity, null), Guid.NewGuid().ToString(), keeper, SystemRoles.WarehouseKeeper, default));
                    picking = await Call<CurrentPickingService, CurrentPickingResult>(s => s.ScanAsync(list.PickingCode, new(label.QrCode, item.RemainingQuantity, picking.Version, label.Version,
                        [new RunPickingSplitQuantityData(item.AllocationId, item.RemainingQuantity)]), Guid.NewGuid().ToString(), keeper, SystemRoles.WarehouseKeeper, default));
                }
                await Call<CurrentPickingService, CurrentPickingResult>(s => s.CompleteAsync(list.PickingCode, new(picking.Version, null, "E2E complete"), keeper, SystemRoles.WarehouseKeeper, default));
            }
            var parts = await Call<ShipmentService, IReadOnlyCollection<ShipmentResult>>(s => s.ListAsync(op, SystemRoles.Operator, orderId, null, null, default));
            Assert.NotEmpty(parts);
            if (orderId == main.Id) Assert.Equal(2, parts.Count);
            foreach (var part in parts)
            {
                var issued = await Call<ShipmentService, ShipmentResult>(s => s.ConfirmIssueAsync(part.Id, new(part.Version), keeper, SystemRoles.WarehouseKeeper, default));
                Guid driver = default, vehicle = default;
                await Db(async db =>
                {
                    driver = (await db.DriverRecords.FirstAsync(x => x.WarehouseId == part.WarehouseId && x.IsActive)).Id;
                    vehicle = (await db.VehicleRecords.FirstAsync(x => x.WarehouseId == part.WarehouseId && x.IsActive && x.Capacity >= part.TotalQuantity)).Id;
                });
                var assigned = await Call<ShipmentService, ShipmentResult>(s => s.AssignAsync(part.Id, new(driver, vehicle, issued.Version), SystemRoles.Operator, default));
                var dispatched = await Call<ShipmentService, ShipmentResult>(s => s.DispatchAsync(part.Id, new(assigned.Version), op, SystemRoles.Operator, default));
                string[] codes = [];
                await Db(async db =>
                {
                    codes = await (from evt in db.QrLabelEventRecords
                                   join label in db.BatchQrLabelRecords on evt.BatchQrLabelId equals label.Id
                                   where evt.EventType == QrLabelEventTypes.Dispatched && evt.ReferenceCode == dispatched.OrderCode && label.CurrentWarehouseId == part.WarehouseId
                                   select label.QrCode).Distinct().ToArrayAsync();
                });
                var lines = dispatched.Lines.Select(x => new ConfirmShipmentReceiptLineData(x.OrderLineId, x.BatchId, x.Quantity, 0)).ToArray();
                await Call<ShipmentService, ShipmentResult>(s => s.ConfirmReceiptAsync(part.Id, new(codes, lines, dispatched.Version, "E2E receipt"), Guid.NewGuid().ToString(), retailerUser, SystemRoles.Retailer, default));
            }
            OrderDetailResult detail;
            detail = await Call<OrderService, OrderDetailResult>(s => s.GetAsync(orderId, retailerUser, SystemRoles.Retailer, default));
            Assert.Equal(OrderStatuses.Delivered, detail.Status);
            var payment = await Call<PaymentService, PaymentResult>(s => s.GetByOrderAsync(orderId, sales, SystemRoles.Sales, default));
            Assert.NotNull(payment.DeliveredDate); Assert.Equal(payment.DeliveredDate.Value.AddDays(14), payment.DueDate);
            var paid = await Call<PaymentService, PaymentResult>(s => s.CollectAsync(orderId, new("Cash", payment.Version, "E2E cash"), $"e2e:{orderId}", sales, SystemRoles.Sales, default));
            Assert.Equal("Paid", paid.Status); Assert.Equal(0, paid.OutstandingAmount);
            // Simulate a lost collection response: the same request must replay safely.
            var replay = await Call<PaymentService, PaymentResult>(s => s.CollectAsync(orderId, new("Cash", payment.Version, "E2E cash"), $"e2e:{orderId}", sales, SystemRoles.Sales, default));
            Assert.Equal(paid.Version, replay.Version);
            await Db(async db =>
            {
                Assert.Single(await db.PaymentReceiptAllocationRecords.Where(x => db.PaymentRecords.Any(p => p.Id == x.PaymentId && p.SalesOrderId == orderId)).ToArrayAsync());
            });
        }
        await Fulfill(main.Id);
        var ql = await Quote(late.Id);
        await Call<QuoteService, SalesOrderQuoteResult>(s => s.AcceptAsync(ql.Id, ql.Version, OrderPaymentMethods.Credit, retailerUser, SystemRoles.Retailer, default));
        await Fulfill(late.Id);
        await Db(async db => { Assert.All(await db.InventoryBalanceRecords.Where(b => db.BatchRecords.Any(x => x.Id == b.BatchId && x.ProductId == product)).ToArrayAsync(), b => Assert.Equal(0m, b.ReservedQuantity)); });
    }
}
