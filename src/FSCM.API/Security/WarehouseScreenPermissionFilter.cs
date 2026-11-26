using System.Security.Claims;
using FSCM.Domain.Authorization;
using FSCM.Persistence.DbContexts;
using FSCM.Persistence.WarehouseWork;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace FSCM.API.Security;

/// <summary>Shared warehouse endpoints retain the grant of the actual document.</summary>
public sealed class WarehouseScreenPermissionFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor action || action.ControllerName != "WarehouseDocuments" ||
            context.HttpContext.User.FindFirstValue(ClaimTypes.Role) == SystemRoles.Admin)
        {
            await next();
            return;
        }
        var grants = context.HttpContext.User.FindAll(PermissionAuthorizationHandler.GrantClaim).Select(c => c.Value).ToArray();
        var readOnly = HttpMethods.IsGet(context.HttpContext.Request.Method);
        if (context.ActionArguments.TryGetValue("id", out var rawId) && rawId is Guid id)
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var document = await db.WarehouseDocuments.AsNoTracking().Where(d => d.Id == id)
                .Select(d => new { d.Kind, d.Reason, d.Status }).SingleOrDefaultAsync(context.HttpContext.RequestAborted);
            if (document is not null && !WarehouseScreenAccess.Allows(grants, document.Kind, document.Reason, document.Status, readOnly))
            {
                context.Result = new ForbidResult();
                return;
            }
        }
        if (context.ActionArguments.Values.OfType<CreateWarehouseRequest>().FirstOrDefault() is { } request &&
            !WarehouseScreenAccess.Allows(grants, "Request", request.Reason, "Open", false))
        {
            context.Result = new ForbidResult();
            return;
        }
        var executed = await next();
        if (executed.Result is ObjectResult { Value: WarehouseWorkResult[] items } result)
            result.Value = items.Where(item => WarehouseScreenAccess.Allows(grants,
                item.Document.Kind, item.Document.Reason, item.Document.Status, true)).ToArray();
    }
}
