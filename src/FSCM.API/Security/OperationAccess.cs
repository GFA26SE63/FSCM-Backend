using System.Security.Claims;
using FSCM.Application.Features.Roles;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using Microsoft.AspNetCore.Authorization;

namespace FSCM.API.Security;

/// <summary>One runtime authority for REST and GraphQL: live API grant plus a persisted screen mapping.</summary>
public sealed class OperationAccess(IAuthorizationService authorization, IRoleStore roles)
{
    private readonly object mappingLock = new();
    private Task<IReadOnlyList<ScreenApiMapping>>? mappingTask;

    public async Task<bool> AllowsAsync(ClaimsPrincipal user, string controller, string action, CancellationToken ct)
    {
        if (user.Identity?.IsAuthenticated != true) return false;
        var operation = ApiPermissionCatalog.Key(controller, action);
        if (!ApiPermissionCatalog.All.Any(p => p.Key == operation)) return false;
        // Admin owns every registered system operation, including operations whose
        // delegated screen mapping is absent. Authentication/catalog checks remain.
        if (user.FindFirstValue(ClaimTypes.Role) == SystemRoles.Admin) return true;
        if (!(await authorization.AuthorizeAsync(user, null, "permission:" + operation)).Succeeded) return false;
        Task<IReadOnlyList<ScreenApiMapping>> pending;
        lock (mappingLock)
            pending = mappingTask ??= roles.GetScreenApiMappingsAsync(user.FindFirstValue(ClaimTypes.Role) ?? "", ct);
        var screens = (await pending).Where(m => m.ApiCode == operation).Select(m => m.ScreenCode);
        foreach (var screen in screens)
            if ((await authorization.AuthorizeAsync(user, null, "permission:" + screen)).Succeeded) return true;
        return false;
    }
}
