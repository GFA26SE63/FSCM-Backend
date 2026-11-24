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
}
