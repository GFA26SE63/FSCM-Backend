using FSCM.Application.Features.Roles;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using FSCM.Persistence.Identity;
using FSCM.Persistence.Roles;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FSCM.Application.Features.Identity;
using FSCM.IntegrationTests.Api;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FSCM.IntegrationTests.Persistence;

public sealed class ConfiguredDatabaseFactAttribute : FactAttribute
{
    public ConfiguredDatabaseFactAttribute()
    {
        if (AppDatabaseConfiguration.FindAppSettingsPath() is null)
            Skip = "The ignored API appsettings.json is unavailable on this runner; no alternate database is substituted.";
    }
}

public sealed class ConfiguredDatabaseRbacTests
{
    [ConfiguredDatabaseFact]
    public async Task Assigned_role_FK_drives_live_grants_and_revocation_on_the_single_configured_database()
    {
        await using var db = new AppDbContextFactory().CreateDbContext([]);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(await db.UserAccountRecords.AnyAsync(u => u.RoleDefinitionId == null));
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var actor = await db.UserAccountRecords.AsNoTracking().Where(u => u.Role == SystemRoles.Admin)
                .Select(u => u.Id).FirstAsync();
            var service = new RoleService(new RoleStore(db), TimeProvider.System);
            var code = "RBAC_" + Guid.NewGuid().ToString("N");
            var role = await service.CreateRoleAsync(new(code, "Kiểm thử phân quyền", null,
                ["warehouse-inbound", "skus", "api:Products.Create"], SystemRoles.WarehouseKeeper), actor, default);
            var user = new UserAccount
            {
                Id = Guid.NewGuid(), RoleDefinitionId = role.Id, Role = SystemRoles.WarehouseKeeper,
                Email = code.ToLowerInvariant() + "@rbac-test.invalid", FullName = "Kiểm thử RBAC",
                IdentityProvider = "Local", IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            };
            db.UserAccountRecords.Add(user);
            await db.SaveChangesAsync();
            var identities = new IdentityStore(db);
            var grants = await identities.GetUserPermissionsAsync(user.Id, default);
            Assert.Contains("warehouse-inbound", grants);
            Assert.DoesNotContain("warehouse-outbound", grants);
            Assert.Equal(code, (await identities.FindUserByIdAsync(user.Id, default))!.RoleDefinition!.Code);

            using var identityFactory = new IdentityAuthorizationTests.IdentityApiFactory();
            using var factory = identityFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<AppDbContext>();
                services.AddSingleton(db);
                services.RemoveAll<IIdentityStore>();
                services.AddScoped<IIdentityStore, IdentityStore>();
            }));
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identityFactory.CreateToken(user));
            // Warehouse persona with an explicit API grant reaches validation on an erstwhile Admin-only command.
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/products", new { })).StatusCode);
            await service.UpdateRoleAsync(role.Id, new(role.Name, null, ["skus"]), actor, default);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/products", new { })).StatusCode);

            await service.UpdateRoleAsync(role.Id, new(role.Name, null, []), actor, default);
            Assert.Empty(await identities.GetUserPermissionsAsync(user.Id, default));
            await service.ListRolesAsync(null, default);
            Assert.Empty(await identities.GetUserPermissionsAsync(user.Id, default));
            await service.UpdateRoleAsync(role.Id, new(role.Name, null, ["batches"]), actor, default);
            Assert.Equal(new[] { "batches" }, await identities.GetUserPermissionsAsync(user.Id, default));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteRoleAsync(role.Id, actor, default));
            user.IsActive = false;
            await db.SaveChangesAsync();
            Assert.Empty(await identities.GetUserPermissionsAsync(user.Id, default));
        }
        finally
        {
            // Only this test's writes are rolled back. No database reset, seed, or table cleanup.
            await transaction.RollbackAsync();
        }
    }
}
