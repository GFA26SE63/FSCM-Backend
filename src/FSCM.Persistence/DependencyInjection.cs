using FSCM.Application.Features.AllocationRuns;
using FSCM.Application.Features.Catalog;
using FSCM.Application.Features.Complaints;
using FSCM.Application.Features.DeliveryEta;
using FSCM.Application.Features.Deliveries;
using FSCM.Application.Features.EntityQueries;
using FSCM.Application.Features.Identity;
using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.InventoryLocks;
using FSCM.Application.Features.InternalTransfers;
using FSCM.Application.Features.Loyalty;
using FSCM.Application.Features.ManualPayments;
using FSCM.Application.Features.Maps;
using FSCM.Application.Features.MobileSnapshots;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Payments;
using FSCM.Application.Features.Picking;
using FSCM.Application.Features.Retailers;
using FSCM.Application.Features.Refunds;
using FSCM.Application.Features.QrLabels;
using FSCM.Application.Features.Promotions;
using FSCM.Application.Features.Reports;
using FSCM.Application.Features.ShelfLifeDiscounts;
using FSCM.Application.Features.Shipments;
using FSCM.Application.Features.Territory;
using FSCM.Application.Features.TransitTables;
using FSCM.Application.Features.WarehouseConfiguration;
using FSCM.Persistence.Complaints;
using FSCM.Persistence.AllocationRuns;
using FSCM.Persistence.Catalog;
using FSCM.Persistence.DeliveryEta;
using FSCM.Persistence.DbContexts;
using FSCM.Persistence.Deliveries;
using FSCM.Persistence.EntityQueries;
using FSCM.Persistence.Identity;
using FSCM.Persistence.Inventory;
using FSCM.Persistence.InventoryLocks;
using FSCM.Persistence.InternalTransfers;
using FSCM.Persistence.Loyalty;
using FSCM.Persistence.ManualPayments;
using FSCM.Persistence.Maps;
using FSCM.Persistence.MobileSnapshots;
using FSCM.Persistence.Orders;
using FSCM.Persistence.Payments;
using FSCM.Persistence.Picking;
using FSCM.Persistence.Retailers;
using FSCM.Persistence.Refunds;
using FSCM.Persistence.QrLabels;
using FSCM.Persistence.Promotions;
using FSCM.Persistence.Reports;
using FSCM.Persistence.Seeding;
using FSCM.Persistence.Shipments;
using FSCM.Persistence.ShelfLifeDiscounts;
using FSCM.Persistence.Territory;
using FSCM.Persistence.TransitTables;
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

        services.AddScoped<IAllocationRunStore, AllocationRunStore>();
        services.AddScoped<IEntityQueryService, EntityQueryService>();
        services.AddScoped<ICatalogStore, CatalogStore>();
        services.AddScoped<IDeliveryStore, DeliveryStore>();
        services.AddScoped<IShipmentStore, ShipmentStore>();
        services.AddScoped<IReportStore, ReportStore>();
        services.AddScoped<IIdentityStore, IdentityStore>();
        services.AddScoped<IInventoryStore, InventoryStore>();
        services.AddScoped<IInventoryLifecycleStore, InventoryStore>();
        services.AddScoped<IInventoryOutboxStore, InventoryOutboxStore>();
        services.AddScoped<IInventoryLockStore, InventoryLockStore>();
        services.AddScoped<IInternalTransferStore, InternalTransferStore>();
        services.AddScoped<IManualPaymentStore, ManualPaymentStore>();
        services.AddScoped<ILoyaltyStore, LoyaltyStore>();
        services.AddScoped<IRewardStore, RewardStore>();
    services.AddScoped<IRedemptionStore, RedemptionStore>();
    services.AddScoped<IComplaintStore, ComplaintStore>();
        services.AddScoped<IRedemptionWorkflowStore, RedemptionStore>();
        services.AddScoped<ILoyaltyCardStore, LoyaltyCardStore>();
        services.AddScoped<IMapStore, MapStore>();
        services.AddScoped<IMobileSnapshotStore, MobileSnapshotStore>();
        services.AddScoped<IOrderStore, OrderStore>();
        services.AddScoped<IPaymentStore, PaymentStore>();
        services.AddScoped<IPickingStore, PickingStore>();
        services.AddScoped<IRunPickingStore, RunPickingStore>();
        services.AddScoped<IQrLabelStore, QrLabelStore>();
        services.AddScoped<IPromotionStore, PromotionStore>();
        services.AddScoped<IRetailerStore, RetailerStore>();
        services.AddScoped<IRefundStore, RefundStore>();
        services.AddScoped<ITerritoryStore, TerritoryStore>();
        services.AddScoped<IShelfLifeDiscountStore, ShelfLifeDiscountStore>();
        services.AddScoped<ITransitTableStore, TransitTableStore>();
        services.AddScoped<IDeliveryEtaStore, DeliveryEtaStore>();
        services.AddScoped<IWarehouseConfigurationStore, WarehouseConfigurationStore>();
        services.AddScoped<DevelopmentDataSeeder>();
        services.AddScoped<AdminBootstrapper>();

        return services;
    }
}
