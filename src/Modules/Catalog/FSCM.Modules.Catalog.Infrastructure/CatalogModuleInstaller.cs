using FSCM.BuildingBlocks.Application;
using FSCM.BuildingBlocks.Infrastructure.Persistence;
using FSCM.Modules.Catalog.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.Modules.Catalog.Infrastructure;

public sealed class CatalogModuleInstaller : IModuleInstaller
{
    public string Name => "Catalog";
    public void Register(IServiceCollection s, IConfiguration cfg)
    {
        s.AddDbContext<CatalogDbContext>(o => o.UseNpgsql(cfg.GetConnectionString("Fscm"), x => x.MigrationsHistoryTable("__migrations", "catalog")));
        s.AddScoped<ModuleDbContext>(sp => sp.GetRequiredService<CatalogDbContext>());
        s.AddScoped<ICatalogUnitOfWork>(sp => sp.GetRequiredService<CatalogDbContext>());
        s.AddScoped<IProductRepository, ProductRepository>();
        s.AddScoped<ICategoryRepository, CategoryRepository>();
        s.AddCatalogApplication();
    }
}
