using System.Security.Claims;
using FSCM.Application.Features.EntityQueries;
using FSCM.Application.Features.Identity;
using FSCM.Application.Features.Payments;
using FSCM.Domain.Authorization;
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

    [Authorize(Roles = [SystemRoles.Retailer])]
    public Task<RetailerReceivablePage> RetailerReceivables(
        RetailerReceivableFilter filter,
        [Service] RetailerReceivableService service,
        [Service] IHttpContextAccessor accessor,
        CancellationToken cancellationToken) =>
        service.ListAsync(CurrentUser.Id(accessor), filter, cancellationToken);

    [Authorize(Roles = [SystemRoles.Retailer])]
    public Task<RetailerReceivableResult> RetailerReceivable(
        Guid orderId,
        [Service] RetailerReceivableService service,
        [Service] IHttpContextAccessor accessor,
        CancellationToken cancellationToken) =>
        service.GetByOrderAsync(CurrentUser.Id(accessor), orderId, cancellationToken);
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
