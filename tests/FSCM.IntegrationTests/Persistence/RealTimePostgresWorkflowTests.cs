using FSCM.Application;
using FSCM.Application.Features.AllocationRuns;
using FSCM.Application.Features.Complaints;
using FSCM.Application.Features.Loyalty;
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
    private sealed class CrashAllocator : IRealTimeAllocationService
    {
        public Task AllocateAsync(Guid order, Guid actor, CancellationToken ct) => throw new InvalidOperationException("Simulated process interruption after SO commit.");
    }
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
        await Db(async db =>
        {
            (await db.SystemConfigurationRecords.SingleAsync(c => c.ConfigKey == "allocation.service_radius_meters" && c.IsActive)).ConfigValue = "30000";
            (await db.SystemConfigurationRecords.SingleAsync(c => c.ConfigKey == "allocation.weights" && c.IsActive)).ConfigValue = "{\"expiry\":0.7,\"distance\":0.2,\"coverage\":0.1}";
            var table = await db.TransitTableRecords.SingleAsync(t => t.IsActive);
            table.MaxServingDistanceMeters = 30000;
            (await db.TransitTableBandRecords.Where(b => b.TransitTableId == table.Id).OrderByDescending(b => b.MaxDistanceMeters).FirstAsync()).MaxDistanceMeters = 30001;
            var route = await db.MapDistanceCacheRecords.FirstAsync(r => r.RetailerId == retailer && r.IsActive);
            route.DistanceMeters = 25000; // Must use cached road distance and the configured radius, not 20 km/default coordinates.
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
        Assert.NotNull(main.AllocationAudit);
        Assert.Equal(30000m, main.AllocationAudit.Weights.MaximumDistanceMeters);
        Assert.Equal(.7m, main.AllocationAudit.Weights.ShelfLife);
        Assert.Equal(System.Text.Json.JsonValueKind.String, System.Text.Json.JsonSerializer.SerializeToElement(main.AllocationAudit)
            .GetProperty("Lines")[0].GetProperty("Filters")[0].GetProperty("Reason").ValueKind);
        Assert.NotEmpty(main.AllocationAudit.Lines.Single().Filters);
        Assert.Contains(main.AllocationAudit.Lines.Single().Scores, s => s.Selected);
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
                if (orderId == main.Id && part.Id == parts.First().Id)
                {
                    await Db(async db => { foreach (var batch in await db.BatchRecords.Where(b =>
                        db.AllocationRecords.Any(a => a.BatchId == b.Id && main.Lines.Select(l => l.Id).Contains(a.SalesOrderLineId))).ToArrayAsync())
                        batch.ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1); await db.SaveChangesAsync(); });
                    await Assert.ThrowsAsync<OrderOperationException>(() => Call<ShipmentService, ShipmentResult>(s =>
                        s.ConfirmIssueAsync(part.Id, new(part.Version), keeper, SystemRoles.WarehouseKeeper, default)));
                    await Db(async db => { foreach (var batch in await db.BatchRecords.Where(b =>
                        db.AllocationRecords.Any(a => a.BatchId == b.Id && main.Lines.Select(l => l.Id).Contains(a.SalesOrderLineId))).ToArrayAsync())
                        batch.ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7); await db.SaveChangesAsync(); });
                }
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
                if (orderId == main.Id && part.Id == parts.Last().Id)
                    lines[0] = lines[0] with { ReceivedQuantity = lines[0].ReceivedQuantity - 1 };
                var receipt = await Call<ShipmentService, ShipmentResult>(s => s.ConfirmReceiptAsync(part.Id, new(codes, lines, dispatched.Version, "E2E receipt"), Guid.NewGuid().ToString(), retailerUser, SystemRoles.Retailer, default));
                if (receipt.Status == ShipmentStatuses.Exception)
                {
                    var resolved = await Call<ShipmentService, ShipmentResult>(s => s.ResolveExceptionAsync(part.Id,
                        new("Shortage recorded for complaint handling", receipt.Version), op, SystemRoles.Operator, default));
                    Assert.Equal(ShipmentStatuses.Received, resolved.Status);
                }
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
        await Db(async db =>
        {
            db.LoyaltyProgramRecords.Add(new LoyaltyProgram { Id = Guid.NewGuid(), Code = "LP-E2E", Name = "E2E earning snapshot",
                SpendAmountPerPoint = 10000, MinimumRedeemPoints = 1, StartAt = DateTimeOffset.UtcNow.AddDays(-1),
                EndAt = DateTimeOffset.UtcNow.AddDays(10), IsActive = true, CreatedByUserId = op,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });
        await Fulfill(main.Id);
        var targets = await Call<ComplaintService, IReadOnlyCollection<ComplaintTarget>>(s => s.ListTargetsAsync(retailerUser, main.Id, default));
        Assert.Equal(2, targets.Count);
        var complaintTarget = targets.First();
        var rejectedClaim = await Call<ComplaintService, ReceiptDiscrepancy>(s => s.CreateAsync(retailerUser,
            new(main.OrderCode, complaintTarget.Sku, complaintTarget.LabelCode, complaintTarget.ExpectedQuantity - 1, "reject test", null, complaintTarget.ShipmentId, AllocationId: complaintTarget.AllocationId), default));
        await Call<ComplaintService, ComplaintResult>(s => s.ResolveAsync(rejectedClaim.Id, op, new("Rejected", false), default));
        await Assert.ThrowsAsync<ComplaintOperationException>(() => Call<ComplaintService, ComplaintResult>(s =>
            s.ResolveAsync(rejectedClaim.Id, op, new("Changed decision", true), default)));
        var claim = await Call<ComplaintService, ReceiptDiscrepancy>(s => s.CreateAsync(retailerUser,
            new(main.OrderCode, complaintTarget.Sku, complaintTarget.LabelCode, complaintTarget.ExpectedQuantity - 1, "missing one", null, complaintTarget.ShipmentId, AllocationId: complaintTarget.AllocationId), default));
        await Assert.ThrowsAsync<ComplaintOperationException>(() => Call<ComplaintService, ReceiptDiscrepancy>(s => s.CreateAsync(retailerUser,
            new(main.OrderCode, complaintTarget.Sku, complaintTarget.LabelCode, complaintTarget.ExpectedQuantity - 1, "different note", null, complaintTarget.ShipmentId, AllocationId: complaintTarget.AllocationId), default)));
        await Db(async db => { var program = await db.LoyaltyProgramRecords.SingleAsync(p => p.Code == "LP-E2E");
            program.SpendAmountPerPoint = 1; program.IsActive = false; program.EndAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            program.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(); });
        var approvedClaim = await Call<ComplaintService, ComplaintResult>(s => s.ResolveAsync(claim.Id, op, new("Approved", true), default));
        Assert.True(approvedClaim.FinancialAdjustmentApplied);
        var afterClaim = await Call<PaymentService, PaymentResult>(s => s.GetByOrderAsync(main.Id, op, SystemRoles.Operator, default));
        await Call<ComplaintService, ComplaintResult>(s => s.ResolveAsync(claim.Id, op, new("Replay with different note", true), default));
        Assert.Equal(afterClaim.Version, (await Call<PaymentService, PaymentResult>(s => s.GetByOrderAsync(main.Id, op, SystemRoles.Operator, default))).Version);
        var adjusted = await Call<PaymentService, PaymentResult>(s => s.AdjustReceivableAsync(main.Id,
            new(10000, "direct adjustment", afterClaim.Version), "e2e-direct-adjust", op, SystemRoles.Operator, default));
        await Db(async db =>
        {
            var ledger = await db.LoyaltyTransactionRecords.Where(l => l.SalesOrderId == main.Id &&
                (l.TransactionType == LoyaltyTransactionTypes.Earned || l.TransactionType == LoyaltyTransactionTypes.EarnedAdjusted)).ToArrayAsync();
            Assert.Equal((int)decimal.Floor(adjusted.AdjustedAmount / 10000m), ledger.Sum(l => l.Points));
            Assert.All(ledger, l => Assert.Equal(10000m, l.SpendAmountPerPointSnapshot));
        });
        var ql = await Quote(late.Id);
        await Call<QuoteService, SalesOrderQuoteResult>(s => s.AcceptAsync(ql.Id, ql.Version, OrderPaymentMethods.Credit, retailerUser, SystemRoles.Retailer, default));
        await Fulfill(late.Id);
        var interruptedRequest = new CreateAssistedOrderData(Guid.NewGuid(), retailer, OrderPaymentMethods.Credit,
            null, null, "Recovery E2E", [new(product, 1)]);
        await using (var scope = provider.CreateAsyncScope())
        {
            var interrupted = ActivatorUtilities.CreateInstance<OrderService>(scope.ServiceProvider, new CrashAllocator());
            await Assert.ThrowsAsync<InvalidOperationException>(() => interrupted.CreateAsync(interruptedRequest, sales, default));
        }
        Guid interruptedId = default;
        await Db(async db => { var row = await db.SalesOrderRecords.SingleAsync(o => o.ClientOrderKey == interruptedRequest.ClientOrderKey);
            interruptedId = row.Id; Assert.Equal(OrderStatuses.Allocating, row.Status); });
        await using (var scope = provider.CreateAsyncScope())
        {
            var recoveryStore = scope.ServiceProvider.GetRequiredService<IRealTimeAllocationStore>();
            Assert.Contains(await recoveryStore.ListRecoverableAsync(DateTimeOffset.UtcNow.AddMinutes(1), default), r => r.Id == interruptedId);
            await recoveryStore.MarkFailedAsync(interruptedId, sales, OrderStatuses.AllocationFailed, "Interrupted", DateTimeOffset.UtcNow, default);
        }
        var failedOrder = await Call<OrderService, OrderDetailResult>(s => s.GetAsync(interruptedId, sales, SystemRoles.Sales, default));
        var recovered = await Call<OrderService, OrderDetailResult>(s => s.RetryAllocationAsync(interruptedId, failedOrder.Version, sales, SystemRoles.Sales, default));
        Assert.Equal(OrderStatuses.Quoted, recovered.Status);
        var replayedOrder = await Call<OrderService, OrderDetailResult>(s => s.CreateAsync(interruptedRequest, sales, default));
        Assert.Equal(recovered.Version, replayedOrder.Version);
        await Db(async db => { Assert.Equal(2, await db.AllocationRunRecords.CountAsync(r => r.SalesOrderId == interruptedId));
            Assert.Single(await db.SalesOrderRecords.Where(o => o.ClientOrderKey == interruptedRequest.ClientOrderKey).ToArrayAsync()); });
        var recoveredQuote = await Quote(interruptedId);
        await Call<QuoteService, SalesOrderQuoteResult>(s => s.RejectAsync(recoveredQuote.Id, recoveredQuote.Version, "Release recovery test stock", retailerUser, SystemRoles.Retailer, default));
        await Db(async db => { Assert.All(await db.InventoryBalanceRecords.Where(b => db.BatchRecords.Any(x => x.Id == b.BatchId && x.ProductId == product)).ToArrayAsync(), b => Assert.Equal(0m, b.ReservedQuantity)); });
    }
}
