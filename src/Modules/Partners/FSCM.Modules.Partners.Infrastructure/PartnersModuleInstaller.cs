using FSCM.BuildingBlocks.Application;
using FSCM.BuildingBlocks.Infrastructure.Persistence;
using FSCM.Modules.Partners.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.Modules.Partners.Infrastructure;

public sealed class PartnersModuleInstaller : IModuleInstaller
{
    public string Name => "Partners";
    public void Register(IServiceCollection s, IConfiguration cfg)
    {
        s.AddDbContext<PartnersDbContext>(o => o.UseNpgsql(cfg.GetConnectionString("Fscm"), x => x.MigrationsHistoryTable("__migrations", "partners")));
        s.AddScoped<ModuleDbContext>(sp => sp.GetRequiredService<PartnersDbContext>());
        s.AddScoped<IPartnersUnitOfWork>(sp => sp.GetRequiredService<PartnersDbContext>());
        s.AddScoped<IAreaRepository, AreaRepository>();
        s.AddScoped<IRetailerRepository, RetailerRepository>();
        s.AddPartnersApplication();
    }
}
