using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
    public void Every_mapped_graphql_entity_connection_has_ordering_before_offset_paging()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unused;Username=unused")
            .ConfigureWarnings(warnings => warnings.Throw(CoreEventId.RowLimitingOperationWithoutOrderByWarning)).Options);
        var source = new EntityQueryService(db);
        var access = new EntityQueryAccess(Guid.NewGuid(), SystemRoles.Admin, IdentityScope.Empty);
        var queryMethod = typeof(EntityQueryService).GetMethod(nameof(EntityQueryService.Query))!;
        var connections = typeof(FSCM.API.GraphQL.EntityQuery).GetMethods()
            .Where(method => method.ReturnType.IsGenericType &&
                method.ReturnType.GetGenericTypeDefinition() == typeof(IQueryable<>) &&
                // Archived Review-2 models are deliberately ignored by AppDbContext.
                db.Model.FindEntityType(method.ReturnType.GetGenericArguments()[0]) is not null).ToArray();
        Assert.NotEmpty(connections);
        foreach (var connection in connections)
        {
            var entity = connection.ReturnType.GetGenericArguments()[0];
            var query = (IQueryable)queryMethod.MakeGenericMethod(entity).Invoke(source, [access])!;
            var skip = Expression.Call(typeof(Queryable), nameof(Queryable.Skip), [entity], query.Expression, Expression.Constant(1));
            var take = Expression.Call(typeof(Queryable), nameof(Queryable.Take), [entity], skip, Expression.Constant(2));
            var sql = query.Provider.CreateQuery(take).ToQueryString();
            Assert.Contains("ORDER BY", sql);
        }
    }

    [Fact]
    public void Composite_primary_keys_and_explicit_client_sorting_remain_supported()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unused;Username=unused")
            .ConfigureWarnings(warnings => warnings.Throw(CoreEventId.RowLimitingOperationWithoutOrderByWarning)).Options);
        var source = new EntityQueryService(db);
        var access = new EntityQueryAccess(Guid.NewGuid(), SystemRoles.Admin, IdentityScope.Empty);
        var composite = source.Query<ScreenApiMapping>(access).Take(2).ToQueryString();
        var order = composite[composite.LastIndexOf("ORDER BY", StringComparison.Ordinal)..];
        Assert.Contains("BusinessRole", order);
        Assert.Contains("ScreenCode", order);
        Assert.Contains("ApiCode", order);

        var custom = source.Query<Warehouse>(access).OrderBy(w => w.Name).ThenBy(w => w.Id).Take(2).ToQueryString();
        order = custom[custom.LastIndexOf("ORDER BY", StringComparison.Ordinal)..];
        Assert.True(order.IndexOf("Name", StringComparison.Ordinal) < order.IndexOf("Id", StringComparison.Ordinal));
    }

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
