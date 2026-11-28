using FSCM.Application;
using FSCM.Application.Features.Loyalty;
using FSCM.Application.Features.Reports;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Infrastructure;
using FSCM.Persistence;
using FSCM.Persistence.DbContexts;
using FSCM.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.IntegrationTests.Persistence;

public sealed class BusinessReportsPostgresTests
{
    [PostgresWorkflowFact]
    public async Task All_seven_business_reports_execute_cleanly_on_postgresql_with_correct_scoping()
    {
        var target = new Npgsql.NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("FSCM_TEST_POSTGRES"));
        Assert.True(target.Host is "127.0.0.1" or "localhost");
        Assert.StartsWith("fscm_e2e_", target.Database);
        target.Database += $"_brpt_{Guid.NewGuid().ToString("N")[..12]}";
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = target.ConnectionString,
            ["TestFixtures:DefaultPassword"] = "Local-E2e-Only!2049",
            ["Jwt:Secret"] = new string('r', 64),
            ["Jwt:Issuer"] = "FSCM.API",
            ["Jwt:Audience"] = "FSCM.Clients"
        }).Build();
        var services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(config);
        services.AddApplicationServices().AddPersistenceServices(config).AddInfrastructureServices(config);
        services.AddScoped<WorkflowDatabaseFixture>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Database.CanConnectAsync(), "Use a fresh disposable database.");
        await scope.ServiceProvider.GetRequiredService<WorkflowDatabaseFixture>().SeedAsync();

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.AddHours(7).DateTime);
        var retailer = await db.RetailerRecords.SingleAsync(r => r.Code == "10004127");
        var op = await db.UserAccountRecords.SingleAsync(u => u.Email == "operator@fscm.local");

        var program = new LoyaltyProgram
        {
            Id = Guid.NewGuid(), Code = "RPT-OVERHAUL", Name = "Report Overhaul",
            StartAt = now.AddDays(-1), EndAt = now.AddDays(1), SpendAmountPerPoint = 1000, MinimumRedeemPoints = 1,
            IsActive = true, CreatedByUserId = op.Id, CreatedAt = now, UpdatedAt = now
        };
        var gift = new LoyaltyReward
        {
            Id = Guid.NewGuid(), LoyaltyProgramId = program.Id, Code = "RPT-GIFT", Name = "Gift",
            RequiredPoints = 100, AvailableQuantity = 10, RewardType = "Gift", IsActive = true, CreatedAt = now, UpdatedAt = now
        };
        foreach (var p in await db.LoyaltyProgramRecords.ToArrayAsync()) p.IsActive = false;
        db.AddRange(program, gift, new LoyaltyProgramRetailer { Id = Guid.NewGuid(), LoyaltyProgramId = program.Id, RetailerId = retailer.Id });

        var card = await db.RetailerLoyaltyRecords.SingleOrDefaultAsync(c => c.RetailerId == retailer.Id)
            ?? new RetailerLoyalty { Id = Guid.NewGuid(), RetailerId = retailer.Id, CreatedAt = now };
        if (card.CurrentPoints == 0 && card.CreatedAt == now) db.Add(card);
        card.CurrentPoints = 1000; card.HeldPoints = 0; card.UpdatedAt = now;
        await db.SaveChangesAsync();

        var redemptionStore = scope.ServiceProvider.GetRequiredService<IRedemptionStore>();
        var workflow = scope.ServiceProvider.GetRequiredService<IRedemptionWorkflowStore>();
        var created = await redemptionStore.CreateAsync(retailer.UserAccountId!.Value, new CreateRedemptionData(gift.Id, 1, Guid.NewGuid().ToString(), "test"), now, default);
        await workflow.TransitionAsync(created.Redemption.Id, "Approved", op.Id, now.AddSeconds(1), default);
        await workflow.TransitionAsync(created.Redemption.Id, "Delivered", op.Id, now.AddSeconds(2), default);
        await workflow.TransitionAsync(created.Redemption.Id, "Completed", op.Id, now.AddSeconds(3), default);

        var reports = scope.ServiceProvider.GetRequiredService<IBusinessReportStore>();
        foreach (var tab in new[] { "orders", "receipts", "refunds" })
            await reports.GetFinancialAsync(new(today.AddDays(-30), today, tab), op.Id, SystemRoles.Operator, default);
        foreach (var view in new[] { "invoices", "customers" })
            await reports.GetReceivablesAsync(new(View: view), op.Id, SystemRoles.Operator, default);
        await reports.GetPromotionsAsync(new(today.AddDays(-30), today), op.Id, SystemRoles.Operator, default);
        foreach (var tab in new[] { "redemptions", "ledger", "balances" })
        {
            var res = await reports.GetLoyaltyAsync(new(today.AddDays(-30), today, tab, Retailer: retailer.Code, Program: program.Code), op.Id, SystemRoles.Operator, default);
            if (tab == "redemptions") Assert.Equal(1, res.Redemptions.TotalCount);
        }
        await reports.GetClaimsAsync(new(today.AddDays(-30), today), op.Id, SystemRoles.Operator, default);
        foreach (var tab in new[] { "shipments", "drivers", "inbound", "picking", "transfers" })
            await reports.GetFulfillmentAsync(new(today.AddDays(-30), today, tab), op.Id, SystemRoles.Operator, default);
        foreach (var tab in new[] { "expiry", "locks", "disposals" })
            await reports.GetInventoryRiskAsync(new(today.AddDays(-30), today, tab), op.Id, SystemRoles.Operator, default);
    }
}
