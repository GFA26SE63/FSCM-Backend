using System.Security.Claims;
using FSCM.Application.Features.Catalog;
using FSCM.Application.Features.Identity;
using FSCM.Application.Features.InternalTransfers;
using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.QrLabels;
using FSCM.Application.Features.Promotions;
using FSCM.Application.Features.Retailers;
using FSCM.Application.Features.Territory;
using FSCM.Application.Features.WarehouseConfiguration;
using FSCM.Domain.Authorization;
using HotChocolate.Authorization;
using HotChocolate.Data;
using HotChocolate.Types;

namespace FSCM.API.GraphQL;

public sealed class SystemQuery
{
    public string Status() => "FSCM API is running";

    [Authorize]
    public Task<AuthenticatedUser> Me([Service] IdentityService service,
        [Service] IHttpContextAccessor accessor, CancellationToken cancellationToken) =>
        service.GetCurrentUserAsync(CurrentUser.Id(accessor), cancellationToken);

    [Authorize(Roles = [SystemRoles.Admin])]
    public Task<AuthenticatedUser> User(Guid id, [Service] IdentityService service,
        CancellationToken cancellationToken) => service.GetCurrentUserAsync(id, cancellationToken);

    [Authorize(Roles = [SystemRoles.Admin])]
    public Task<AuthenticatedUser> UserScope(Guid userId, [Service] IdentityService service,
        CancellationToken cancellationToken) => service.GetCurrentUserAsync(userId, cancellationToken);

    [Authorize, UsePaging(DefaultPageSize = 20, MaxPageSize = 100, IncludeTotalCount = true, RequirePagingBoundaries = true), UseFiltering, UseSorting]
    public async Task<IEnumerable<CategoryResult>> Categories([DefaultValue(false)] bool includeInactive,
        [Service] CatalogService service, CancellationToken cancellationToken) =>
        await service.ListCategoriesAsync(includeInactive, cancellationToken);

    [Authorize]
    public Task<CategoryResult> Category(Guid id, [Service] CatalogService service,
        CancellationToken cancellationToken) => service.GetCategoryAsync(id, cancellationToken);

    [Authorize, UsePaging(DefaultPageSize = 20, MaxPageSize = 100, IncludeTotalCount = true, RequirePagingBoundaries = true), UseFiltering, UseSorting]
    public async Task<IEnumerable<ProductResult>> Products(string? search, Guid? categoryId, string? status,
        [Service] CatalogService service, CancellationToken cancellationToken) =>
        await service.ListProductsForQueryAsync(search, categoryId, status, cancellationToken);

    [Authorize]
    public Task<ProductResult> Product(Guid id, [Service] CatalogService service,
        CancellationToken cancellationToken) => service.GetProductAsync(id, cancellationToken);

    [Authorize, UsePaging(DefaultPageSize = 20, MaxPageSize = 100, IncludeTotalCount = true, RequirePagingBoundaries = true), UseFiltering, UseSorting]
    public async Task<IEnumerable<ProductPriceResult>> ProductPrices(Guid productId,
        [Service] CatalogService service, CancellationToken cancellationToken) =>
        await service.ListPriceHistoryAsync(productId, cancellationToken);

    [Authorize]
    public Task<ProductPriceSuggestion> ProductPriceSuggestion(Guid productId,
        [Service] CatalogService service, CancellationToken cancellationToken) =>
        service.GetPriceSuggestionAsync(productId, cancellationToken);

    [Authorize, UsePaging(DefaultPageSize = 20, MaxPageSize = 100, IncludeTotalCount = true, RequirePagingBoundaries = true), UseFiltering, UseSorting]
    public async Task<IEnumerable<WarehouseResult>> Warehouses([DefaultValue(false)] bool includeInactive,
        [Service] WarehouseConfigurationService service, CancellationToken cancellationToken) =>
        await service.ListWarehousesAsync(includeInactive, cancellationToken);

