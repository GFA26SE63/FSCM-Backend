using FSCM.Modules.Logistics.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.Modules.Logistics.Application;

public static class LogisticsApplication
{
    public static IServiceCollection AddLogisticsApplication(this IServiceCollection s) => s
        .AddScoped<ILogisticsModule, LogisticsModule>()
        .AddScoped<TransitTableService>()
        .AddScoped<FleetService>();
}
