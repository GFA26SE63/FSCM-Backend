using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace FSCM.API.Security;

public sealed record PermissionRequirement(string Key) : IAuthorizationRequirement;
public sealed record OperationRequirement(string Controller, string Action) : IAuthorizationRequirement;

public sealed class OperationAuthorizationHandler(IServiceProvider services) : AuthorizationHandler<OperationRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, OperationRequirement requirement)
    {
        var access = services.GetRequiredService<OperationAccess>();
        if (await access.AllowsAsync(context.User, requirement.Controller, requirement.Action,
                services.GetRequiredService<IHttpContextAccessor>().HttpContext?.RequestAborted ?? default))
            context.Succeed(requirement);
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
