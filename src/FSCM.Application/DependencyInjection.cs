using FluentValidation;
using FSCM.Application.Features.Catalog;
using FSCM.Application.Features.Deliveries;
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
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddAutoMapper(_ => { }, typeof(DependencyInjection).Assembly);
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<CatalogService>();
        services.AddScoped<DeliveryService>();
        services.AddScoped<IdentityService>();
        services.AddScoped<InventoryService>();
        services.AddScoped<InventoryLifecycleService>();
        services.AddScoped<IInventoryLifecycleService>(provider => provider.GetRequiredService<InventoryLifecycleService>());
        services.AddScoped<InternalTransferService>();
        services.AddScoped<OrderService>();
        services.AddScoped<IOrderPricingService, OrderPricingService>();
        services.AddScoped<PickingService>();
        services.AddScoped<PickingPlanService>();
        services.AddScoped<IPickingPlanService>(provider => provider.GetRequiredService<PickingPlanService>());
        services.AddScoped<QrLabelService>();
        services.AddScoped<PromotionService>();
        services.AddScoped<RetailerService>();
        services.AddScoped<TerritoryService>();
        services.AddScoped<WarehouseConfigurationService>();

        return services;
    }
}
