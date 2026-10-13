using FSCM.BuildingBlocks.Application;
using FSCM.BuildingBlocks.Infrastructure.Persistence;
using FSCM.Modules.Logistics.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.Modules.Logistics.Infrastructure;

public sealed class LogisticsModuleInstaller : IModuleInstaller
{
    public string Name => "Logistics";
    public void Register(IServiceCollection s, IConfiguration cfg)
    {
        s.AddDbContext<LogisticsDbContext>(o => o.UseNpgsql(cfg.GetConnectionString("Fscm"), x => x.MigrationsHistoryTable("__migrations", "logistics")));
        s.AddScoped<ModuleDbContext>(sp => sp.GetRequiredService<LogisticsDbContext>());
        s.AddScoped<ITransitRuleRepository, TransitRuleRepository>();
        s.AddScoped<IDistanceRepository, DistanceRepository>();
        s.AddScoped<IFleetRepository, FleetRepository>();
        s.AddScoped<ILogisticsUnitOfWork>(sp => sp.GetRequiredService<LogisticsDbContext>());
        s.AddLogisticsApplication();
    }
}
