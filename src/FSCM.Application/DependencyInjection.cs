using FluentValidation;
using FSCM.Application.Features.Catalog;
using FSCM.Application.Features.Identity;
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
        services.AddScoped<IdentityService>();
        services.AddScoped<TerritoryService>();
        services.AddScoped<WarehouseConfigurationService>();

        return services;
    }
}
