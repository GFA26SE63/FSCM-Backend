using FSCM.BuildingBlocks.Application;
using FSCM.BuildingBlocks.Infrastructure.Persistence;
using FSCM.Modules.Loyalty.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.Modules.Loyalty.Infrastructure;

public sealed class LoyaltyModuleInstaller : IModuleInstaller
{
    public string Name => "Loyalty";
    public void Register(IServiceCollection s, IConfiguration cfg)
    {
        s.AddDbContext<LoyaltyDbContext>(o => o.UseNpgsql(cfg.GetConnectionString("Fscm"), x => x.MigrationsHistoryTable("__migrations", "loyalty")));
        s.AddScoped<ModuleDbContext>(sp => sp.GetRequiredService<LoyaltyDbContext>());
        s.AddScoped<ILoyaltyUnitOfWork>(sp => sp.GetRequiredService<LoyaltyDbContext>());
        s.AddScoped<IProgramRepository, ProgramRepository>();
        s.AddScoped<IPointRepository, PointRepository>();
        s.AddScoped<IGiftRepository, GiftRepository>();
        s.AddLoyaltyApplication();
    }
}
