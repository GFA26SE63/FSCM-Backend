using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FSCM.API.Security;

/// <summary>Database screen grants supplement the domain's role and resource-scope rules.</summary>
public sealed class ScreenPermissionFilter(IAuthorizationService authorization,
    FSCM.Application.Features.Roles.IRoleStore roles) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor action ||
            context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            return;

        if (action.ControllerName is "Auth" or "Notifications") return;
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            context.Result = new ChallengeResult();
            return;
        }
        var operation = FSCM.Domain.Authorization.ApiPermissionCatalog.Key(action.ControllerName, action.ActionName);
        if (!FSCM.Domain.Authorization.ApiPermissionCatalog.All.Any(p => p.Key == operation))
        {
            context.Result = new ForbidResult();
            return;
        }
        if (!(await authorization.AuthorizeAsync(context.HttpContext.User, null, "permission:" + operation)).Succeeded)
        {
            context.Result = new ForbidResult();
            return;
        }
        var keys = await roles.GetApiScreenCodesAsync(
            context.HttpContext.User.FindFirstValue(ClaimTypes.Role) ?? "", operation, context.HttpContext.RequestAborted);
        foreach (var key in keys)
        {
            if ((await authorization.AuthorizeAsync(context.HttpContext.User, null, "permission:" + key)).Succeeded)
                return;
        }
        context.Result = new ForbidResult();
    }
}
