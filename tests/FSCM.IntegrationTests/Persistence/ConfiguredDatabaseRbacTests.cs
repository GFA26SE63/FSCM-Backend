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
            // The persisted mapping, not the bootstrap C# mapping, is authoritative when saving.
            var mapping = await db.ScreenApiMappings.SingleAsync(row => row.BusinessRole == SystemRoles.WarehouseKeeper &&
                row.ScreenCode == "warehouse-outbound" && row.ApiCode == "api:PickingLists.Scan");
            db.ScreenApiMappings.Remove(mapping);
            await db.SaveChangesAsync();
            var mappedRole = await service.CreateRoleAsync(new("MAP_" + Guid.NewGuid().ToString("N"), "Mapping test", null,
                BaseRole: SystemRoles.WarehouseKeeper, SelectedScreenCodes: ["warehouse-outbound"]), actor, default);
            Assert.DoesNotContain("api:PickingLists.Scan", mappedRole.Permissions);
            db.ScreenApiMappings.Add(mapping);
            await db.SaveChangesAsync();
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
                services.RemoveAll<IRoleStore>();
                services.AddScoped<IRoleStore, RoleStore>();
            }));
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identityFactory.CreateToken(user));
            // Warehouse persona with an explicit API grant reaches validation on an erstwhile Admin-only command.
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/products", new { })).StatusCode);

            // Exercise the actual Save button's PUT contract with a stale/malicious API grant list.
            await service.UpdateRoleAsync(role.Id, new(role.Name, null,
                ["warehouse-outbound", "warehouse-inbound", "api:PickingLists.Scan",
                    "api:WarehouseDocuments.Factory", "api:WarehouseDocuments.Scan"]), actor, default);
            var sameUserToken = client.DefaultRequestHeaders.Authorization;
            var admin = await db.UserAccountRecords.SingleAsync(account => account.Id == actor);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identityFactory.CreateToken(admin));
            var saved = await client.PutAsJsonAsync($"/api/v1/roles/{role.Id}", new
            {
                name = role.Name,
                selectedScreenCodes = new[] { "warehouse-inbound" },
                permissions = new[] { "warehouse-inbound", "api:PickingLists.Scan", "api:WarehouseDocuments.Factory", "api:WarehouseDocuments.Scan" }
            });
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            var persisted = await db.RolePermissionRecords.AsNoTracking().Where(p => p.RoleId == role.Id)
                .Select(p => p.PermissionKey).ToArrayAsync();
            Assert.DoesNotContain("api:PickingLists.Scan", persisted);
            Assert.Contains("api:WarehouseDocuments.Factory", persisted);
            Assert.Contains("api:WarehouseDocuments.Scan", persisted);
            client.DefaultRequestHeaders.Authorization = sameUserToken;
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.PostAsJsonAsync("/api/v1/picking-lists/PK-RBAC/scans", new { })).StatusCode);

            await service.UpdateRoleAsync(role.Id, new(role.Name, null, SelectedScreenCodes: ["warehouse-inbound"]), actor, default);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/products", new { })).StatusCode);

            await service.UpdateRoleAsync(role.Id, new(role.Name, null, []), actor, default);
            Assert.Empty(await identities.GetUserPermissionsAsync(user.Id, default));
            await service.ListRolesAsync(null, default);
            Assert.Empty(await identities.GetUserPermissionsAsync(user.Id, default));
            await service.UpdateRoleAsync(role.Id, new(role.Name, null, ["batches"]), actor, default);
            var restoredGrants = await identities.GetUserPermissionsAsync(user.Id, default);
            Assert.Equal(new[] { "batches" }, restoredGrants.Where(key => !key.StartsWith("api:")));
            Assert.Contains("api:Inventory.List", restoredGrants);
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
