using System.Security.Claims;
using FSCM.Application.Features.Identity;
using Microsoft.AspNetCore.Authentication;

namespace FSCM.API.Security;

public sealed class ActiveAccountClaimsTransformation(IIdentityStore identityStore) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
            return principal;

        // Entra is used only at the exchange endpoint. Its identity is validated by the
        // dedicated Entra bearer scheme before an FSCM account/JWT exists.
        if (principal.HasClaim(claim => claim.Type == "oid") &&
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out _))
            return principal;

        var rawUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var tokenRole = principal.FindFirstValue(ClaimTypes.Role);
        if (!Guid.TryParse(rawUserId, out var userId) || string.IsNullOrWhiteSpace(tokenRole))
            return Anonymous();

        var user = await identityStore.FindUserByIdAsync(userId, CancellationToken.None);
        return user is not null && user.IsActive && string.Equals(user.Role, tokenRole, StringComparison.Ordinal)
            ? principal
            : Anonymous();
    }

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());
}