    [Authorize]
    public Task<WarehouseResult> Warehouse(Guid id, [Service] WarehouseConfigurationService service,
        CancellationToken cancellationToken) => service.GetWarehouseAsync(id, cancellationToken);

    [Authorize, UsePaging(DefaultPageSize = 20, MaxPageSize = 100, IncludeTotalCount = true, RequirePagingBoundaries = true), UseFiltering, UseSorting]
    public async Task<IEnumerable<TransitTimeRuleResult>> TransitTimeRules(Guid warehouseId,
        [DefaultValue(false)] bool includeInactive,
        [Service] WarehouseConfigurationService service, CancellationToken cancellationToken) =>
        await service.ListTransitRulesAsync(warehouseId, includeInactive, cancellationToken);

    [Authorize, UsePaging(DefaultPageSize = 20, MaxPageSize = 100, IncludeTotalCount = true, RequirePagingBoundaries = true), UseFiltering, UseSorting]
    public async Task<IEnumerable<MlorResult>> MlorConfigurations(Guid? retailerId, Guid? productId,
        [DefaultValue(false)] bool includeInactive, [Service] WarehouseConfigurationService service,
        CancellationToken cancellationToken) =>
        await service.ListMlorAsync(retailerId, productId, includeInactive, cancellationToken);

    [Authorize, UsePaging(DefaultPageSize = 20, MaxPageSize = 100, IncludeTotalCount = true, RequirePagingBoundaries = true), UseFiltering, UseSorting]
    public async Task<IEnumerable<ConfigurationResult>> Configurations(
        [Service] WarehouseConfigurationService service, CancellationToken cancellationToken) =>
        await service.ListCurrentConfigurationsAsync(cancellationToken);

    [Authorize, UsePaging(DefaultPageSize = 20, MaxPageSize = 100, IncludeTotalCount = true, RequirePagingBoundaries = true), UseSorting]
    public async Task<IEnumerable<ConfigurationResult>> ConfigurationHistory(string key,
        [Service] WarehouseConfigurationService service, CancellationToken cancellationToken) =>
        await service.ListConfigurationHistoryAsync(key, cancellationToken);

    [Authorize, UsePaging(DefaultPageSize = 20, MaxPageSize = 100, IncludeTotalCount = true, RequirePagingBoundaries = true), UseFiltering, UseSorting]
    public async Task<IEnumerable<RegionResult>> Regions([DefaultValue(false)] bool includeInactive,
        [Service] TerritoryService service, CancellationToken cancellationToken) =>
        await service.ListRegionsAsync(includeInactive, cancellationToken);

    [Authorize]
    public Task<RegionResult> Region(Guid id, [Service] TerritoryService service,
        CancellationToken cancellationToken) => service.GetRegionAsync(id, cancellationToken);

    [Authorize, UsePaging(DefaultPageSize = 20, MaxPageSize = 100, IncludeTotalCount = true, RequirePagingBoundaries = true), UseFiltering, UseSorting]
    public async Task<IEnumerable<SalesGroupResult>> SalesGroups(Guid? regionId,
        [DefaultValue(false)] bool includeInactive,
        [Service] TerritoryService service, CancellationToken cancellationToken) =>
        await service.ListSalesGroupsAsync(regionId, includeInactive, cancellationToken);

    [Authorize]
    public Task<SalesGroupResult> SalesGroup(Guid id, [Service] TerritoryService service,
        CancellationToken cancellationToken) => service.GetSalesGroupAsync(id, cancellationToken);

    [Authorize(Roles = [SystemRoles.Admin, SystemRoles.SalesManager])]
    [UsePaging(DefaultPageSize = 20, MaxPageSize = 100, IncludeTotalCount = true, RequirePagingBoundaries = true), UseFiltering, UseSorting]
    public async Task<IEnumerable<SalesGroupMembershipResult>> SalesAssignments(Guid? salesGroupId,
        Guid? userId, [DefaultValue(false)] bool includeInactive, [Service] TerritoryService service,
        [Service] IHttpContextAccessor accessor, CancellationToken cancellationToken)
    {
        var user = CurrentUser.From(accessor);
        return await service.ListMembershipsAsync(salesGroupId, userId, includeInactive, user.Id,
            user.Principal.IsInRole(SystemRoles.Admin), cancellationToken);
    }

