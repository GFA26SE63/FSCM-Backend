using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FSCM.API.Security;

/// <summary>Live screen and operation grants authorize the action; services retain resource scope rules.</summary>
public sealed class ScreenPermissionFilter(OperationAccess access) : IAsyncAuthorizationFilter
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
        if (!await access.AllowsAsync(context.HttpContext.User, action.ControllerName, action.ActionName,
                context.HttpContext.RequestAborted)) context.Result = new ForbidResult();
    }
}
