using FSCM.BuildingBlocks.Application;

namespace FSCM.Worker;

/// <summary>Same modules as the API host (keep the two lists in sync).</summary>
public static class WorkerModules
{
    public static IReadOnlyList<IModuleInstaller> Installers { get; } = new IModuleInstaller[]
    {
        new FSCM.Modules.Identity.Infrastructure.IdentityModuleInstaller(),
        new FSCM.Modules.Catalog.Infrastructure.CatalogModuleInstaller(),
        new FSCM.Modules.Partners.Infrastructure.PartnersModuleInstaller(),
        new FSCM.Modules.Logistics.Infrastructure.LogisticsModuleInstaller(),
        new FSCM.Modules.Inventory.Infrastructure.InventoryModuleInstaller(),
        new FSCM.Modules.Ordering.Infrastructure.OrderingModuleInstaller(),
        new FSCM.Modules.Pricing.Infrastructure.PricingModuleInstaller(),
        new FSCM.Modules.Finance.Infrastructure.FinanceModuleInstaller(),
        new FSCM.Modules.Loyalty.Infrastructure.LoyaltyModuleInstaller(),
        new FSCM.Modules.Performance.Infrastructure.PerformanceModuleInstaller(),
        new FSCM.Modules.Notifications.Infrastructure.NotificationsModuleInstaller(),
    };
}
