using FSCM.Application.Features.EntityQueries;
using FSCM.Application.Features.Identity;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using FSCM.Persistence.EntityQueries;
using Microsoft.EntityFrameworkCore;

namespace FSCM.IntegrationTests.Persistence;

public sealed class EntityQueryScopeTests
{
    [Fact]
    public void Admin_notification_query_is_always_limited_to_the_current_recipient()
    {
        var userId = Guid.NewGuid();
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unused;Username=unused").Options);

        var sql = new EntityQueryService(db)
            .Query<Notification>(new EntityQueryAccess(userId, SystemRoles.Admin, IdentityScope.Empty))
            .ToQueryString();

        Assert.Contains("NOTIFICATION", sql);
        Assert.Contains("UserAccountId", sql);
        Assert.Contains(userId.ToString(), sql);
    }
    [Fact]
    public void Explicit_directory_grants_expand_management_reads_but_do_not_change_notification_scope()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unused;Username=unused").Options);
        var source = new EntityQueryService(db);
        var actor = Guid.NewGuid();
        var scoped = new EntityQueryAccess(actor, SystemRoles.WarehouseKeeper, IdentityScope.Empty, []);
        Assert.Contains("WHERE", source.Query<UserAccount>(scoped).ToQueryString());
        var delegated = scoped with { Permissions = ["users", "api:Users.List"] };
        Assert.DoesNotContain("WHERE", source.Query<UserAccount>(delegated).ToQueryString());
        Assert.Contains(actor.ToString(), source.Query<Notification>(delegated).ToQueryString());
    }
    [Fact]
    public void Warehouse_order_scope_includes_run_allocation_and_shipment_history_with_live_warehouse_assignments()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unused;Username=unused").Options);
        var actor = Guid.NewGuid();
        var sql = FSCM.Persistence.ReportingScopeQueries.WarehouseOrderIds(db, actor).ToQueryString();
        Assert.Contains("SHIPMENT", sql);
        Assert.Contains("ALLOCATION_RUN", sql);
        Assert.Contains("USER_WAREHOUSE_ASSIGNMENT", sql);
        Assert.Contains(actor.ToString(), sql);
    }
}
