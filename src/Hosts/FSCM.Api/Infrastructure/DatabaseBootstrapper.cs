using FSCM.BuildingBlocks.Application;
using FSCM.BuildingBlocks.Infrastructure.Persistence;
using FSCM.Modules.Identity.Application;
using FSCM.Modules.Identity.Domain.Users;
using FSCM.Modules.Logistics.Application;
using FSCM.Modules.Logistics.Domain.Transit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace FSCM.Api.Infrastructure;

/// <summary>
/// Local / demo database setup when <c>Database:AutoCreate = true</c>:
/// creates the schema of every module (one schema per module, same database) and seeds what the system cannot work without:
/// the Admin account (Admin is never created from the UI) and the shared transit table agreed on 09/10/2026.
/// On Azure use EF Core migrations instead (see README, "Migrations").
/// </summary>
public static class DatabaseBootstrapper
{
    public static async Task RunAsync(IServiceProvider root, IConfiguration cfg, ILogger log, CancellationToken ct = default)
    {
        using var scope = root.CreateScope();
        var sp = scope.ServiceProvider;

        var contexts = new List<DbContext> { sp.GetRequiredService<SharedDbContext>() };
        contexts.AddRange(sp.GetServices<ModuleDbContext>());
        foreach (var db in contexts)
        {
            var creator = db.GetService<IRelationalDatabaseCreator>();
            if (!await creator.ExistsAsync(ct)) await creator.CreateAsync(ct);
            try
            {
                await creator.CreateTablesAsync(ct);
                log.LogInformation("Created tables for {Context}", db.GetType().Name);
            }
            catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.DuplicateTable or PostgresErrorCodes.DuplicateSchema)
            {
                log.LogInformation("Tables of {Context} already exist", db.GetType().Name);
            }
        }

        // Admin (seeded only).
        var users = sp.GetRequiredService<IUserRepository>();
        if (await users.GetAsync("admin", ct) is null)
        {
            users.Add(AppUser.SeedAdmin("admin", cfg["Seed:AdminEntraObjectId"], cfg["Seed:AdminFullName"] ?? "Quản trị hệ thống", cfg["Seed:AdminEmail"] ?? "admin@fscm.local"));
            await sp.GetRequiredService<IIdentityUnitOfWork>().SaveChangesAsync(ct);
            log.LogInformation("Seeded Admin account 'admin'");
        }

        // Shared transit table (CR-02) + distance limits.
        var transit = sp.GetRequiredService<ITransitRuleRepository>();
        var today = sp.GetRequiredService<IClock>().Today;
        if ((await transit.InForceAsync(today, ct)).Count == 0)
        {
            await transit.ReplaceTableAsync(TransitTable.Defaults(new DateOnly(2026, 10, 9)), ct);
            await transit.SaveLimitsAsync(new DistanceLimits(), ct);
            await sp.GetRequiredService<ILogisticsUnitOfWork>().SaveChangesAsync(ct);
            log.LogInformation("Seeded transit table (0–10 km 1 day, 10–20 km 2 days, 20–40 km 3 days)");
        }
    }
}
