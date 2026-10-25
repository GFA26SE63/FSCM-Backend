using FSCM.Domain.Entities;

namespace FSCM.Application.Features.WarehouseConfiguration;

public sealed record WarehouseView(Warehouse Warehouse, MapLocation? Location);
public sealed record WarehouseKeeperView(Guid WarehouseId, Guid UserId, string FullName, string Email);
public sealed record MlorView(
    RetailerProductMlor Mlor,
    string RetailerCode,
    string RetailerName,
    string? CategoryCode,
    string? CategoryName,
    string? ProductSku,
    string? ProductName);

public interface IWarehouseConfigurationStore
{
    Task<IReadOnlyCollection<WarehouseView>> ListWarehousesAsync(bool includeInactive, CancellationToken cancellationToken);
    Task<WarehouseView?> FindWarehouseViewAsync(Guid warehouseId, CancellationToken cancellationToken);
    Task<Warehouse?> FindWarehouseAsync(Guid warehouseId, CancellationToken cancellationToken);
    Task<Warehouse?> FindWarehouseByCodeAsync(string code, CancellationToken cancellationToken);
    Task<MapLocation?> FindWarehouseLocationAsync(Guid warehouseId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<WarehouseKeeperView>> ListActiveWarehouseKeepersAsync(
        IReadOnlyCollection<Guid> warehouseIds, CancellationToken cancellationToken);
    void AddWarehouse(Warehouse warehouse);
    void AddLocation(MapLocation location);

    Task<IReadOnlyCollection<TransitTimeRule>> ListTransitRulesAsync(Guid warehouseId, bool includeInactive, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<TransitTimeRule>> ListTransitRulesAsync(
        IReadOnlyCollection<Guid> warehouseIds, bool includeInactive, CancellationToken cancellationToken);
    void AddTransitRules(IEnumerable<TransitTimeRule> rules);

    Task<IReadOnlyCollection<MlorView>> ListMlorAsync(
        Guid? retailerId, Guid? productCategoryId, Guid? productId,
        bool includeInactive, CancellationToken cancellationToken);
    Task<RetailerProductMlor?> FindActiveMlorAsync(
        Guid retailerId, Guid? productCategoryId, Guid? productId, CancellationToken cancellationToken);
    Task<RetailerProductMlor?> FindMlorAsync(Guid mlorId, CancellationToken cancellationToken);
    Task<Retailer?> FindRetailerAsync(Guid retailerId, CancellationToken cancellationToken);
    Task<Product?> FindProductAsync(Guid productId, CancellationToken cancellationToken);
    Task<ProductCategory?> FindCategoryAsync(Guid categoryId, CancellationToken cancellationToken);
    void AddMlor(RetailerProductMlor mlor);
    void AddAuditLog(AuditLog auditLog);

    Task<IReadOnlyCollection<SystemConfiguration>> ListCurrentConfigurationsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<SystemConfiguration>> ListConfigurationHistoryAsync(string key, CancellationToken cancellationToken);
    Task<SystemConfiguration?> FindCurrentConfigurationAsync(string key, CancellationToken cancellationToken);
    void AddConfiguration(SystemConfiguration configuration);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
