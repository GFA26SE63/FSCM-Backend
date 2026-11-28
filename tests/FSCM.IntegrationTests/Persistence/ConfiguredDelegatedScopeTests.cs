using FSCM.Application.Features.Reports;
using FSCM.Domain.Authorization;
using FSCM.Persistence.DbContexts;
using FSCM.Persistence.Reports;
using FSCM.Persistence.Refunds;

namespace FSCM.IntegrationTests.Persistence;

public sealed class ConfiguredDelegatedScopeTests
{
    [ConfiguredDatabaseFact]
    public async Task Delegated_reports_and_refund_obligations_never_fall_back_to_global_data_without_assignments()
    {
        await using var db = new AppDbContextFactory().CreateDbContext([]);
        var actor = Guid.NewGuid(); // No assigned retailer, warehouse, sales group or declarations.
        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 11, 27);
        var reports = new BusinessReportStore(db, TimeProvider.System);
        foreach (var tab in new[] { "shipments", "drivers", "inbound", "picking", "transfers" })
        {
            var result = await reports.GetFulfillmentAsync(new(from, to, tab), actor, SystemRoles.WarehouseKeeper, default);
            Assert.Empty(result.Shipments.Items);
            Assert.Equal(0, result.Shipments.TotalCount);
        }
        foreach (var tab in new[] { "expiry", "locks", "disposals" })
        {
            var result = await reports.GetInventoryRiskAsync(new(from, to, tab), actor, SystemRoles.WarehouseKeeper, default);
            Assert.Empty(result.Expiry.Items);
            Assert.Empty(result.Disposals.Items);
        }
        foreach (var tab in new[] { "orders", "receipts", "refunds" })
        {
            var result = await reports.GetFinancialAsync(new(from, to, tab), actor, SystemRoles.SalesManager, default);
            Assert.Empty(result.Orders.Items);
            Assert.Equal(0, result.Orders.TotalCount);
        }
        await Assert.ThrowsAsync<ReportOperationException>(() =>
            reports.GetFinancialAsync(new(from, to), actor, SystemRoles.WarehouseKeeper, default));
        await Assert.ThrowsAsync<ReportOperationException>(() =>
            reports.GetInventoryRiskAsync(new(from, to), actor, SystemRoles.SalesManager, default));
        // Exercise every remaining report query against the real configured database schema.
        await reports.GetReceivablesAsync(new(View: "customers"), actor, SystemRoles.SalesManager, default);
        await reports.GetPromotionsAsync(new(from, to), actor, SystemRoles.SalesManager, default);
        foreach (var tab in new[] { "redemptions", "ledger", "balances" })
            await reports.GetLoyaltyAsync(new(from, to, tab), actor, SystemRoles.SalesManager, default);
        await reports.GetClaimsAsync(new(from, to), actor, SystemRoles.SalesManager, default);
        Assert.Empty(await new RefundStore(db).ListObligationsAsync(actor, SystemRoles.Retailer, default));
        var warehouse = await new WarehouseLogisticsReportStore(db).ReadAsync(new(from, to), to,
            actor, SystemRoles.SalesManager, default);
        Assert.Empty(warehouse.Warehouses);
    }
}
