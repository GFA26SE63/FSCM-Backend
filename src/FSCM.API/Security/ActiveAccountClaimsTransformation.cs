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

        // Admin remains the actor role (first claim) for auditing and scoped
        // service rules, but also satisfies every role-based endpoint policy.
        if (user.Role == SystemRoles.Admin)
        {
            var delegatedRoles = new ClaimsIdentity();
            foreach (var role in SystemRoles.All.Where(role => role != SystemRoles.Admin))
                delegatedRoles.AddClaim(new Claim(ClaimTypes.Role, role));
            principal.AddIdentity(delegatedRoles);
        }

        return principal;
    }

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());
}
