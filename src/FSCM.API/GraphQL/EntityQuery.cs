using FSCM.Application.Features.EntityQueries;
using FSCM.Application.Features.Retailers;
using FSCM.Application.Features.WarehouseConfiguration;
using FSCM.Domain.Entities;
using HotChocolate.Data;
using HotChocolate.Types;
using InventoryBatchEntity = FSCM.Domain.Entities.Batch;

namespace FSCM.API.GraphQL;

/// <summary>
/// Composable entity read model for Portal and Mobile. Authorization scope is
/// applied by IEntityQueryService before GraphQL filtering and sorting are composed.
/// Authentication secrets and retired/internal-only aggregates are intentionally absent.
/// </summary>
public sealed class EntityQuery(EntityQueryAccess access)
{
    public Task<IReadOnlyCollection<WarehouseResult>> WarehouseProfiles(
        [Service] WarehouseConfigurationService service,
        bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        service.ListWarehousesAsync(includeInactive, cancellationToken);

    public Task<WarehouseResult> WarehouseProfile(
        Guid warehouseId,
        [Service] WarehouseConfigurationService service,
        CancellationToken cancellationToken = default) =>
        service.GetWarehouseAsync(warehouseId, cancellationToken);

    public Task<RetailerPage> RetailerProfiles(
        [Service] RetailerService service,
        string? status = null,
        string? search = null,
        string? scope = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        service.SearchAsync(access.UserId, access.Role, status, search, scope, page, pageSize, cancellationToken);

    public Task<RetailerDetails> RetailerProfile(
        Guid retailerId,
        [Service] RetailerService service,
        CancellationToken cancellationToken = default) =>
        service.GetAsync(retailerId, access.UserId, access.Role, cancellationToken);

    public Task<RetailerMlorResolutionResult> RetailerMlorResolution(
        Guid retailerId,
        Guid productId,
        [Service] RetailerService service,
        CancellationToken cancellationToken = default) =>
        service.ResolveMlorAsync(retailerId, productId, access.UserId, access.Role, cancellationToken);

    [UsePaging(typeof(DomainEntityObjectType<UserAccount>), ConnectionName = "EntityUserAccounts", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<UserAccount> UserAccounts([Service] IEntityQueryService source) => source.Query<UserAccount>(access);
    [UsePaging(typeof(DomainEntityObjectType<Warehouse>), ConnectionName = "EntityWarehouses", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<Warehouse> Warehouses([Service] IEntityQueryService source) => source.Query<Warehouse>(access);
    [UsePaging(typeof(DomainEntityObjectType<UserWarehouseAssignment>), ConnectionName = "EntityUserWarehouseAssignments", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<UserWarehouseAssignment> UserWarehouseAssignments([Service] IEntityQueryService source) => source.Query<UserWarehouseAssignment>(access);
    [UsePaging(typeof(DomainEntityObjectType<UserRegionAssignment>), ConnectionName = "EntityUserRegionAssignments", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<UserRegionAssignment> UserRegionAssignments([Service] IEntityQueryService source) => source.Query<UserRegionAssignment>(access);
    [UsePaging(typeof(DomainEntityObjectType<Region>), ConnectionName = "EntityRegions", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<Region> Regions([Service] IEntityQueryService source) => source.Query<Region>(access);
    [UsePaging(typeof(DomainEntityObjectType<SalesGroup>), ConnectionName = "EntitySalesGroups", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<SalesGroup> SalesGroups([Service] IEntityQueryService source) => source.Query<SalesGroup>(access);
    [UsePaging(typeof(DomainEntityObjectType<SalesGroupMember>), ConnectionName = "EntitySalesGroupMembers", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<SalesGroupMember> SalesGroupMembers([Service] IEntityQueryService source) => source.Query<SalesGroupMember>(access);
    [UsePaging(typeof(DomainEntityObjectType<Retailer>), ConnectionName = "EntityRetailers", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<Retailer> Retailers([Service] IEntityQueryService source) => source.Query<Retailer>(access);
    [UsePaging(typeof(DomainEntityObjectType<RetailerSalesAssignment>), ConnectionName = "EntityRetailerSalesAssignments", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<RetailerSalesAssignment> RetailerSalesAssignments([Service] IEntityQueryService source) => source.Query<RetailerSalesAssignment>(access);
    [UsePaging(typeof(DomainEntityObjectType<RetailerProductMlor>), ConnectionName = "EntityRetailerProductMlors", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<RetailerProductMlor> RetailerProductMlors([Service] IEntityQueryService source) => source.Query<RetailerProductMlor>(access);
    [UsePaging(typeof(DomainEntityObjectType<ProductCategory>), ConnectionName = "EntityProductCategories", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<ProductCategory> ProductCategories([Service] IEntityQueryService source) => source.Query<ProductCategory>(access);
    [UsePaging(typeof(DomainEntityObjectType<Product>), ConnectionName = "EntityProducts", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<Product> Products([Service] IEntityQueryService source) => source.Query<Product>(access);
    [UsePaging(typeof(DomainEntityObjectType<ProductPrice>), ConnectionName = "EntityProductPrices", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<ProductPrice> ProductPrices([Service] IEntityQueryService source) => source.Query<ProductPrice>(access);

    [UsePaging(typeof(DomainEntityObjectType<AllocationRun>), ConnectionName = "EntityAllocationRuns", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<AllocationRun> AllocationRuns([Service] IEntityQueryService source) => source.Query<AllocationRun>(access);
    [UsePaging(typeof(DomainEntityObjectType<AllocationRunOrder>), ConnectionName = "EntityAllocationRunOrders", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<AllocationRunOrder> AllocationRunOrders([Service] IEntityQueryService source) => source.Query<AllocationRunOrder>(access);
    [UsePaging(typeof(DomainEntityObjectType<AllocationRunLine>), ConnectionName = "EntityAllocationRunLines", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<AllocationRunLine> AllocationRunLines([Service] IEntityQueryService source) => source.Query<AllocationRunLine>(access);
    [UsePaging(typeof(DomainEntityObjectType<AllocationRunStock>), ConnectionName = "EntityAllocationRunStocks", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<AllocationRunStock> AllocationRunStocks([Service] IEntityQueryService source) => source.Query<AllocationRunStock>(access);
    [UsePaging(typeof(DomainEntityObjectType<AllocationRunAllocation>), ConnectionName = "EntityAllocationRunAllocations", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<AllocationRunAllocation> AllocationRunAllocations([Service] IEntityQueryService source) => source.Query<AllocationRunAllocation>(access);
    [UsePaging(typeof(DomainEntityObjectType<AllocationPlanVersion>), ConnectionName = "EntityAllocationPlanVersions", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<AllocationPlanVersion> AllocationPlanVersions([Service] IEntityQueryService source) => source.Query<AllocationPlanVersion>(access);
    [UsePaging(typeof(DomainEntityObjectType<AllocationPlanPrice>), ConnectionName = "EntityAllocationPlanPrices", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<AllocationPlanPrice> AllocationPlanPrices([Service] IEntityQueryService source) => source.Query<AllocationPlanPrice>(access);
    [UsePaging(typeof(DomainEntityObjectType<RunPickingList>), ConnectionName = "EntityRunPickingLists", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<RunPickingList> RunPickingLists([Service] IEntityQueryService source) => source.Query<RunPickingList>(access);
    [UsePaging(typeof(DomainEntityObjectType<RunPickingGather>), ConnectionName = "EntityRunPickingGathers", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<RunPickingGather> RunPickingGathers([Service] IEntityQueryService source) => source.Query<RunPickingGather>(access);
    [UsePaging(typeof(DomainEntityObjectType<RunPickingSplit>), ConnectionName = "EntityRunPickingSplits", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<RunPickingSplit> RunPickingSplits([Service] IEntityQueryService source) => source.Query<RunPickingSplit>(access);

    [UsePaging(typeof(DomainEntityObjectType<TransitTable>), ConnectionName = "EntityTransitTables", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<TransitTable> TransitTables([Service] IEntityQueryService source) => source.Query<TransitTable>(access);
    [UsePaging(typeof(DomainEntityObjectType<TransitTableBand>), ConnectionName = "EntityTransitTableBands", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<TransitTableBand> TransitTableBands([Service] IEntityQueryService source) => source.Query<TransitTableBand>(access);
    [UsePaging(typeof(DomainEntityObjectType<ShelfLifeDiscountTable>), ConnectionName = "EntityShelfLifeDiscountTables", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<ShelfLifeDiscountTable> ShelfLifeDiscountTables([Service] IEntityQueryService source) => source.Query<ShelfLifeDiscountTable>(access);
    [UsePaging(typeof(DomainEntityObjectType<ShelfLifeDiscountTier>), ConnectionName = "EntityShelfLifeDiscountTiers", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<ShelfLifeDiscountTier> ShelfLifeDiscountTiers([Service] IEntityQueryService source) => source.Query<ShelfLifeDiscountTier>(access);

    [UsePaging(typeof(DomainEntityObjectType<GoodsReceipt>), ConnectionName = "EntityGoodsReceipts", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<GoodsReceipt> GoodsReceipts([Service] IEntityQueryService source) => source.Query<GoodsReceipt>(access);
    [UsePaging(typeof(DomainEntityObjectType<GoodsReceiptLine>), ConnectionName = "EntityGoodsReceiptLines", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<GoodsReceiptLine> GoodsReceiptLines([Service] IEntityQueryService source) => source.Query<GoodsReceiptLine>(access);
    [UsePaging(typeof(DomainEntityObjectType<InventoryBatchEntity>), ConnectionName = "EntityBatches", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<InventoryBatchEntity> Batches([Service] IEntityQueryService source) => source.Query<InventoryBatchEntity>(access);
    [UsePaging(typeof(DomainEntityObjectType<BatchQrLabel>), ConnectionName = "EntityBatchQrLabels", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<BatchQrLabel> BatchQrLabels([Service] IEntityQueryService source) => source.Query<BatchQrLabel>(access);
    [UsePaging(typeof(DomainEntityObjectType<QrLabelEvent>), ConnectionName = "EntityQrLabelEvents", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<QrLabelEvent> QrLabelEvents([Service] IEntityQueryService source) => source.Query<QrLabelEvent>(access);
    [UsePaging(typeof(DomainEntityObjectType<InventoryBalance>), ConnectionName = "EntityInventoryBalances", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<InventoryBalance> InventoryBalances([Service] IEntityQueryService source) => source.Query<InventoryBalance>(access);
    [UsePaging(typeof(DomainEntityObjectType<InventoryTransaction>), ConnectionName = "EntityInventoryTransactions", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<InventoryTransaction> InventoryTransactions([Service] IEntityQueryService source) => source.Query<InventoryTransaction>(access);
    [UsePaging(typeof(DomainEntityObjectType<ExpirySweepRun>), ConnectionName = "EntityExpirySweepRuns", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<ExpirySweepRun> ExpirySweepRuns([Service] IEntityQueryService source) => source.Query<ExpirySweepRun>(access);

    [UsePaging(typeof(DomainEntityObjectType<Promotion>), ConnectionName = "EntityPromotions", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<Promotion> Promotions([Service] IEntityQueryService source) => source.Query<Promotion>(access);
    [UsePaging(typeof(DomainEntityObjectType<PromotionProduct>), ConnectionName = "EntityPromotionProducts", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<PromotionProduct> PromotionProducts([Service] IEntityQueryService source) => source.Query<PromotionProduct>(access);
    [UsePaging(typeof(DomainEntityObjectType<PromotionCategory>), ConnectionName = "EntityPromotionCategories", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<PromotionCategory> PromotionCategories([Service] IEntityQueryService source) => source.Query<PromotionCategory>(access);
    [UsePaging(typeof(DomainEntityObjectType<PromotionRetailer>), ConnectionName = "EntityPromotionRetailers", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<PromotionRetailer> PromotionRetailers([Service] IEntityQueryService source) => source.Query<PromotionRetailer>(access);
    [UsePaging(typeof(DomainEntityObjectType<SalesOrder>), ConnectionName = "EntitySalesOrders", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<SalesOrder> SalesOrders([Service] IEntityQueryService source) => source.Query<SalesOrder>(access);
    [UsePaging(typeof(DomainEntityObjectType<SalesOrderLine>), ConnectionName = "EntitySalesOrderLines", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<SalesOrderLine> SalesOrderLines([Service] IEntityQueryService source) => source.Query<SalesOrderLine>(access);
    [UsePaging(typeof(DomainEntityObjectType<OrderPromotion>), ConnectionName = "EntityOrderPromotions", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<OrderPromotion> OrderPromotions([Service] IEntityQueryService source) => source.Query<OrderPromotion>(access);
    [UsePaging(typeof(DomainEntityObjectType<OrderStatusHistory>), ConnectionName = "EntityOrderStatusHistories", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<OrderStatusHistory> OrderStatusHistories([Service] IEntityQueryService source) => source.Query<OrderStatusHistory>(access);
    [UsePaging(typeof(DomainEntityObjectType<SalesOrderAttachment>), ConnectionName = "EntitySalesOrderAttachments", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<SalesOrderAttachment> SalesOrderAttachments([Service] IEntityQueryService source) => source.Query<SalesOrderAttachment>(access);

    [UsePaging(typeof(DomainEntityObjectType<Allocation>), ConnectionName = "EntityAllocations", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<Allocation> Allocations([Service] IEntityQueryService source) => source.Query<Allocation>(access);
    [UsePaging(typeof(DomainEntityObjectType<Reservation>), ConnectionName = "EntityReservations", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<Reservation> Reservations([Service] IEntityQueryService source) => source.Query<Reservation>(access);
    [UsePaging(typeof(DomainEntityObjectType<PickingList>), ConnectionName = "EntityPickingLists", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<PickingList> PickingLists([Service] IEntityQueryService source) => source.Query<PickingList>(access);
    [UsePaging(typeof(DomainEntityObjectType<PickingItem>), ConnectionName = "EntityPickingItems", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<PickingItem> PickingItems([Service] IEntityQueryService source) => source.Query<PickingItem>(access);
    [UsePaging(typeof(DomainEntityObjectType<ReceiptConfirmation>), ConnectionName = "EntityReceiptConfirmations", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<ReceiptConfirmation> ReceiptConfirmations([Service] IEntityQueryService source) => source.Query<ReceiptConfirmation>(access);
    [UsePaging(typeof(DomainEntityObjectType<ReceiptConfirmationItem>), ConnectionName = "EntityReceiptConfirmationItems", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<ReceiptConfirmationItem> ReceiptConfirmationItems([Service] IEntityQueryService source) => source.Query<ReceiptConfirmationItem>(access);
    [UsePaging(typeof(DomainEntityObjectType<ReceiptConfirmationQrLabel>), ConnectionName = "EntityReceiptConfirmationQrLabels", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<ReceiptConfirmationQrLabel> ReceiptConfirmationQrLabels([Service] IEntityQueryService source) => source.Query<ReceiptConfirmationQrLabel>(access);
    [UsePaging(typeof(DomainEntityObjectType<ReceiptDiscrepancy>), ConnectionName = "EntityReceiptDiscrepancies", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<ReceiptDiscrepancy> ReceiptDiscrepancies([Service] IEntityQueryService source) => source.Query<ReceiptDiscrepancy>(access);
    [UsePaging(typeof(DomainEntityObjectType<ReceiptDiscrepancyItem>), ConnectionName = "EntityReceiptDiscrepancyItems", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<ReceiptDiscrepancyItem> ReceiptDiscrepancyItems([Service] IEntityQueryService source) => source.Query<ReceiptDiscrepancyItem>(access);

    [UsePaging(typeof(DomainEntityObjectType<Payment>), ConnectionName = "EntityPayments", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<Payment> Payments([Service] IEntityQueryService source) => source.Query<Payment>(access);
    [UsePaging(typeof(DomainEntityObjectType<ManualPayment>), ConnectionName = "EntityManualPayments", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<ManualPayment> ManualPayments([Service] IEntityQueryService source) => source.Query<ManualPayment>(access);
    [UsePaging(typeof(DomainEntityObjectType<ManualRefund>), ConnectionName = "EntityManualRefunds", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<ManualRefund> ManualRefunds([Service] IEntityQueryService source) => source.Query<ManualRefund>(access);
    [UsePaging(typeof(DomainEntityObjectType<Delivery>), ConnectionName = "EntityDeliveries", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<Delivery> Deliveries([Service] IEntityQueryService source) => source.Query<Delivery>(access);
    [UsePaging(typeof(DomainEntityObjectType<Shipment>), ConnectionName = "EntityShipments", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<Shipment> Shipments([Service] IEntityQueryService source) => source.Query<Shipment>(access);
    [UsePaging(typeof(DomainEntityObjectType<Driver>), ConnectionName = "EntityDrivers", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<Driver> Drivers([Service] IEntityQueryService source) => source.Query<Driver>(access);
    [UsePaging(typeof(DomainEntityObjectType<Vehicle>), ConnectionName = "EntityVehicles", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<Vehicle> Vehicles([Service] IEntityQueryService source) => source.Query<Vehicle>(access);

    [UsePaging(typeof(DomainEntityObjectType<Notification>), ConnectionName = "EntityNotifications", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<Notification> Notifications([Service] IEntityQueryService source) => source.Query<Notification>(access);
    [UsePaging(typeof(DomainEntityObjectType<AuditLog>), ConnectionName = "EntityAuditLogs", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<AuditLog> AuditLogs([Service] IEntityQueryService source) => source.Query<AuditLog>(access);
    [UsePaging(typeof(DomainEntityObjectType<RetailerLoyalty>), ConnectionName = "EntityRetailerLoyalties", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<RetailerLoyalty> RetailerLoyalties([Service] IEntityQueryService source) => source.Query<RetailerLoyalty>(access);
    [UsePaging(typeof(DomainEntityObjectType<LoyaltyTransaction>), ConnectionName = "EntityLoyaltyTransactions", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<LoyaltyTransaction> LoyaltyTransactions([Service] IEntityQueryService source) => source.Query<LoyaltyTransaction>(access);
    [UsePaging(typeof(DomainEntityObjectType<LoyaltyProgram>), ConnectionName = "EntityLoyaltyPrograms", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<LoyaltyProgram> LoyaltyPrograms([Service] IEntityQueryService source) => source.Query<LoyaltyProgram>(access);
    [UsePaging(typeof(DomainEntityObjectType<LoyaltyReward>), ConnectionName = "EntityLoyaltyRewards", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<LoyaltyReward> LoyaltyRewards([Service] IEntityQueryService source) => source.Query<LoyaltyReward>(access);
    [UsePaging(typeof(DomainEntityObjectType<RewardRedemption>), ConnectionName = "EntityRewardRedemptions", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<RewardRedemption> RewardRedemptions([Service] IEntityQueryService source) => source.Query<RewardRedemption>(access);

    [UsePaging(typeof(DomainEntityObjectType<MapLocation>), ConnectionName = "EntityMapLocations", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<MapLocation> MapLocations([Service] IEntityQueryService source) => source.Query<MapLocation>(access);
    [UsePaging(typeof(DomainEntityObjectType<MapDistanceCache>), ConnectionName = "EntityMapDistanceCaches", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<MapDistanceCache> MapDistanceCaches([Service] IEntityQueryService source) => source.Query<MapDistanceCache>(access);
    [UsePaging(typeof(DomainEntityObjectType<WriteOff>), ConnectionName = "EntityWriteOffs", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<WriteOff> WriteOffs([Service] IEntityQueryService source) => source.Query<WriteOff>(access);
    [UsePaging(typeof(DomainEntityObjectType<WriteOffItem>), ConnectionName = "EntityWriteOffItems", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<WriteOffItem> WriteOffItems([Service] IEntityQueryService source) => source.Query<WriteOffItem>(access);
    [UsePaging(typeof(DomainEntityObjectType<WriteOffAttachment>), ConnectionName = "EntityWriteOffAttachments", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<WriteOffAttachment> WriteOffAttachments([Service] IEntityQueryService source) => source.Query<WriteOffAttachment>(access);
    [UsePaging(typeof(DomainEntityObjectType<SystemConfiguration>), ConnectionName = "EntitySystemConfigurations", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false), UseProjection, UseFiltering, UseSorting]
    public IQueryable<SystemConfiguration> SystemConfigurations([Service] IEntityQueryService source) => source.Query<SystemConfiguration>(access);
}
