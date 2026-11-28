using FSCM.Application;
using FSCM.Application.Features.Loyalty;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Infrastructure;
using FSCM.IntegrationTests.Fixtures;
using FSCM.Persistence;
using FSCM.Persistence.DbContexts;
using FSCM.Persistence.Loyalty;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.IntegrationTests.Persistence;

public sealed class LoyaltyProgramsPostgresTests
{
    [PostgresWorkflowFact]
    public async Task Stores_enforce_retailer_scope_best_points_and_redemption_from_every_eligible_program()
    {
        var target = new Npgsql.NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("FSCM_TEST_POSTGRES"));
        Assert.True(target.Host is "localhost" or "127.0.0.1");
        Assert.StartsWith("fscm_e2e_", target.Database);
        target.Database += "_loyalty_programs";
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:DefaultConnection"] = target.ConnectionString,
            ["TestFixtures:DefaultPassword"] = "Local-E2e-Only!2049",
            ["Jwt:Secret"] = new string('l', 64), ["Jwt:Issuer"] = "FSCM.API", ["Jwt:Audience"] = "FSCM.Clients"
        }).Build();
        var services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(config);
        services.AddApplicationServices().AddPersistenceServices(config).AddInfrastructureServices(config);
        services.AddScoped<WorkflowDatabaseFixture>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Database.CanConnectAsync(), "Use a new disposable test database only.");
        await scope.ServiceProvider.GetRequiredService<WorkflowDatabaseFixture>().SeedAsync();
        var retailer = await db.RetailerRecords.FirstAsync(r => r.UserAccountId != null && r.Status == "Active");
        var admin = await db.UserAccountRecords.FirstAsync(u => u.Role == SystemRoles.Admin);
        var order = await db.SalesOrderRecords.FirstAsync(o => o.RetailerId == retailer.Id);
        var captureAudit = await db.AuditLogRecords.FirstAsync(a => a.EntityId == order.Id && a.Action == "OrderCaptured");
        captureAudit.NewValues = "{}"; // Explicit legacy fixture for the live-scope checks below.
        foreach (var old in await db.LoyaltyProgramRecords.ToArrayAsync()) old.IsActive = false;
        var now = DateTimeOffset.UtcNow;
        LoyaltyProgram Program(string code, decimal rate, bool linked = true, bool active = true, int start = -2, int end = 2)
        {
            var p = new LoyaltyProgram { Id = Guid.NewGuid(), Code = code, Name = code, SpendAmountPerPoint = rate,
                IsActive = active, StartAt = now.AddDays(start), EndAt = now.AddDays(end),
                CreatedByUserId = admin.Id, CreatedAt = now, UpdatedAt = now };
            db.LoyaltyProgramRecords.Add(p);
            if (linked) db.LoyaltyProgramRetailerRecords.Add(new() { Id = Guid.NewGuid(), LoyaltyProgramId = p.Id, RetailerId = retailer.Id });
            return p;
        }
        var best = Program("LP-801", 100);
        var newer = Program("LP-802", 200, start: -1);
        var foreign = Program("LP-803", 1, linked: false);
        Program("LP-804", 1, active: false);
        Program("LP-805", 1, start: 1);
        Program("LP-806", 1, start: -4, end: -1);
        order.FinalAmount = 1_000;
        var payment = await db.PaymentRecords.SingleOrDefaultAsync(p => p.SalesOrderId == order.Id);
        if (payment != null) payment.AdjustedAmount = 1_000;
        await db.SaveChangesAsync();
        var accrual = new LoyaltyAccrualStore(db);
        var view = await accrual.FindAsync(order.Id, default);
        Assert.Equal(best.Id, view!.Program!.Id);
        // No linked program means no earning, even though the foreign program is active.
        best.IsActive = false; newer.IsActive = false; await db.SaveChangesAsync();
        Assert.Null((await accrual.FindAsync(order.Id, default))!.Program);
        best.IsActive = true; newer.IsActive = true; await db.SaveChangesAsync();
        var card = await new LoyaltyCardService(new LoyaltyCardStore(db)).GetAsync(admin.Id, SystemRoles.Admin, retailer.Id, default);
        Assert.Equal(new[] { best.Id, newer.Id }.Order(), card.EligiblePrograms.Select(p => p.Id).Order());
        Assert.Null(card.PointsExpireAt);
        var balance = await db.RetailerLoyaltyRecords.SingleOrDefaultAsync(b => b.RetailerId == retailer.Id);
        if (balance == null) { balance = new() { Id = Guid.NewGuid(), RetailerId = retailer.Id, CreatedAt = now, UpdatedAt = now }; db.RetailerLoyaltyRecords.Add(balance); }
        balance.CurrentPoints = 1_000; balance.HeldPoints = 0;
        LoyaltyReward Reward(LoyaltyProgram p, string code)
        {
            var reward = new LoyaltyReward { Id = Guid.NewGuid(), LoyaltyProgramId = p.Id, Code = code, Name = code,
                RewardType = "Gift", RequiredPoints = 100, IsActive = true, CreatedAt = now, UpdatedAt = now };
            db.LoyaltyRewardRecords.Add(reward); return reward;
        }
        var first = Reward(best, "QUA-801"); var second = Reward(newer, "QUA-802"); var outside = Reward(foreign, "QUA-803");
        await db.SaveChangesAsync();
        var redemption = new RedemptionStore(db);
        var request = new CreateRedemptionData(first.Id, 1, "loyalty-one", null);
        var result = await redemption.CreateAsync(retailer.UserAccountId!.Value, request, now, default);
        var replay = await redemption.CreateAsync(retailer.UserAccountId.Value, request, now, default);
        Assert.Equal(result.Redemption.Id, replay.Redemption.Id);
        await redemption.CreateAsync(retailer.UserAccountId.Value, new(second.Id, 1, "loyalty-two", null), now, default);
        Assert.Equal(800, balance.CurrentPoints); Assert.Equal(200, balance.HeldPoints);
        await Assert.ThrowsAsync<LoyaltyOperationException>(() => redemption.CreateAsync(retailer.UserAccountId.Value, new(outside.Id, 1, "loyalty-outside", null), now, default));
        captureAudit.NewValues = System.Text.Json.JsonSerializer.Serialize(new {
            LoyaltyEligibility = OrderLoyaltyEligibility.Capture(retailer.Id, order.ServerReceivedAt, [best, newer])
        }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        best.EndAt = now.AddSeconds(-1); best.IsActive = false; best.SpendAmountPerPoint = 999;
        db.LoyaltyProgramRetailerRecords.RemoveRange(await db.LoyaltyProgramRetailerRecords.Where(l => l.LoyaltyProgramId == best.Id).ToArrayAsync());
        Program("LP-807", 1); // Newly eligible after the order was received: must not supersede its snapshot.
        await db.SaveChangesAsync();
        var frozen = await accrual.FindAsync(order.Id, default);
        Assert.Equal(best.Id, frozen!.Program!.Id); Assert.Equal(100m, frozen.Program.SpendAmountPerPoint);
    }
}
