using FSCM.BuildingBlocks.Application;
using FSCM.BuildingBlocks.Infrastructure.Persistence;
using FSCM.Modules.Pricing.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.Modules.Pricing.Infrastructure;

public sealed class PricingModuleInstaller : IModuleInstaller
{
    public string Name => "Pricing";
    public void Register(IServiceCollection s, IConfiguration cfg)
    {
        s.AddDbContext<PricingDbContext>(o => o.UseNpgsql(cfg.GetConnectionString("Fscm"), x => x.MigrationsHistoryTable("__migrations", "pricing")));
        s.AddScoped<ModuleDbContext>(sp => sp.GetRequiredService<PricingDbContext>());
        s.AddScoped<IPricingUnitOfWork>(sp => sp.GetRequiredService<PricingDbContext>());
        s.AddScoped<IPromotionRepository, PromotionRepository>();
        s.AddScoped<IBatchDiscountRepository, BatchDiscountRepository>();
        s.AddPricingApplication();
    }
}
