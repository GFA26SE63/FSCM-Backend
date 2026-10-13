using System.Reflection;
using FSCM.BuildingBlocks.Application;

namespace FSCM.Api;

/// <summary>
/// The single place where the modular monolith is assembled. Adding a module = add its two project references
/// (Infrastructure + Api) to FSCM.Api.csproj and one line here. FSCM.Worker uses the same list of installers.
/// </summary>
public static class ModuleCatalog
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

    /// <summary>Assemblies that contain the controllers of each module (FSCM.Modules.*.Api).</summary>
    public static IEnumerable<Assembly> ApiAssemblies() =>
        new[] { "FSCM.Modules.Identity.Api", "FSCM.Modules.Catalog.Api", "FSCM.Modules.Partners.Api", "FSCM.Modules.Logistics.Api", "FSCM.Modules.Inventory.Api", "FSCM.Modules.Ordering.Api", "FSCM.Modules.Pricing.Api", "FSCM.Modules.Finance.Api", "FSCM.Modules.Loyalty.Api", "FSCM.Modules.Performance.Api", "FSCM.Modules.Notifications.Api" }.Select(n => Assembly.Load(new AssemblyName(n)));
}
