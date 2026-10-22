using FSCM.Application.Features.WarehouseConfiguration;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.WarehouseConfiguration;

public sealed class WarehouseConfigurationStore(AppDbContext dbContext) : IWarehouseConfigurationStore
{
    public async Task<IReadOnlyCollection<WarehouseView>> ListWarehousesAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        var warehouses = dbContext.WarehouseRecords.AsNoTracking().AsQueryable();
        if (!includeInactive) warehouses = warehouses.Where(item => item.IsActive);
        return await CreateWarehouseViewQuery(warehouses.OrderBy(item => item.Code)).ToArrayAsync(cancellationToken);
    }

    public Task<WarehouseView?> FindWarehouseViewAsync(Guid warehouseId, CancellationToken cancellationToken) =>
        CreateWarehouseViewQuery(dbContext.WarehouseRecords.AsNoTracking().Where(item => item.Id == warehouseId))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<Warehouse?> FindWarehouseAsync(Guid warehouseId, CancellationToken cancellationToken) =>
        dbContext.WarehouseRecords.SingleOrDefaultAsync(item => item.Id == warehouseId, cancellationToken);

    public Task<Warehouse?> FindWarehouseByCodeAsync(string code, CancellationToken cancellationToken) =>
        dbContext.WarehouseRecords.SingleOrDefaultAsync(item => item.Code == code, cancellationToken);

    public Task<MapLocation?> FindWarehouseLocationAsync(Guid warehouseId, CancellationToken cancellationToken) =>
        dbContext.MapLocationRecords.SingleOrDefaultAsync(item => item.WarehouseId == warehouseId, cancellationToken);

    public void AddWarehouse(Warehouse warehouse) => dbContext.WarehouseRecords.Add(warehouse);
    public void AddLocation(MapLocation location) => dbContext.MapLocationRecords.Add(location);

    public async Task<IReadOnlyCollection<TransitTimeRule>> ListTransitRulesAsync(
        Guid warehouseId, bool includeInactive, CancellationToken cancellationToken)
    {
        var query = dbContext.TransitTimeRuleRecords.Where(item => item.WarehouseId == warehouseId);
        if (!includeInactive) query = query.Where(item => item.IsActive);
        return await query.OrderBy(item => item.MinDistanceMeters).ThenByDescending(item => item.CreatedAt).ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<TransitTimeRule>> ListTransitRulesAsync(
        IReadOnlyCollection<Guid> warehouseIds, bool includeInactive, CancellationToken cancellationToken)
    {
        var query = dbContext.TransitTimeRuleRecords.AsNoTracking()
            .Where(item => warehouseIds.Contains(item.WarehouseId));
        if (!includeInactive) query = query.Where(item => item.IsActive);
        return await query.OrderBy(item => item.WarehouseId)
            .ThenBy(item => item.MinDistanceMeters).ToArrayAsync(cancellationToken);
    }

    public void AddTransitRules(IEnumerable<TransitTimeRule> rules) => dbContext.TransitTimeRuleRecords.AddRange(rules);

    public async Task<IReadOnlyCollection<MlorView>> ListMlorAsync(
        Guid? retailerId, Guid? productCategoryId, Guid? productId,
        bool includeInactive, CancellationToken cancellationToken)
    {
        var query = dbContext.RetailerProductMlorRecords.AsNoTracking().AsQueryable();
        if (retailerId.HasValue) query = query.Where(item => item.RetailerId == retailerId.Value);
        if (productCategoryId.HasValue) query = query.Where(item => item.ProductCategoryId == productCategoryId.Value);
        if (productId.HasValue) query = query.Where(item => item.ProductId == productId.Value);
        if (!includeInactive) query = query.Where(item => item.IsActive);
        var rows = await (
            from mlor in query
            join retailer in dbContext.RetailerRecords.AsNoTracking() on mlor.RetailerId equals retailer.Id
            orderby retailer.Code, mlor.EffectiveFrom descending
            select new { Mlor = mlor, retailer.Code, retailer.Name })
            .ToArrayAsync(cancellationToken);
        var categoryIds = rows.Where(item => item.Mlor.ProductCategoryId.HasValue)
            .Select(item => item.Mlor.ProductCategoryId!.Value).Distinct().ToArray();
        var productIds = rows.Where(item => item.Mlor.ProductId.HasValue)
            .Select(item => item.Mlor.ProductId!.Value).Distinct().ToArray();
        var categories = await dbContext.ProductCategoryRecords.AsNoTracking()
            .Where(item => categoryIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        var products = await dbContext.ProductRecords.AsNoTracking()
            .Where(item => productIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        return rows.Select(item =>
        {
            categories.TryGetValue(item.Mlor.ProductCategoryId ?? Guid.Empty, out var category);
            products.TryGetValue(item.Mlor.ProductId ?? Guid.Empty, out var product);
            return new MlorView(item.Mlor, item.Code, item.Name,
                category?.Code, category?.Name, product?.Sku, product?.Name);
        }).ToArray();
    }

    public Task<RetailerProductMlor?> FindActiveMlorAsync(
        Guid retailerId, Guid? productCategoryId, Guid? productId, CancellationToken cancellationToken) =>
        dbContext.RetailerProductMlorRecords.SingleOrDefaultAsync(
            item => item.RetailerId == retailerId && item.ProductCategoryId == productCategoryId &&
                item.ProductId == productId && item.IsActive,
            cancellationToken);

    public Task<Retailer?> FindRetailerAsync(Guid retailerId, CancellationToken cancellationToken) =>
        dbContext.RetailerRecords.AsNoTracking().SingleOrDefaultAsync(item => item.Id == retailerId, cancellationToken);

    public Task<Product?> FindProductAsync(Guid productId, CancellationToken cancellationToken) =>
        dbContext.ProductRecords.AsNoTracking().SingleOrDefaultAsync(item => item.Id == productId, cancellationToken);

    public Task<ProductCategory?> FindCategoryAsync(Guid categoryId, CancellationToken cancellationToken) =>
        dbContext.ProductCategoryRecords.AsNoTracking().SingleOrDefaultAsync(item => item.Id == categoryId, cancellationToken);

    public void AddMlor(RetailerProductMlor mlor) => dbContext.RetailerProductMlorRecords.Add(mlor);
    public void AddAuditLog(AuditLog auditLog) => dbContext.AuditLogRecords.Add(auditLog);

    public async Task<IReadOnlyCollection<SystemConfiguration>> ListCurrentConfigurationsAsync(CancellationToken cancellationToken) =>
        await dbContext.SystemConfigurationRecords.AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.ConfigKey)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyCollection<SystemConfiguration>> ListConfigurationHistoryAsync(
        string key, CancellationToken cancellationToken) =>
        await dbContext.SystemConfigurationRecords
            .Where(item => item.ConfigKey == key)
            .OrderByDescending(item => item.Version)
            .ToArrayAsync(cancellationToken);

    public Task<SystemConfiguration?> FindCurrentConfigurationAsync(string key, CancellationToken cancellationToken) =>
        dbContext.SystemConfigurationRecords.SingleOrDefaultAsync(
            item => item.ConfigKey == key && item.IsActive, cancellationToken);

    public void AddConfiguration(SystemConfiguration configuration) => dbContext.SystemConfigurationRecords.Add(configuration);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    private IQueryable<WarehouseView> CreateWarehouseViewQuery(IQueryable<Warehouse> warehouses) =>
        from warehouse in warehouses
        join location in dbContext.MapLocationRecords.AsNoTracking()
            on warehouse.Id equals location.WarehouseId into locations
        from location in locations.DefaultIfEmpty()
        select new WarehouseView(warehouse, location);
}