    [Authorize(Roles = [SystemRoles.Admin, SystemRoles.SalesManager])]
    [UsePaging(DefaultPageSize = 20, MaxPageSize = 100, IncludeTotalCount = true, RequirePagingBoundaries = true), UseFiltering, UseSorting]
    public async Task<IEnumerable<RetailerTerritoryAssignmentResult>> RetailerAssignments(Guid? retailerId,
        Guid? salesGroupId, [DefaultValue(false)] bool includeInactive, [Service] TerritoryService service,
        [Service] IHttpContextAccessor accessor, CancellationToken cancellationToken)
    {
        var user = CurrentUser.From(accessor);
        return await service.ListRetailerAssignmentsAsync(retailerId, salesGroupId, includeInactive, user.Id,
            user.Principal.IsInRole(SystemRoles.Admin), cancellationToken);
    }

    [Authorize(Roles = [SystemRoles.Sales])]
    public Task<RetailerOrderEligibility> RetailerOrderEligibility(Guid retailerId,
        [Service] RetailerService service, [Service] IHttpContextAccessor accessor,
        CancellationToken cancellationToken) =>
        service.CheckOrderEligibilityAsync(CurrentUser.Id(accessor), retailerId, cancellationToken);

    [Authorize(Roles = [SystemRoles.Sales])]
    public Task<TerritoryOrderAccessResult> TerritoryOrderAccess(Guid retailerId,
        [Service] TerritoryService service, [Service] IHttpContextAccessor accessor,
        CancellationToken cancellationToken) =>
        service.CheckOrderAccessAsync(CurrentUser.Id(accessor), retailerId, cancellationToken);

    [Authorize(Roles = [SystemRoles.Admin, SystemRoles.Operator, SystemRoles.SalesManager, SystemRoles.Sales])]
    [UsePaging(DefaultPageSize = 20, MaxPageSize = 100, IncludeTotalCount = true, RequirePagingBoundaries = true), UseFiltering, UseSorting]
    public async Task<IEnumerable<RetailerDetails>> Retailers(string? status,
        [Service] RetailerService service, [Service] IHttpContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var user = CurrentUser.From(accessor);
        return await service.ListAsync(user.Id, user.Role, status, cancellationToken);
    }

    [Authorize(Roles = [SystemRoles.Admin, SystemRoles.Operator, SystemRoles.SalesManager, SystemRoles.Sales])]
    public Task<RetailerDetails> Retailer(Guid id, [Service] RetailerService service,
        [Service] IHttpContextAccessor accessor, CancellationToken cancellationToken)
    {
        var user = CurrentUser.From(accessor);
        return service.GetAsync(id, user.Id, user.Role, cancellationToken);
    }

    [Authorize(Roles = [SystemRoles.Admin, SystemRoles.Operator, SystemRoles.WarehouseKeeper])]
    [UsePaging(DefaultPageSize = 20, MaxPageSize = 100, IncludeTotalCount = true, RequirePagingBoundaries = true), UseFiltering, UseSorting]
    public async Task<IEnumerable<GoodsReceiptResult>> GoodsReceipts(Guid? warehouseId,
        [Service] InventoryService service, [Service] IHttpContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var user = CurrentUser.From(accessor);
        return await service.ListReceiptsAsync(user.Id, user.Role, warehouseId, cancellationToken);
    }

    [Authorize(Roles = [SystemRoles.Admin, SystemRoles.Operator, SystemRoles.WarehouseKeeper])]
    public Task<GoodsReceiptResult> GoodsReceipt(Guid id, [Service] InventoryService service,
        [Service] IHttpContextAccessor accessor, CancellationToken cancellationToken)
    {
        var user = CurrentUser.From(accessor);
        return service.GetReceiptAsync(id, user.Id, user.Role, cancellationToken);
    }

