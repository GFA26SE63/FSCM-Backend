using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace FSCM.API.Security;

public sealed record PermissionRequirement(string Key) : IAuthorizationRequirement;
public sealed record ScreenResourceRequirement(string Resource) : IAuthorizationRequirement;

public sealed class ScreenResourceAuthorizationHandler : AuthorizationHandler<ScreenResourceRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ScreenResourceRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var role = context.User.FindFirstValue(ClaimTypes.Role);
            var keys = ScreenPermissionMap.Rest(requirement.Resource, "List", role);
            if (role == FSCM.Domain.Authorization.SystemRoles.Admin || keys is null ||
                keys.Any(key => context.User.HasClaim(PermissionAuthorizationHandler.GrantClaim, key)))
                context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}

public sealed class PermissionAuthorizationHandler
    : AuthorizationHandler<PermissionRequirement>
{
    public const string GrantClaim = "fscm:screen-grant";
    public const string GlobalScopeClaim = "fscm:global-administration-scope";

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return Task.CompletedTask;
        if (context.User.FindFirstValue(ClaimTypes.Role) == FSCM.Domain.Authorization.SystemRoles.Admin ||
            context.User.HasClaim(GrantClaim, requirement.Key))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
