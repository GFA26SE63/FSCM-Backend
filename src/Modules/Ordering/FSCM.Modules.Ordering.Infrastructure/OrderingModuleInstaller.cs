using FSCM.BuildingBlocks.Application;
using FSCM.BuildingBlocks.Infrastructure.Persistence;
using FSCM.Modules.Ordering.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.Modules.Ordering.Infrastructure;

public sealed class OrderingModuleInstaller : IModuleInstaller
{
    public string Name => "Ordering";
    public void Register(IServiceCollection s, IConfiguration cfg)
    {
        s.AddDbContext<OrderingDbContext>(o => o.UseNpgsql(cfg.GetConnectionString("Fscm"), x => x.MigrationsHistoryTable("__migrations", "ordering")));
        s.AddScoped<ModuleDbContext>(sp => sp.GetRequiredService<OrderingDbContext>());
        s.AddScoped<IOrderingUnitOfWork>(sp => sp.GetRequiredService<OrderingDbContext>());
        s.AddScoped<IOrderRepository, OrderRepository>();
        s.AddScoped<IPickingRepository, PickingRepository>();
        s.AddScoped<IComplaintRepository, ComplaintRepository>();
        s.AddScoped<IAllocationSettingsProvider, AllocationSettingsProvider>();
        s.AddOrderingApplication();
    }
}
