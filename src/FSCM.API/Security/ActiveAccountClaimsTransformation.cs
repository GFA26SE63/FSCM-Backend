using System.Security.Claims;
using FSCM.Application.Features.Identity;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authentication;

namespace FSCM.API.Security;

public sealed class ActiveAccountClaimsTransformation(IIdentityStore identityStore) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
            return principal;

        // Google is used only at the exchange endpoint. Its identity is validated by the
        // dedicated Google bearer scheme before an FSCM account/JWT exists.
        if (principal.HasClaim(claim => claim.Type == "sub") &&
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out _))
            return principal;

        var rawUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var tokenRole = principal.FindFirstValue(ClaimTypes.Role);
        if (!Guid.TryParse(rawUserId, out var userId) || string.IsNullOrWhiteSpace(tokenRole))
            return Anonymous();

        var user = await identityStore.FindUserByIdAsync(userId, CancellationToken.None);
        if (user is null || !user.IsActive || !string.Equals(user.Role, tokenRole, StringComparison.Ordinal))
            return Anonymous();

        // Rebuild grants from the database on every HTTP authentication, never from JWT grants.
        principal = new ClaimsPrincipal(principal.Identities.Select(identity => new ClaimsIdentity(
            identity.Claims.Where(claim => claim.Type != PermissionAuthorizationHandler.GrantClaim &&
                claim.Type != PermissionAuthorizationHandler.GlobalScopeClaim),
            identity.AuthenticationType, identity.NameClaimType, identity.RoleClaimType)));
        var grants = new ClaimsIdentity();
        if (user.Role == SystemRoles.Admin)
            grants.AddClaim(new Claim(PermissionAuthorizationHandler.GlobalScopeClaim, "true"));
        foreach (var permission in await identityStore.GetUserPermissionsAsync(user.Id, CancellationToken.None))
            grants.AddClaim(new Claim(PermissionAuthorizationHandler.GrantClaim, permission));
        principal.AddIdentity(grants);

        return principal;
    }

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());
}
