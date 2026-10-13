using FSCM.BuildingBlocks.Application;
using FSCM.BuildingBlocks.Infrastructure.Persistence;
using FSCM.Modules.Identity.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.Modules.Identity.Infrastructure;

public sealed class IdentityModuleInstaller : IModuleInstaller
{
    public string Name => "Identity";
    public void Register(IServiceCollection s, IConfiguration cfg)
    {
        s.AddDbContext<IdentityDbContext>(o => o.UseNpgsql(cfg.GetConnectionString("Fscm"), x => x.MigrationsHistoryTable("__migrations", "identity")));
        s.AddScoped<ModuleDbContext>(sp => sp.GetRequiredService<IdentityDbContext>());
        s.AddScoped<IIdentityUnitOfWork>(sp => sp.GetRequiredService<IdentityDbContext>());
        s.AddScoped<IUserRepository, UserRepository>();
        s.AddHttpContextAccessor();
        s.AddScoped<ICurrentUser, HttpCurrentUser>();   // the Worker host replaces this with SystemCurrentUser
        s.AddIdentityApplication();
    }
}

public static class IdentityApplicationBuilderExtensions
{
    /// <summary>Call after UseAuthentication().</summary>
    public static IApplicationBuilder UseFscmCurrentUser(this IApplicationBuilder app) => app.UseMiddleware<CurrentUserMiddleware>();
}
