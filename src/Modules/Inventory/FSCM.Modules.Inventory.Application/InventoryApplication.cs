using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Inventory.Application.Rotation;
using FSCM.Modules.Inventory.Application.Stock;
using FSCM.Modules.Inventory.Contracts;
using FSCM.Modules.Ordering.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.Modules.Inventory.Application;

public static class InventoryApplication
{
    public static IServiceCollection AddInventoryApplication(this IServiceCollection s) => s
        .AddScoped<IInventoryModule, InventoryModule>()
        .AddScoped<WarehouseService>().AddScoped<BatchService>().AddScoped<GoodsReceiptService>().AddScoped<LabelService>()
        .AddScoped<TransferService>().AddScoped<DisposalService>().AddScoped<RotationService>()
        .AddScoped<RotationJob>().AddScoped<IScheduledJob>(sp => sp.GetRequiredService<RotationJob>())
        .AddScoped<IScheduledJob, DateTrackerJob>()
        .AddScoped<IIntegrationEventHandler<OrderApproved>, OrderApprovedHandler>()
        .AddScoped<IIntegrationEventHandler<OrderCancelled>, OrderCancelledHandler>()
        .AddScoped<IIntegrationEventHandler<OrderRejected>, OrderRejectedHandler>();
}
