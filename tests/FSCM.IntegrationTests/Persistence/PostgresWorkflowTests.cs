using FSCM.Application;
using FSCM.Application.Features.AllocationRuns;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Payments;
using FSCM.Application.Features.Picking;
using FSCM.Application.Features.QrLabels;
using FSCM.Application.Features.Quotes;
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

public sealed class PostgresWorkflowTests
{
    [PostgresWorkflowFact]
    public async Task Credit_multiwarehouse_receipt_collection_late_PO_and_expiration_use_real_Postgres()
    {
        var connection = Environment.GetEnvironmentVariable("FSCM_TEST_POSTGRES")!;
        var target = new Npgsql.NpgsqlConnectionStringBuilder(connection);
        Assert.True(target.Host is "127.0.0.1" or "localhost");
        Assert.StartsWith("fscm_e2e_", target.Database);
        // This suite migrates and seeds its database. The caller must supply a dedicated test DB.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:DefaultConnection"] = connection,
            ["SeedData:DefaultPassword"] = "Local-E2e-Only!2049",
            ["Jwt:Secret"] = new string('e', 64), ["Jwt:Issuer"] = "FSCM.API", ["Jwt:Audience"] = "FSCM.Clients"
        }).Build();
        var services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(config);
        services.AddApplicationServices().AddPersistenceServices(config).AddInfrastructureServices(config);
        await using var provider = services.BuildServiceProvider();
        async Task<R> Call<T, R>(Func<T, Task<R>> action) where T : notnull
        { await using var scope = provider.CreateAsyncScope(); return await action(scope.ServiceProvider.GetRequiredService<T>()); }
        async Task Db(Func<AppDbContext, Task> action)
        { await using var scope = provider.CreateAsyncScope(); await action(scope.ServiceProvider.GetRequiredService<AppDbContext>()); }
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>().SeedAsync();
        Guid sales = default, op = default, retailerUser = default, keeper = default, retailer = default, product = default;
        await Db(async db => {
            var users = await db.UserAccountRecords.ToArrayAsync();
            sales = users.Single(u => u.Email == "sales@fscm.local").Id;
            op = users.Single(u => u.Email == "operator@fscm.local").Id;
            retailerUser = users.Single(u => u.Email == "retailer@fscm.local").Id;
            keeper = users.Single(u => u.Email == "warehouse.keeper@fscm.local").Id;
            retailer = (await db.RetailerRecords.SingleAsync(r => r.Code == "10004127")).Id;
            product = (await db.ProductRecords.SingleAsync(p => p.Sku == "SKU001")).Id;
            foreach (var order in await db.SalesOrderRecords.ToArrayAsync()) order.Status = OrderStatuses.Cancelled;
            var batches = await db.BatchRecords.Where(b => b.ProductId == product).OrderByDescending(b => b.ExpirationDate).ToArrayAsync();
            var selected = batches.GroupBy(b => b.WarehouseId).Take(2).Select(g => g.First().Id).ToHashSet();
            Assert.Equal(2, selected.Count);
            var destination = await db.MapLocationRecords.SingleAsync(l => l.RetailerId == retailer);
            foreach (var warehouse in batches.Where(b => selected.Contains(b.Id)).Select(b => b.WarehouseId)) {
                var origin = await db.MapLocationRecords.SingleAsync(l => l.WarehouseId == warehouse);
                db.MapDistanceCacheRecords.Add(new MapDistanceCache {
                    Id = Guid.NewGuid(), WarehouseId = warehouse, RetailerId = retailer,
                    OriginLocationId = origin.Id, DestinationLocationId = destination.Id,
                    OriginLocationVersion = origin.Version, DestinationLocationVersion = destination.Version,
                    DistanceMeters = 1000, DurationSeconds = 300, Provider = "E2E fixture", ProviderStatus = "OK",
                    Version = 1, CalculatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1), IsActive = true
                });
            }
            foreach (var balance in await db.InventoryBalanceRecords.Where(b => batches.Select(x => x.Id).Contains(b.BatchId)).ToArrayAsync())
            { balance.PhysicalQuantity = selected.Contains(balance.BatchId) ? 48m : 0m; balance.ReservedQuantity = 0; balance.AvailableQuantity = balance.PhysicalQuantity; }
            foreach (var label in await db.BatchQrLabelRecords.Where(l => selected.Contains(l.BatchId)).ToArrayAsync()) label.Status = QrLabelStatuses.Invalidated;
            await db.SaveChangesAsync();
        });
        async Task<OrderDetailResult> Create(int quantity) {
            var order = await Call<OrderService, OrderDetailResult>(s => s.CreateAsync(new(Guid.NewGuid(), retailer, OrderPaymentMethods.Credit, null, null, "PostgreSQL E2E", [new(product, quantity)]), sales, default));
            return await Call<OrderService, OrderDetailResult>(s => s.ApproveAsync(order.Id, order.Version, op, SystemRoles.Operator, default));
        }
        var main = await Create(60); var late = await Create(12); var rejected = await Create(12); var expired = await Create(12);
        var run = await Call<AllocationRunService, AllocationRunResult>(s => s.StartAsync(DateTimeOffset.UtcNow.AddSeconds(2), default));
        await Call<AllocationRunService, AllocationPricing>(s => s.PlanAsync(run.Id, default));
        var versions = await Call<AllocationPlanApprovalService, IReadOnlyCollection<AllocationPlanVersionResult>>(s => s.ListAsync(run.Id, default));
        var version = versions.OrderByDescending(v => v.VersionNumber).First();
        await Call<AllocationPlanApprovalService, AllocationPlanVersionResult>(s => s.ApproveAsync(version.Id, version.Version, op, default));
        async Task<SalesOrderQuoteResult> Quote(Guid order) => (await Call<QuoteService, IReadOnlyCollection<SalesOrderQuoteResult>>(s => s.ListForOrderAsync(order, retailerUser, SystemRoles.Retailer, default))).Single();
        var q = await Quote(main.Id);
        await Call<QuoteService, SalesOrderQuoteResult>(s => s.AcceptAsync(q.Id, q.Version, retailerUser, SystemRoles.Retailer, default));
        var qr = await Quote(rejected.Id);
        await Call<QuoteService, SalesOrderQuoteResult>(s => s.RejectAsync(qr.Id, qr.Version, "E2E reject", retailerUser, SystemRoles.Retailer, default));
        var qe = await Quote(expired.Id);
        await Db(async db => { var row = await db.SalesOrderQuoteRecords.SingleAsync(x => x.Id == qe.Id); row.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1); await db.SaveChangesAsync(); });
        await using (var scope = provider.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<QuoteService>().ExpirePendingAsync(default);
        Assert.Equal(SalesOrderQuoteStatuses.Expired, (await Quote(expired.Id)).Status);
        async Task Fulfill(Guid orderId) {
            var lists = await Call<RunPickingIssueService, IReadOnlyCollection<RunPickingListResult>>(s => s.IssueAsync(run.Id, default));
            foreach (var list in lists.Where(l => l.Gathers.Any(g => g.Splits.Any(x => x.SalesOrderId == orderId)))) {
                var picking = await Call<PickingService, PickingListResult>(s => s.GetAsync(list.PickingCode, keeper, SystemRoles.WarehouseKeeper, default));
                foreach (var gather in picking.Gathers.Where(g => g.RemainingQuantity > 0)) {
                    var label = await Call<QrLabelService, QrLabelResult>(s => s.GenerateAsync(new(gather.BatchId, gather.RemainingQuantity, null), Guid.NewGuid().ToString(), keeper, SystemRoles.WarehouseKeeper, default));
                    picking = await Call<PickingService, PickingListResult>(s => s.ScanAsync(list.PickingCode, new(label.QrCode, gather.RemainingQuantity, picking.Version, label.Version,
                        gather.Splits.Where(x => x.RemainingQuantity > 0).Select(x => new RunPickingSplitQuantityData(x.AllocationRunAllocationId, x.RemainingQuantity)).ToArray()), Guid.NewGuid().ToString(), keeper, SystemRoles.WarehouseKeeper, default));
                }
                await Call<PickingService, PickingListResult>(s => s.CompleteAsync(list.PickingCode, new(picking.Version, null, "E2E complete"), keeper, SystemRoles.WarehouseKeeper, default));
            }
            var parts = await Call<FSCM.Application.Features.Shipments.ShipmentService, IReadOnlyCollection<FSCM.Application.Features.Shipments.ShipmentResult>>(s => s.ListAsync(op, SystemRoles.Operator, orderId, null, null, default));
            Assert.NotEmpty(parts);
            if (orderId == main.Id) Assert.Equal(2, parts.Count);
            foreach (var part in parts) await Call<FSCM.Application.Features.Shipments.ShipmentService, FSCM.Application.Features.Shipments.ShipmentResult>(s => s.ConfirmIssueAsync(part.Id, new(part.Version), keeper, SystemRoles.WarehouseKeeper, default));
            var detail = await Call<OrderService, OrderDetailResult>(s => s.GetAsync(orderId, retailerUser, SystemRoles.Retailer, default));
            Assert.NotEmpty(detail.LabelCodes);
            if (parts.Count > 1) {
                Guid badLabel = default;
                await Db(async db => {
                    badLabel = await (from evt in db.QrLabelEventRecords join label in db.BatchQrLabelRecords on evt.BatchQrLabelId equals label.Id
                        where evt.EventType == QrLabelEventTypes.Dispatched && evt.ReferenceCode == detail.OrderCode && label.CurrentWarehouseId == parts.Last().WarehouseId
                        select label.Id).FirstAsync();
                    (await db.BatchQrLabelRecords.SingleAsync(x => x.Id == badLabel)).Status = QrLabelStatuses.Available;
                    await db.SaveChangesAsync();
                });
                await Assert.ThrowsAsync<FSCM.Application.Features.Shipments.ShipmentOperationException>(() =>
                    Call<FSCM.Application.Features.Shipments.ShipmentService, IReadOnlyCollection<FSCM.Application.Features.Shipments.ShipmentResult>>(s => s.ConfirmOrderShipmentsReceiptAsync(orderId, detail.LabelCodes, "must rollback", Guid.NewGuid().ToString(), retailerUser, SystemRoles.Retailer, default)));
                await Db(async db => {
                    Assert.False(await db.ReceiptConfirmationRecords.AnyAsync(r => r.SalesOrderId == orderId));
                    Assert.All(await db.ShipmentRecords.Where(x => x.SalesOrderId == orderId).ToArrayAsync(), x => Assert.NotEqual("Received", x.Status));
                    (await db.BatchQrLabelRecords.SingleAsync(x => x.Id == badLabel)).Status = QrLabelStatuses.InTransit;
                    await db.SaveChangesAsync();
                });
            }
            await Call<FSCM.Application.Features.Shipments.ShipmentService, IReadOnlyCollection<FSCM.Application.Features.Shipments.ShipmentResult>>(s => s.ConfirmOrderShipmentsReceiptAsync(orderId, detail.LabelCodes, "E2E receive", Guid.NewGuid().ToString(), retailerUser, SystemRoles.Retailer, default));
            detail = await Call<OrderService, OrderDetailResult>(s => s.GetAsync(orderId, retailerUser, SystemRoles.Retailer, default));
            Assert.Equal(OrderStatuses.Delivered, detail.Status);
            var payment = await Call<PaymentService, PaymentResult>(s => s.GetByOrderAsync(orderId, sales, SystemRoles.Sales, default));
            Assert.NotNull(payment.DeliveredDate); Assert.Equal(payment.DeliveredDate.Value.AddDays(14), payment.DueDate);
            var paid = await Call<PaymentService, PaymentResult>(s => s.CollectAsync(orderId, new("Cash", payment.Version, "E2E cash"), $"e2e:{orderId}", sales, SystemRoles.Sales, default));
            Assert.Equal("Paid", paid.Status); Assert.Equal(0, paid.OutstandingAmount);
        }
        await Fulfill(main.Id);
        var ql = await Quote(late.Id);
        await Call<QuoteService, SalesOrderQuoteResult>(s => s.AcceptAsync(ql.Id, ql.Version, retailerUser, SystemRoles.Retailer, default));
        await Fulfill(late.Id);
        await Db(async db => { Assert.All(await db.InventoryBalanceRecords.Where(b => db.BatchRecords.Any(x => x.Id == b.BatchId && x.ProductId == product)).ToArrayAsync(), b => Assert.Equal(0m, b.ReservedQuantity)); });
    }
}
