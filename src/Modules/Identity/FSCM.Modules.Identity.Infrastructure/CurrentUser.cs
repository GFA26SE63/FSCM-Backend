using System.Security.Claims;
using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Identity.Application;
using FSCM.Modules.Identity.Domain.Users;
using Microsoft.AspNetCore.Http;

namespace FSCM.Modules.Identity.Infrastructure;

/// <summary>
/// ICurrentUser for HTTP requests. <see cref="CurrentUserMiddleware"/> resolves the AppUser once per request
/// (token subject = Entra object id) and stores it in HttpContext.Items; this class only reads it.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    internal const string ItemKey = "fscm.user";
    private AppUser? User => accessor.HttpContext?.Items[ItemKey] as AppUser;
    public string UserId => User?.Id ?? throw new UnauthorizedAccessException("Chưa đăng nhập hoặc tài khoản chưa được cấp quyền trong FSCM.");
    public string FullName => User?.FullName ?? "";
    public Role Role => User is { } u ? Enum.Parse<Role>(u.Role.ToString()) : throw new UnauthorizedAccessException();
    public string? WarehouseCode => User?.WarehouseCode;
    public string? SalesGroupCode => User?.SalesGroupCode;
    public string? RetailerCode => User?.RetailerCode;
}

public sealed class CurrentUserMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, IUserRepository users)
    {
        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            var subject = ctx.User.FindFirstValue("oid") ?? ctx.User.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier") ?? ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (subject is not null && await users.ResolveAsync(subject, ctx.RequestAborted) is { Active: true } u)
            {
                ctx.Items[HttpCurrentUser.ItemKey] = u;
                // The business role lives in the FSCM database, so [Authorize(Roles = ...)] works without Entra app roles.
                ctx.User.AddIdentity(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, u.Role.ToString()) }));
            }
        }
        await next(ctx);
    }
}
