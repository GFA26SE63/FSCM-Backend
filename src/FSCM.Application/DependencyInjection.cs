using FluentValidation;
using FSCM.Application.Features.AllocationRuns;
using FSCM.Domain.Allocation;
using FSCM.Application.Features.Catalog;
using FSCM.Application.Features.Complaints;
using FSCM.Application.Features.DeliveryEta;
using FSCM.Application.Features.Deliveries;
using FSCM.Application.Features.Identity;
using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.InventoryLocks;
using FSCM.Application.Features.InternalTransfers;
using FSCM.Application.Features.Loyalty;
using FSCM.Application.Features.Maps;
using FSCM.Application.Features.MobileSnapshots;
using FSCM.Application.Features.Notifications;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Payments;
using FSCM.Application.Features.Picking;
using FSCM.Application.Features.Retailers;
using FSCM.Application.Features.Refunds;
using FSCM.Application.Features.Quotes;
using FSCM.Application.Features.QrLabels;
using FSCM.Application.Features.Promotions;
using FSCM.Application.Features.Reports;
using FSCM.Application.Features.ShelfLifeDiscounts;
using FSCM.Application.Features.Shipments;
using FSCM.Application.Features.Territory;
using FSCM.Application.Features.TransitTables;
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
        services.AddSingleton<AllocationEngine>();
        services.AddScoped<AllocationPlanner>();
        services.AddScoped<AllocationPricingService>();
        services.AddScoped<IRealTimeAllocationService, RealTimeAllocationService>();
        services.AddScoped<CatalogService>();
        services.AddScoped<DeliveryService>();
        services.AddScoped<ShipmentService>();
        services.AddScoped<ReportService>();
        services.AddScoped<IdentityService>();
        services.AddScoped<RetailerActivationService>();
        services.AddScoped<InventoryService>();
        services.AddScoped<InventoryLifecycleService>();
        services.AddScoped<InventoryLockService>();
        services.AddScoped<InventoryOutboxDispatcher>();
        services.AddScoped<IInventoryLifecycleService>(provider => provider.GetRequiredService<InventoryLifecycleService>());
        services.AddScoped<LoyaltyService>();
        services.AddScoped<RewardService>();
        services.AddScoped<ComplaintService>();
        services.AddScoped<RedemptionService>();
        services.AddScoped<RedemptionWorkflowService>();
        services.AddScoped<LoyaltyCardService>();
        services.AddScoped<LoyaltyAccrualService>();
        services.AddScoped<OrderCompletionService>();
        services.AddScoped<MapService>();
        services.AddScoped<MobileSnapshotService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<OrderService>();
        services.AddScoped<OrderReleaseService>();
        services.AddScoped<IOrderPricingService, OrderPricingService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<PaymentReceiptService>();
        services.AddScoped<RetailerReceivableService>();
        services.AddScoped<PaymentPlanService>();
        services.AddScoped<QuoteService>();
        services.AddScoped<IPaymentPlanService>(provider => provider.GetRequiredService<PaymentPlanService>());
        services.AddScoped<PickingPlanService>();
        services.AddScoped<IPickingPlanService>(provider => provider.GetRequiredService<PickingPlanService>());
        services.AddScoped<QrLabelService>();
        services.AddScoped<PromotionService>();
        services.AddScoped<RetailerService>();
        services.AddScoped<RefundService>();
        services.AddScoped<TerritoryService>();
        services.AddScoped<ShelfLifeDiscountService>();
        services.AddScoped<TransitTableService>();
        services.AddScoped<DeliveryEtaService>();
        services.AddScoped<WarehouseConfigurationService>();

        return services;
    }
}
