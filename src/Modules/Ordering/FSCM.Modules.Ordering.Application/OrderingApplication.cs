using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Finance.Contracts;
using FSCM.Modules.Ordering.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.Modules.Ordering.Application;

public static class OrderingApplication
{
    public static IServiceCollection AddOrderingApplication(this IServiceCollection s) => s
        .AddScoped<IOrderingModule, OrderingModule>()
        .AddScoped<OrderPlacementService>().AddScoped<OrderWorkflowService>().AddScoped<OrderQueryService>()
        .AddScoped<PickingService>().AddScoped<ComplaintService>().AddScoped<AllocationSettingsService>().AddScoped<SyncSnapshotService>()
        .AddScoped<IIntegrationEventHandler<CreditOrderPaid>, CreditOrderPaidHandler>();
}
