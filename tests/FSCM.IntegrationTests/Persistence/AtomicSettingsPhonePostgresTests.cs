using FSCM.Application;
using FSCM.Application.Features.Identity;
using FSCM.Application.Features.Promotions;
using FSCM.Application.Features.WarehouseConfiguration;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Infrastructure;
using FSCM.Persistence;
using FSCM.Persistence.DbContexts;
using FSCM.Persistence.Identity;
using FSCM.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.IntegrationTests.Persistence;

public sealed class AtomicSettingsPhonePostgresTests
{
    [PostgresWorkflowFact]
    public async Task Migration_group_rollback_replay_and_concurrent_phone_uniqueness_use_real_Postgres()
    {
        var target = new Npgsql.NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("FSCM_TEST_POSTGRES"));
        Assert.True(target.Host is "127.0.0.1" or "localhost");
        Assert.StartsWith("fscm_e2e_", target.Database);
        target.Database += "_atomicphone";
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = target.ConnectionString,
            ["SeedData:DefaultPassword"] = "Local-E2e-Only!2049",
            ["Jwt:Secret"] = new string('a', 64), ["Jwt:Issuer"] = "FSCM.API", ["Jwt:Audience"] = "FSCM.Clients"
        }).Build();
        var services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(config);
        services.AddApplicationServices().AddPersistenceServices(config).AddInfrastructureServices(config);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Database.CanConnectAsync(), "Use a NEW disposable database; existing data must not be modified.");
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261111052230_AddClaimCancelledStatus");
        var now = DateTimeOffset.UtcNow;
        var legacy = Guid.NewGuid(); var current = Guid.NewGuid(); var blocked = Guid.NewGuid(); var blockedOwner = Guid.NewGuid();
        async Task OldAccount(Guid id, string email, string phone, bool active) => await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "USER_ACCOUNT" ("Id","IdentityProvider","Email","Phone","FullName","Role","IsActive","CreatedAt","UpdatedAt")
            VALUES ({id},'Local',{email},{phone},'Migration test','Retailer',{active},{now},{now});
            """);
        await OldAccount(legacy, "legacy@pending.fscm.local", "0900000091", false);
        await OldAccount(current, "current@example.test", "0900000091", false);
        await OldAccount(blocked, "active@pending.fscm.local", "0900000092", true);
        await OldAccount(blockedOwner, "owner@example.test", "0900000092", true);
        var migrationError = await Assert.ThrowsAnyAsync<Exception>(() => migrator.MigrateAsync());
        Assert.Contains("Account phone duplicates remain", migrationError.ToString());
        Assert.Equal("0900000091", await db.UserAccountRecords.Where(u => u.Id == legacy).Select(u => u.Phone).SingleAsync());
        Assert.False(await db.AuditLogRecords.AnyAsync(a => a.Action == "LegacyPlaceholderPhoneReleased"));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"USER_ACCOUNT\" SET \"Phone\" = '0900000093' WHERE \"Id\" = {blockedOwner}");
        await migrator.MigrateAsync();
        Assert.Null(await db.UserAccountRecords.Where(u => u.Id == legacy).Select(u => u.Phone).SingleAsync());
        Assert.Equal("0900000091", await db.UserAccountRecords.Where(u => u.Id == current).Select(u => u.Phone).SingleAsync());
        Assert.Equal("0900000092", await db.UserAccountRecords.Where(u => u.Id == blocked).Select(u => u.Phone).SingleAsync());
        Assert.Single(await db.AuditLogRecords.Where(a => a.Action == "LegacyPlaceholderPhoneReleased" && a.EntityId == legacy).ToArrayAsync());
        await scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>().SeedAsync();
        db.ChangeTracker.Clear();

        async Task<T> Call<S, T>(Func<S, Task<T>> action) where S : notnull
        {
            await using var callScope = provider.CreateAsyncScope();
            return await action(callScope.ServiceProvider.GetRequiredService<S>());
        }
        var actor = await db.UserAccountRecords.Where(u => u.Role == SystemRoles.Admin).Select(u => u.Id).FirstAsync();
        var products = await db.ProductRecords.Where(p => p.Status == ProductStatuses.Published).OrderBy(p => p.Sku).Take(2).Select(p => p.Id).ToArrayAsync();
        Assert.Equal(2, products.Length);
        async Task<string[]> ConfigurationSnapshot() => await db.SystemConfigurationRecords.AsNoTracking().OrderBy(c => c.ConfigKey).ThenBy(c => c.Version)
            .Select(c => c.ConfigKey + ":" + c.Version + ":" + c.IsActive + ":" + c.ConfigValue).ToArrayAsync();
        var beforeSettings = await ConfigurationSnapshot();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION followup_fail_config() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
              IF NEW."ConfigKey" = 'mlor.default_ratio' AND NEW."ConfigValue"::text = '0.35' THEN
                RAISE EXCEPTION 'Injected configuration commit failure';
              END IF; RETURN NEW;
            END $$;
            CREATE CONSTRAINT TRIGGER followup_fail_config AFTER INSERT ON "SYSTEM_CONFIGURATION"
              DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION followup_fail_config();
            """);
        ConfigurationValueData[] entries = [new(BusinessConfigurationKeys.ServiceRadiusMeters, "33000"), new(BusinessConfigurationKeys.DefaultMlorRatio, "0.35")];
        var settingsError = await Assert.ThrowsAnyAsync<Exception>(() => Call<WarehouseConfigurationService, IReadOnlyCollection<ConfigurationResult>>(s => s.UpdateConfigurationsAsync(entries, actor, default)));
        Assert.Contains("Injected configuration commit failure", settingsError.ToString());
        Assert.Equal(beforeSettings, await ConfigurationSnapshot());
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER followup_fail_config ON \"SYSTEM_CONFIGURATION\"; DROP FUNCTION followup_fail_config();");
        var savedSettings = await Call<WarehouseConfigurationService, IReadOnlyCollection<ConfigurationResult>>(s => s.UpdateConfigurationsAsync(entries, actor, default));
        Assert.Single(savedSettings.Select(c => c.EffectiveFrom).Distinct());
        var afterSettings = await ConfigurationSnapshot();
        await Call<WarehouseConfigurationService, IReadOnlyCollection<ConfigurationResult>>(s => s.UpdateConfigurationsAsync(entries, actor, default));
        Assert.Equal(afterSettings, await ConfigurationSnapshot());

        var beforeCampaigns = await db.PromotionRecords.CountAsync();
        var group = new CreateCampaignGroupData(Guid.NewGuid(), "PerSku", "Atomic campaign test", 10, 11, now, now.AddDays(2), products, [], [],
            [new(products[0], 10, 9), new(products[1], 30, 11)], true);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION followup_fail_campaign() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
              IF NEW."Name" = 'Atomic campaign test' AND NEW."DiscountPercent" = 11 THEN
                RAISE EXCEPTION 'Injected campaign commit failure';
              END IF; RETURN NEW;
            END $$;
            CREATE CONSTRAINT TRIGGER followup_fail_campaign AFTER INSERT ON "PROMOTION"
              DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION followup_fail_campaign();
            """);
        var campaignError = await Assert.ThrowsAnyAsync<Exception>(() => Call<PromotionService, IReadOnlyCollection<PromotionResult>>(s => s.CreateCampaignGroupAsync(group, actor, default)));
        Assert.Contains("Injected campaign commit failure", campaignError.ToString());
        Assert.Equal(beforeCampaigns, await db.PromotionRecords.CountAsync());
        Assert.False(await db.IdempotencyKeyRecords.AnyAsync(k => k.Key == $"campaign:{group.RequestId:N}"));
        Assert.False(await db.AuditLogRecords.AnyAsync(a => a.EntityName == "CampaignSubmission" && a.EntityId == group.RequestId));
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER followup_fail_campaign ON \"PROMOTION\"; DROP FUNCTION followup_fail_campaign();");
        var savedCampaigns = await Call<PromotionService, IReadOnlyCollection<PromotionResult>>(s => s.CreateCampaignGroupAsync(group, actor, default));
        Assert.Equal(new decimal[] { 9, 11 }, savedCampaigns.Select(p => p.DiscountPercent));
        Assert.Equal(new decimal[] { 10, 30 }, savedCampaigns.Select(p => p.MinimumQuantity));
        Assert.All(savedCampaigns, p => Assert.True(p.IsActive));
        // The same request after a lost response returns the original IDs, even after key cleanup.
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"IDEMPOTENCY_KEY\" WHERE \"Key\" = {$"campaign:{group.RequestId:N}"}");
        var replay = await Call<PromotionService, IReadOnlyCollection<PromotionResult>>(s => s.CreateCampaignGroupAsync(group, actor, default));
        Assert.Equal(savedCampaigns.Select(p => p.Id), replay.Select(p => p.Id));
        Assert.Equal(beforeCampaigns + 2, await db.PromotionRecords.CountAsync());
        await Assert.ThrowsAsync<PromotionOperationException>(() => Call<PromotionService, IReadOnlyCollection<PromotionResult>>(s => s.CreateCampaignGroupAsync(group with { ActivateOnSave = false }, actor, default)));
        var concurrent = group with { RequestId = Guid.NewGuid(), Name = "Concurrent campaign test" };
        var simultaneous = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Call<PromotionService, IReadOnlyCollection<PromotionResult>>(s => s.CreateCampaignGroupAsync(concurrent, actor, default))));
        Assert.Equal(simultaneous[0].Select(p => p.Id), simultaneous[1].Select(p => p.Id));
        Assert.Equal(beforeCampaigns + 4, await db.PromotionRecords.CountAsync());

        // Force both writers past the pre-check to exercise the database uniqueness guard.
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();
        var first = new IdentityStore(firstScope.ServiceProvider.GetRequiredService<AppDbContext>());
        var second = new IdentityStore(secondScope.ServiceProvider.GetRequiredService<AppDbContext>());
        Assert.Null(await first.FindUserByPhoneAsync("0901234000", default));
        Assert.Null(await second.FindUserByPhoneAsync("0901234000", default));
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        void Stage(IdentityStore store, Guid id)
        {
            store.AddUser(new UserAccount { Id = id, Email = $"{id:N}@example.test", Phone = "0901234000", FullName = "Concurrent account test", Role = SystemRoles.Sales,
                IdentityProvider = IdentityProviders.Local, IsActive = true, CreatedAt = now, UpdatedAt = now });
            store.AddCredential(new UserCredential { Id = Guid.NewGuid(), UserAccountId = id, PasswordHash = "test-only", CreatedAt = now, UpdatedAt = now });
            store.AddAuditLog(new AuditLog { Id = Guid.NewGuid(), UserAccountId = actor, EntityName = "UserAccount", EntityId = id, Action = "Created", CreatedAt = now });
        }
        Stage(first, ids[0]); Stage(second, ids[1]);
        async Task<bool> Save(IdentityStore store)
        {
            try { await store.SaveChangesAsync(default); return true; }
            catch (IdentityOperationException exception) { Assert.Equal("conflict", exception.Code); return false; }
        }
        Assert.Single(await Task.WhenAll(Save(first), Save(second)), saved => saved);
        Assert.Single(await db.UserAccountRecords.Where(u => u.Phone == "0901234000").ToArrayAsync());
        Assert.Single(await db.UserCredentials.Where(c => ids.Contains(c.UserAccountId)).ToArrayAsync());
        Assert.Single(await db.AuditLogRecords.Where(a => ids.Contains(a.EntityId)).ToArrayAsync());
        var winningEmail = await db.UserAccountRecords.Where(u => u.Phone == "0901234000").Select(u => u.Email).SingleAsync();
        await using var duplicateEmailScope = provider.CreateAsyncScope();
        var duplicateEmailStore = new IdentityStore(duplicateEmailScope.ServiceProvider.GetRequiredService<AppDbContext>());
        var duplicateEmailId = Guid.NewGuid();
        duplicateEmailStore.AddUser(new UserAccount { Id = duplicateEmailId, Email = winningEmail, Phone = "0901234001", FullName = "Repeated account request",
            Role = SystemRoles.Sales, IdentityProvider = IdentityProviders.Local, IsActive = true, CreatedAt = now, UpdatedAt = now });
        var emailConflict = await Assert.ThrowsAsync<IdentityOperationException>(() => duplicateEmailStore.SaveChangesAsync(default));
        Assert.Equal("conflict", emailConflict.Code);
        Assert.False(await db.UserAccountRecords.AnyAsync(u => u.Id == duplicateEmailId));
    }
}
