using FSCM.BuildingBlocks.Application;
using FSCM.BuildingBlocks.Infrastructure.Persistence;
using FSCM.Modules.Performance.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.Modules.Performance.Infrastructure;

public sealed class PerformanceModuleInstaller : IModuleInstaller
{
    public string Name => "Performance";
    public void Register(IServiceCollection s, IConfiguration cfg)
    {
        s.AddDbContext<PerformanceDbContext>(o => o.UseNpgsql(cfg.GetConnectionString("Fscm"), x => x.MigrationsHistoryTable("__migrations", "performance")));
        s.AddScoped<ModuleDbContext>(sp => sp.GetRequiredService<PerformanceDbContext>());
        s.AddScoped<IPerformanceUnitOfWork>(sp => sp.GetRequiredService<PerformanceDbContext>());
        s.AddScoped<IFactRepository, FactRepository>();
        s.AddPerformanceApplication();
    }
}
