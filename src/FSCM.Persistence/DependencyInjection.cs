using FSCM.Application.Features.Catalog;
using FSCM.Application.Features.Identity;
using FSCM.Application.Features.Territory;
using FSCM.Application.Features.WarehouseConfiguration;
using FSCM.Persistence.Catalog;
using FSCM.Persistence.DbContexts;
using FSCM.Persistence.Identity;
using FSCM.Persistence.Seeding;
using FSCM.Persistence.Territory;
using FSCM.Persistence.WarehouseConfiguration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistenceServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseNpgsql(
                    connectionString,
                    postgresOptions => postgresOptions.EnableRetryOnFailure());
            });
        }

        services.AddScoped<ICatalogStore, CatalogStore>();
        services.AddScoped<IIdentityStore, IdentityStore>();
        services.AddScoped<ITerritoryStore, TerritoryStore>();
        services.AddScoped<IWarehouseConfigurationStore, WarehouseConfigurationStore>();
        services.AddScoped<DevelopmentDataSeeder>();

        return services;
    }
}
