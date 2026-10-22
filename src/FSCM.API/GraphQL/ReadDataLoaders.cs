using FSCM.Application.Features.Catalog;
using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.Territory;
using FSCM.Application.Features.WarehouseConfiguration;
using GreenDonut;

[assembly: DataLoaderModule("FscmGraphQlDataLoaders")]

namespace FSCM.API.GraphQL;

internal static class ReadDataLoaders
{
    [DataLoader]
    public static async Task<Dictionary<Guid, ProductPriceResult[]>> GetPricesByProductIdAsync(
        IReadOnlyList<Guid> productIds,
        CatalogService service,
        CancellationToken cancellationToken) =>
        await service.ListPriceHistoryByProductIdsAsync(productIds, cancellationToken);

    [DataLoader]
    public static async Task<Dictionary<Guid, BatchInventoryResult[]>> GetInventoryByProductIdAsync(
        IReadOnlyList<Guid> productIds,
        InventoryService service,
        IHttpContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var user = CurrentUser.From(accessor);
        var inventory = await service.ListInventoryAsync(
            user.Id, user.Role, null, null, false, cancellationToken);
        return inventory.Where(item => productIds.Contains(item.ProductId))
            .GroupBy(item => item.ProductId)
            .ToDictionary(group => group.Key, group => group.ToArray());
    }

    [DataLoader]
    public static async Task<Dictionary<Guid, BatchInventoryResult[]>> GetInventoryByWarehouseIdAsync(
        IReadOnlyList<Guid> warehouseIds,
        InventoryService service,
        IHttpContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var user = CurrentUser.From(accessor);
        var inventory = await service.ListInventoryAsync(
            user.Id, user.Role, null, null, false, cancellationToken);
        return inventory.Where(item => warehouseIds.Contains(item.WarehouseId))
            .GroupBy(item => item.WarehouseId)
            .ToDictionary(group => group.Key, group => group.ToArray());
    }

    [DataLoader]
    public static async Task<Dictionary<Guid, MlorResult[]>> GetMlorByProductIdAsync(
        IReadOnlyList<Guid> productIds,
        WarehouseConfigurationService service,
        CancellationToken cancellationToken)
    {
        var items = await service.ListMlorAsync(null, null, null, false, cancellationToken);
        return items.Where(item => item.ProductId.HasValue && productIds.Contains(item.ProductId.Value))
            .GroupBy(item => item.ProductId!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());
    }

    [DataLoader]
    public static async Task<Dictionary<Guid, MlorResult[]>> GetMlorByRetailerIdAsync(
        IReadOnlyList<Guid> retailerIds,
        WarehouseConfigurationService service,
        CancellationToken cancellationToken)
    {
        var items = await service.ListMlorAsync(null, null, null, false, cancellationToken);
        return items.Where(item => retailerIds.Contains(item.RetailerId))
            .GroupBy(item => item.RetailerId)
            .ToDictionary(group => group.Key, group => group.ToArray());
    }

    [DataLoader]
    public static async Task<Dictionary<Guid, RegionResult>> GetRegionByIdAsync(
        IReadOnlyList<Guid> regionIds,
        TerritoryService service,
        CancellationToken cancellationToken) =>
        (await service.ListRegionsAsync(true, cancellationToken))
            .Where(item => regionIds.Contains(item.Id))
            .ToDictionary(item => item.Id);

    [DataLoader]
    public static async Task<Dictionary<Guid, SalesGroupResult>> GetSalesGroupByIdAsync(
        IReadOnlyList<Guid> salesGroupIds,
        TerritoryService service,
        CancellationToken cancellationToken) =>
        (await service.ListSalesGroupsAsync(null, true, cancellationToken))
            .Where(item => salesGroupIds.Contains(item.Id))
            .ToDictionary(item => item.Id);

}