    [Authorize(Roles = [SystemRoles.Admin, SystemRoles.Operator, SystemRoles.WarehouseKeeper])]
    [UsePaging(DefaultPageSize = 20, MaxPageSize = 100, IncludeTotalCount = true, RequirePagingBoundaries = true), UseFiltering, UseSorting]
    public async Task<IEnumerable<BatchInventoryResult>> Inventory(Guid? warehouseId, Guid? productId,
        [DefaultValue(false)] bool includeExpired, [Service] InventoryService service,
        [Service] IHttpContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var user = CurrentUser.From(accessor);
        return await service.ListInventoryAsync(user.Id, user.Role, warehouseId, productId,
            includeExpired, cancellationToken);
    }

    [Authorize(Roles = [SystemRoles.Admin, SystemRoles.Operator, SystemRoles.WarehouseKeeper])]
    public Task<BatchInventoryResult> InventoryBatch(Guid id, [Service] InventoryService service,
        [Service] IHttpContextAccessor accessor, CancellationToken cancellationToken)
    {
        var user = CurrentUser.From(accessor);
        return service.GetBatchAsync(id, user.Id, user.Role, cancellationToken);
    }

    [Authorize(Roles = [SystemRoles.Admin, SystemRoles.Operator, SystemRoles.WarehouseKeeper])]
    [UsePaging(DefaultPageSize = 20, MaxPageSize = 100, IncludeTotalCount = true, RequirePagingBoundaries = true), UseFiltering, UseSorting]
    public async Task<IEnumerable<InventoryHistoryResult>> InventoryHistory(Guid? warehouseId, Guid? batchId,
        [Service] InventoryService service, [Service] IHttpContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var user = CurrentUser.From(accessor);
        return await service.ListHistoryAsync(user.Id, user.Role, warehouseId, batchId, cancellationToken);
    }

    [Authorize(Roles = [SystemRoles.Admin, SystemRoles.Operator, SystemRoles.WarehouseKeeper])]
    [UsePaging(DefaultPageSize = 20, MaxPageSize = 100, IncludeTotalCount = true, RequirePagingBoundaries = true), UseFiltering, UseSorting]
    public async Task<IEnumerable<QrLabelResult>> QrLabels(Guid? warehouseId, Guid? batchId,
        [Service] QrLabelService service, [Service] IHttpContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var user = CurrentUser.From(accessor);
        return await service.ListAsync(user.Id, user.Role, warehouseId, batchId, cancellationToken);
    }

    [Authorize(Roles = [SystemRoles.Admin, SystemRoles.Operator, SystemRoles.WarehouseKeeper])]
    public Task<QrLabelDetailResult> QrLabel(string code, [Service] QrLabelService service,
        [Service] IHttpContextAccessor accessor, CancellationToken cancellationToken)
    {
        var user = CurrentUser.From(accessor);
        return service.GetAsync(code, user.Id, user.Role, cancellationToken);
    }

    [Authorize(Roles = [SystemRoles.Admin, SystemRoles.Operator])]
    [UsePaging(DefaultPageSize = 20, MaxPageSize = 100, IncludeTotalCount = true, RequirePagingBoundaries = true), UseFiltering, UseSorting]
    public async Task<IEnumerable<InternalTransferSuggestionResult>> InternalTransferSuggestions(
        Guid? destinationWarehouseId, Guid? productId, [Service] InternalTransferService service,
        CancellationToken cancellationToken) =>
        await service.GetSuggestionsAsync(destinationWarehouseId, productId, cancellationToken);

