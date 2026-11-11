using FSCM.Application;
using FSCM.Application.Common;
using FSCM.Application.Features.ShelfLifeDiscounts;
using FSCM.Application.Features.TransitTables;
using FSCM.Application.Features.WarehouseConfiguration;
using FSCM.Domain.Entities;
using FSCM.Infrastructure;
using FSCM.Persistence;
using FSCM.Persistence.DbContexts;
using FSCM.Persistence.Seeding;
using FSCM.Persistence.TransitTables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.IntegrationTests.Persistence;

public sealed class SettingsValidationPostgresTests
{
    [PostgresWorkflowFact]
    public async Task Scheduled_settings_preserve_current_pricing_and_eta_until_Vietnam_effective_time()
    {
        var target = new Npgsql.NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("FSCM_TEST_POSTGRES"));
        Assert.True(target.Host is "127.0.0.1" or "localhost");
        Assert.StartsWith("fscm_e2e_", target.Database);
        target.Database += "_settings";
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = target.ConnectionString,
            ["SeedData:DefaultPassword"] = "Local-E2e-Only!2049",
            ["Jwt:Secret"] = new string('s', 64), ["Jwt:Issuer"] = "FSCM.API", ["Jwt:Audience"] = "FSCM.Clients"
        }).Build();
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(config);
        services.AddApplicationServices().AddPersistenceServices(config).AddInfrastructureServices(config);
        services.AddSingleton<TimeProvider>(clock);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Database.CanConnectAsync(), "Use a NEW disposable database. Existing databases must not be modified.");
        await scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>().SeedAsync();

        async Task<T> Call<S, T>(Func<S, Task<T>> action) where S : notnull
        {
            await using var callScope = provider.CreateAsyncScope();
            return await action(callScope.ServiceProvider.GetRequiredService<S>());
        }

        var actor = await db.UserAccountRecords.Where(u => u.Role == "Admin").Select(u => u.Id).FirstAsync();
        var product = await db.ProductRecords.FirstAsync(p => p.Status == ProductStatuses.Published);
        // Seed historical pricing directly; public publishing deliberately forbids backdating.
        var historical = new ShelfLifeDiscountTable { Id = Guid.NewGuid(), Version = 1,
            EffectiveFrom = clock.Now.AddDays(-1), IsActive = true, CreatedByUserId = actor,
            CreatedAt = clock.Now.AddDays(-1), UpdatedAt = clock.Now.AddDays(-1) };
        db.ShelfLifeDiscountTableRecords.Add(historical);
        db.ShelfLifeDiscountTierRecords.Add(new ShelfLifeDiscountTier { Id = Guid.NewGuid(),
            ShelfLifeDiscountTableId = historical.Id, Scope = ShelfLifeDiscountScopes.Sku,
            ProductId = product.Id, MinRemainingPercent = 0, MaxRemainingPercent = 100,
            DiscountType = ShelfLifeDiscountTypes.Percent, DiscountValue = 10, CreatedAt = historical.CreatedAt });
        await db.SaveChangesAsync();
        var baselineTransit = await Call<TransitTableService, TransitLookupResult>(s => s.LookupAsync(1000, null, default));
        var baselinePrice = await Call<ShelfLifeDiscountService, ShelfLifeDiscountMatch>(s => s.MatchAsync(product.Id, product.ProductCategoryId, 20, 100000, null, default));
        var headTransit = await Call<TransitTableService, TransitTableResult>(s => s.GetCurrentAsync(default));
        var headDiscount = await Call<ShelfLifeDiscountService, ShelfLifeDiscountTableResult>(s => s.GetCurrentAsync(default));
        var from = EffectiveDates.Tomorrow(clock.Now);
        while (from <= headTransit.EffectiveFrom || from <= headDiscount.EffectiveFrom) from = from.AddDays(1);
        // A real +07:00 request must be normalized before writing to PostgreSQL timestamptz.
        var transitData = new PublishTransitTableData(20000, from, "Scheduled validation test",
            [new(0, 20001, 60, 321, 100000)]);
        var discountData = new PublishShelfLifeDiscountTableData(from, "Scheduled validation test",
            [new(ShelfLifeDiscountScopes.Sku, product.Id, null, 0, 100, ShelfLifeDiscountTypes.Percent, 37)]);
        var nextTransit = await Call<TransitTableService, TransitTableResult>(s => s.PublishAsync(transitData, actor, default));
        var nextDiscount = await Call<ShelfLifeDiscountService, ShelfLifeDiscountTableResult>(s => s.PublishAsync(discountData, actor, default));
        Assert.Equal(TimeSpan.Zero, nextTransit.EffectiveFrom.Offset);
        Assert.Equal(TimeSpan.Zero, nextDiscount.EffectiveFrom.Offset);
        Assert.Equal(baselineTransit.Version, (await Call<TransitTableService, TransitLookupResult>(s => s.LookupAsync(1000, null, default))).Version);
        Assert.Equal(baselinePrice.DiscountAmountPerUnit, (await Call<ShelfLifeDiscountService, ShelfLifeDiscountMatch>(s => s.MatchAsync(product.Id, product.ProductCategoryId, 20, 100000, null, default))).DiscountAmountPerUnit);

        await Assert.ThrowsAsync<TransitTableOperationException>(() => Call<TransitTableService, TransitTableResult>(s => s.PublishAsync(transitData with { EffectiveFrom = clock.Now }, actor, default)));
        await Assert.ThrowsAsync<ShelfLifeDiscountOperationException>(() => Call<ShelfLifeDiscountService, ShelfLifeDiscountTableResult>(s => s.PublishAsync(discountData with { EffectiveFrom = from.AddDays(1), Tiers = [discountData.Tiers.Single() with { ProductId = Guid.NewGuid() }] }, actor, default)));
        Assert.Equal(nextTransit.Version, (await Call<TransitTableService, TransitTableResult>(s => s.GetCurrentAsync(default))).Version);
        Assert.Equal(nextDiscount.Version, (await Call<ShelfLifeDiscountService, ShelfLifeDiscountTableResult>(s => s.GetCurrentAsync(default))).Version);

        clock.Now = from.ToUniversalTime();
        Assert.Equal(321, (await Call<TransitTableService, TransitLookupResult>(s => s.LookupAsync(1000, null, default))).TransitMinutes);
        Assert.Equal(37000m, (await Call<ShelfLifeDiscountService, ShelfLifeDiscountMatch>(s => s.MatchAsync(product.Id, product.ProductCategoryId, 20, 100000, null, default))).DiscountAmountPerUnit);

        var before = await Call<WarehouseConfigurationService, IReadOnlyCollection<ConfigurationResult>>(s => s.ListCurrentConfigurationsAsync(default));
        await Assert.ThrowsAsync<WarehouseConfigurationOperationException>(() => Call<WarehouseConfigurationService, ConfigurationResult>(s => s.UpdateConfigurationAsync(BusinessConfigurationKeys.OrderStepCartons, "0", actor, default)));
        var after = await Call<WarehouseConfigurationService, IReadOnlyCollection<ConfigurationResult>>(s => s.ListCurrentConfigurationsAsync(default));
        Assert.Equal(before.Single(c => c.Key == BusinessConfigurationKeys.OrderStepCartons).Version, after.Single(c => c.Key == BusinessConfigurationKeys.OrderStepCartons).Version);

        // Database uniqueness is the final guard for simultaneous publishers, exposed as 409.
        await using var conflictScope = provider.CreateAsyncScope();
        var conflictDb = conflictScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var duplicate = new TransitTable { Id = Guid.NewGuid(), Version = nextTransit.Version, EffectiveFrom = from.ToUniversalTime(), IsActive = false, CreatedByUserId = actor, CreatedAt = clock.Now, UpdatedAt = clock.Now };
        var store = new TransitTableStore(conflictDb);
        store.AddTable(duplicate);
        var conflict = await Assert.ThrowsAsync<TransitTableOperationException>(() => store.SaveChangesAsync(default));
        Assert.Equal("conflict", conflict.Code);
        await using var verifyScope = provider.CreateAsyncScope();
        Assert.False(await verifyScope.ServiceProvider.GetRequiredService<AppDbContext>().TransitTableRecords.AnyAsync(t => t.Id == duplicate.Id));
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
