using FSCM.Application.Features.Catalog;
using FSCM.Application.Features.Identity;
using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.InternalTransfers;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Picking;
using FSCM.Application.Features.Retailers;
using FSCM.Application.Features.QrLabels;
using FSCM.Application.Features.Promotions;
using FSCM.Application.Features.Territory;
using FSCM.Application.Features.WarehouseConfiguration;
using FSCM.Persistence.Catalog;
using FSCM.Persistence.DbContexts;
using FSCM.Persistence.Identity;
using FSCM.Persistence.Inventory;
using FSCM.Persistence.InternalTransfers;
using FSCM.Persistence.Orders;
using FSCM.Persistence.Picking;
using FSCM.Persistence.Retailers;
using FSCM.Persistence.QrLabels;
using FSCM.Persistence.Promotions;
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
        services.AddScoped<IInventoryStore, InventoryStore>();
        services.AddScoped<IInventoryLifecycleStore, InventoryStore>();
        services.AddScoped<IInternalTransferStore, InternalTransferStore>();
        services.AddScoped<IOrderStore, OrderStore>();
        services.AddScoped<IPickingStore, PickingStore>();
        services.AddScoped<IQrLabelStore, QrLabelStore>();
        services.AddScoped<IPromotionStore, PromotionStore>();
        services.AddScoped<IRetailerStore, RetailerStore>();
        services.AddScoped<ITerritoryStore, TerritoryStore>();
        services.AddScoped<IWarehouseConfigurationStore, WarehouseConfigurationStore>();
        services.AddScoped<DevelopmentDataSeeder>();

        return services;
    }
}