    [Authorize(Roles = [SystemRoles.Admin, SystemRoles.Operator, SystemRoles.WarehouseKeeper])]
    [UsePaging(DefaultPageSize = 20, MaxPageSize = 100, IncludeTotalCount = true, RequirePagingBoundaries = true), UseFiltering, UseSorting]
    public async Task<IEnumerable<InternalTransferResult>> InternalTransfers(Guid? warehouseId, string? status,
        [Service] InternalTransferService service, [Service] IHttpContextAccessor accessor,
        CancellationToken cancellationToken)
    {
        var user = CurrentUser.From(accessor);
        return await service.ListAsync(user.Id, user.Role, warehouseId, status, cancellationToken);
    }

    [Authorize(Roles = [SystemRoles.Admin, SystemRoles.Operator, SystemRoles.WarehouseKeeper])]
    public Task<InternalTransferResult> InternalTransfer(Guid id, [Service] InternalTransferService service,
        [Service] IHttpContextAccessor accessor, CancellationToken cancellationToken)
    {
        var user = CurrentUser.From(accessor);
        return service.GetAsync(id, user.Id, user.Role, cancellationToken);
    }

    [Authorize, UsePaging(DefaultPageSize = 20, MaxPageSize = 100, IncludeTotalCount = true, RequirePagingBoundaries = true), UseFiltering, UseSorting]
    public async Task<IEnumerable<PromotionResult>> Promotions(
        [DefaultValue(false)] bool includeInactive, [Service] PromotionService service,
        CancellationToken cancellationToken) =>
        await service.ListAsync(includeInactive, cancellationToken);

    [Authorize]
    public Task<PromotionResult> Promotion(Guid id, [Service] PromotionService service,
        CancellationToken cancellationToken) => service.GetAsync(id, cancellationToken);

    [Authorize(Roles = [SystemRoles.Admin])]
    [UsePaging(DefaultPageSize = 20, MaxPageSize = 100, IncludeTotalCount = true, RequirePagingBoundaries = true), UseFiltering, UseSorting]
    public async Task<IEnumerable<BatchDiscountSuggestionResult>> BatchDiscountSuggestions(
        Guid? warehouseId, Guid? productId, [Service] PromotionService service,
        CancellationToken cancellationToken) =>
        await service.ListBatchSuggestionsAsync(warehouseId, productId, cancellationToken);

    [Authorize, UsePaging(DefaultPageSize = 20, MaxPageSize = 100, IncludeTotalCount = true, RequirePagingBoundaries = true), UseFiltering, UseSorting]
    public async Task<IEnumerable<BatchDiscountResult>> BatchDiscounts(
        Guid? warehouseId, Guid? productId, [DefaultValue(false)] bool includeInactive,
        [Service] PromotionService service, CancellationToken cancellationToken) =>
        await service.ListBatchDiscountsAsync(warehouseId, productId, includeInactive, cancellationToken);

    [Authorize]
    public Task<BatchDiscountResult> BatchDiscount(Guid id, [Service] PromotionService service,
        CancellationToken cancellationToken) => service.GetBatchDiscountAsync(id, cancellationToken);

    [Authorize]
    public Task<PriceSnapshotResult> PriceSnapshot(
        Guid productId, decimal quantity, Guid? batchId, Guid? retailerId,
        DateTimeOffset? at, [Service] PromotionService service,
        CancellationToken cancellationToken) =>
        service.CalculatePriceAsync(productId, quantity, batchId, retailerId, at, cancellationToken);
}

internal static class CurrentUser
{
    public static (Guid Id, string Role, ClaimsPrincipal Principal) From(IHttpContextAccessor accessor)
    {
        var principal = accessor.HttpContext?.User ?? throw IdentityOperationException.InvalidToken();
        var rawUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = principal.FindFirstValue(ClaimTypes.Role);
        if (!Guid.TryParse(rawUserId, out var userId) || string.IsNullOrWhiteSpace(role))
            throw IdentityOperationException.InvalidToken();
        return (userId, role, principal);
    }

    public static Guid Id(IHttpContextAccessor accessor) => From(accessor).Id;
}
