using FSCM.Application;
using FSCM.Application.Features.Loyalty;
using FSCM.Application.Features.Reports;
using FSCM.Domain.Entities;
using FSCM.Infrastructure;
using FSCM.Persistence;
using FSCM.Persistence.DbContexts;
using FSCM.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.IntegrationTests.Persistence;

public sealed class OperationsReportsPostgresTests
{
    [PostgresWorkflowFact]
    public async Task Reports_use_saved_quantity_exclude_pending_points_and_execute_all_sql_projections()
    {
        var target = new Npgsql.NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("FSCM_TEST_POSTGRES"));
        Assert.True(target.Host is "127.0.0.1" or "localhost");
        Assert.StartsWith("fscm_e2e_", target.Database);
        target.Database += "_ops";
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = target.ConnectionString,
            ["SeedData:DefaultPassword"] = "Local-E2e-Only!2049", ["Jwt:Secret"] = new string('r', 64),
            ["Jwt:Issuer"] = "FSCM.API", ["Jwt:Audience"] = "FSCM.Clients"
        }).Build();
        var services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(config);
        services.AddApplicationServices().AddPersistenceServices(config).AddInfrastructureServices(config);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Database.CanConnectAsync(), "Use a fresh disposable database.");
        await scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>().SeedAsync();
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.AddHours(7).DateTime);
        var retailer = await db.RetailerRecords.SingleAsync(r => r.Code == "10004127");
        var op = await db.UserAccountRecords.SingleAsync(u => u.Email == "operator@fscm.local");
        var program = new LoyaltyProgram { Id = Guid.NewGuid(), Code = "REPORT-TEST", Name = "Report test",
            StartAt = now.AddDays(-1), EndAt = now.AddDays(1), SpendAmountPerPoint = 1000, MinimumRedeemPoints = 1,
            IsActive = true, CreatedByUserId = op.Id, CreatedAt = now, UpdatedAt = now };
        var gift = new LoyaltyReward { Id = Guid.NewGuid(), LoyaltyProgramId = program.Id, Code = "REPORT-GIFT",
            Name = "Gift", RequiredPoints = 100, AvailableQuantity = 10, RewardType = "Gift", IsActive = true,
            CreatedAt = now, UpdatedAt = now };
        // Deactivate other enrollments so this test owns the redemption program selection.
        foreach (var p in await db.LoyaltyProgramRecords.ToArrayAsync()) p.IsActive = false;
        db.AddRange(program, gift, new LoyaltyProgramRetailer { Id = Guid.NewGuid(), LoyaltyProgramId = program.Id,
            RetailerId = retailer.Id });
        var card = await db.RetailerLoyaltyRecords.SingleOrDefaultAsync(c => c.RetailerId == retailer.Id);
        if (card == null) { card = new RetailerLoyalty { Id = Guid.NewGuid(), RetailerId = retailer.Id, CreatedAt = now }; db.Add(card); }
        card.CurrentPoints = 1000; card.HeldPoints = 0; card.UpdatedAt = now;
        db.Add(new LoyaltyTransaction { Id = Guid.NewGuid(), LoyaltyProgramId = program.Id, RetailerLoyaltyId = card.Id,
            TransactionType = "PendingEarn", Points = 900, BalanceAfter = 1000, CreatedAt = now });
        await db.SaveChangesAsync();
        var redemptionStore = scope.ServiceProvider.GetRequiredService<IRedemptionStore>();
        var workflow = scope.ServiceProvider.GetRequiredService<IRedemptionWorkflowStore>();
        var created = await redemptionStore.CreateAsync(retailer.UserAccountId!.Value,
            new CreateRedemptionData(gift.Id, 2, Guid.NewGuid().ToString(), "test"), now, default);
        gift.RequiredPoints = 300; await db.SaveChangesAsync();
        await workflow.TransitionAsync(created.Redemption.Id, "Rejected", op.Id, now.AddSeconds(1), default);
        Assert.Equal(10, gift.AvailableQuantity); Assert.Equal(1000, card.CurrentPoints); Assert.Equal(0, card.HeldPoints);
        var second = await redemptionStore.CreateAsync(retailer.UserAccountId.Value,
            new CreateRedemptionData(gift.Id, 1, Guid.NewGuid().ToString(), "second"), now.AddSeconds(2), default);
        await workflow.TransitionAsync(second.Redemption.Id, "Approved", op.Id, now.AddSeconds(3), default);
        await workflow.TransitionAsync(second.Redemption.Id, "Delivered", op.Id, now.AddSeconds(4), default);
        await workflow.TransitionAsync(second.Redemption.Id, "Completed", op.Id, now.AddSeconds(5), default);
        var reports = scope.ServiceProvider.GetRequiredService<IOperationsReportStore>();
        var q = new OperationsReportQuery(today, today, retailer.Code, program.Code, PageSize: 1);
        var redemptions = await reports.ReadAsync("redemptions", q, default);
        Assert.Equal(2, redemptions.TotalCount); Assert.Single(redemptions.Items);
        var completed = Assert.Single((await reports.ReadAsync("redemptions", q with { Status = "Completed" }, default)).Items);
        Assert.Equal(1, completed.Quantity); Assert.Equal(op.Id, completed.ApprovedByUserId); Assert.NotNull(completed.DeliveredAt);
        var rejected = Assert.Single((await reports.ReadAsync("redemptions", q with { Status = "Rejected" }, default)).Items);
        Assert.Equal(2, rejected.Quantity); Assert.Equal(200, rejected.Points);
        var loyalty = await reports.ReadAsync("loyalty", q, default);
        Assert.Equal(200, loyalty.Summary.Single(s => s.Label == "Điểm khả dụng tăng trong bộ lọc").Value);
        Assert.Equal(500, loyalty.Summary.Single(s => s.Label == "Điểm khả dụng giảm trong bộ lọc").Value);
        var empty = await reports.ReadAsync("loyalty", q with { Retailer = "DOES-NOT-EXIST" }, default);
        Assert.Empty(empty.Items); Assert.Equal(0, empty.TotalCount);
        // Exercise all joins, snapshots, enum conversions, filtering, aggregation and paging in PostgreSQL.
        await reports.ReadAsync("promotions", q with { Program = null, Status = "Completed" }, default);
    }
}
