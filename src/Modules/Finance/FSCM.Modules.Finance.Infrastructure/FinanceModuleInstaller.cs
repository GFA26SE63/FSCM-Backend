using FSCM.BuildingBlocks.Application;
using FSCM.BuildingBlocks.Infrastructure.Persistence;
using FSCM.Modules.Finance.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.Modules.Finance.Infrastructure;

public sealed class FinanceModuleInstaller : IModuleInstaller
{
    public string Name => "Finance";
    public void Register(IServiceCollection s, IConfiguration cfg)
    {
        s.AddDbContext<FinanceDbContext>(o => o.UseNpgsql(cfg.GetConnectionString("Fscm"), x => x.MigrationsHistoryTable("__migrations", "finance")));
        s.AddScoped<ModuleDbContext>(sp => sp.GetRequiredService<FinanceDbContext>());
        s.AddScoped<IFinanceUnitOfWork>(sp => sp.GetRequiredService<FinanceDbContext>());
        s.AddScoped<IReceivableRepository, ReceivableRepository>();
        s.AddFinanceApplication();
    }
}
