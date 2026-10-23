using System.Security.Claims;
using FSCM.Application.Features.EntityQueries;
using FSCM.Application.Features.Identity;
using HotChocolate.Authorization;

namespace FSCM.API.GraphQL;

public sealed class SystemQuery
{
    [Authorize]
    public async Task<EntityQuery> Entities(
        [Service] IdentityService identityService,
        [Service] IHttpContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var current = await identityService.GetCurrentUserAsync(
            CurrentUser.Id(accessor),
            cancellationToken);

        return new EntityQuery(
            new EntityQueryAccess(current.Id, current.Role, current.Scope));
    }
}

internal static class CurrentUser
{
    public static Guid Id(IHttpContextAccessor accessor)
    {
        var principal = accessor.HttpContext?.User
            ?? throw IdentityOperationException.InvalidToken();
        var rawId = principal.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(rawId, out var id)
            ? id
            : throw IdentityOperationException.InvalidToken();
    }
}
