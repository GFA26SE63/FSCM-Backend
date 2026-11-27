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
        var reports = new OperationsReportStore(db);
        foreach (var kind in new[] { "redemptions", "loyalty", "promotions", "disposals", "claims", "receivables" })
        {
            var result = await reports.ReadAsync(kind, new(from, to), actor, SystemRoles.WarehouseKeeper, default);
            Assert.Empty(result.Items);
            Assert.Equal(0, result.TotalCount);
        }
        Assert.Empty(await new RefundStore(db).ListObligationsAsync(actor, SystemRoles.Retailer, default));
        var warehouse = await new WarehouseLogisticsReportStore(db).ReadAsync(new(from, to), to,
            actor, SystemRoles.SalesManager, default);
        Assert.Empty(warehouse.Warehouses);
    }
}
