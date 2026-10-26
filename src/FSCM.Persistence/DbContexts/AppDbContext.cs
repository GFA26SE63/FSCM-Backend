using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FSCM.Persistence.DbContexts;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AccountToken> AccountTokens => Set<AccountToken>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserCredential> UserCredentials => Set<UserCredential>();

    public DbSet<UserAccount> UserAccountRecords => Set<UserAccount>();
    public DbSet<Warehouse> WarehouseRecords => Set<Warehouse>();
    public DbSet<UserWarehouseAssignment> UserWarehouseAssignmentRecords => Set<UserWarehouseAssignment>();
    public DbSet<UserRegionAssignment> UserRegionAssignmentRecords => Set<UserRegionAssignment>();
    public DbSet<Region> RegionRecords => Set<Region>();
    public DbSet<SalesGroup> SalesGroupRecords => Set<SalesGroup>();
    public DbSet<SalesGroupMember> SalesGroupMemberRecords => Set<SalesGroupMember>();
    public DbSet<Retailer> RetailerRecords => Set<Retailer>();
    public DbSet<RetailerSalesAssignment> RetailerSalesAssignmentRecords => Set<RetailerSalesAssignment>();
    public DbSet<ProductCategory> ProductCategoryRecords => Set<ProductCategory>();
    public DbSet<Product> ProductRecords => Set<Product>();
    public DbSet<ProductPrice> ProductPriceRecords => Set<ProductPrice>();
    public DbSet<RetailerProductMlor> RetailerProductMlorRecords => Set<RetailerProductMlor>();
    public DbSet<TransitTimeRule> TransitTimeRuleRecords => Set<TransitTimeRule>();
    public DbSet<AllocationRun> AllocationRunRecords => Set<AllocationRun>();
    public DbSet<AllocationRunOrder> AllocationRunOrderRecords => Set<AllocationRunOrder>();
    public DbSet<AllocationRunLine> AllocationRunLineRecords => Set<AllocationRunLine>();
    public DbSet<AllocationRunStock> AllocationRunStockRecords => Set<AllocationRunStock>();
    public DbSet<AllocationRunAllocation> AllocationRunAllocationRecords => Set<AllocationRunAllocation>();
    public DbSet<AllocationPlanVersion> AllocationPlanVersionRecords => Set<AllocationPlanVersion>();
    public DbSet<AllocationPlanPrice> AllocationPlanPriceRecords => Set<AllocationPlanPrice>();
    public DbSet<AllocationLineTrace> AllocationLineTraceRecords => Set<AllocationLineTrace>();
    public DbSet<AllocationTraceCandidate> AllocationTraceCandidateRecords => Set<AllocationTraceCandidate>();
    public DbSet<AllocationTraceScore> AllocationTraceScoreRecords => Set<AllocationTraceScore>();
    public DbSet<RunPickingList> RunPickingListRecords => Set<RunPickingList>();
    public DbSet<RunPickingGather> RunPickingGatherRecords => Set<RunPickingGather>();
    public DbSet<RunPickingSplit> RunPickingSplitRecords => Set<RunPickingSplit>();
    public DbSet<TransitTable> TransitTableRecords => Set<TransitTable>();
    public DbSet<ShelfLifeDiscountTable> ShelfLifeDiscountTableRecords => Set<ShelfLifeDiscountTable>();
    public DbSet<ShelfLifeDiscountTier> ShelfLifeDiscountTierRecords => Set<ShelfLifeDiscountTier>();
    public DbSet<TransitTableBand> TransitTableBandRecords => Set<TransitTableBand>();
    public DbSet<GoodsReceipt> GoodsReceiptRecords => Set<GoodsReceipt>();
    public DbSet<GoodsReceiptLine> GoodsReceiptLineRecords => Set<GoodsReceiptLine>();
    public DbSet<Batch> BatchRecords => Set<Batch>();
    public DbSet<BatchQrLabel> BatchQrLabelRecords => Set<BatchQrLabel>();
    public DbSet<QrLabelEvent> QrLabelEventRecords => Set<QrLabelEvent>();
    public DbSet<InventoryBalance> InventoryBalanceRecords => Set<InventoryBalance>();
    public DbSet<InventoryTransaction> InventoryTransactionRecords => Set<InventoryTransaction>();
    public DbSet<InventoryLock> InventoryLockRecords => Set<InventoryLock>();
    public DbSet<ExpirySweepRun> ExpirySweepRunRecords => Set<ExpirySweepRun>();
    public DbSet<BusinessDocumentSequence> BusinessDocumentSequenceRecords => Set<BusinessDocumentSequence>();
    public DbSet<Promotion> PromotionRecords => Set<Promotion>();
    public DbSet<PromotionProduct> PromotionProductRecords => Set<PromotionProduct>();
    public DbSet<PromotionCategory> PromotionCategoryRecords => Set<PromotionCategory>();
    public DbSet<PromotionRetailer> PromotionRetailerRecords => Set<PromotionRetailer>();
    public DbSet<SalesOrder> SalesOrderRecords => Set<SalesOrder>();
    public DbSet<SalesOrderLine> SalesOrderLineRecords => Set<SalesOrderLine>();
    public DbSet<OrderPromotion> OrderPromotionRecords => Set<OrderPromotion>();
    public DbSet<Allocation> AllocationRecords => Set<Allocation>();
    public DbSet<StockHold> StockHoldRecords => Set<StockHold>();
    public DbSet<Reservation> ReservationRecords => Set<Reservation>();
    public DbSet<PickingList> PickingListRecords => Set<PickingList>();
    public DbSet<PickingItem> PickingItemRecords => Set<PickingItem>();
    public DbSet<ReceiptConfirmation> ReceiptConfirmationRecords => Set<ReceiptConfirmation>();
    public DbSet<ReceiptConfirmationItem> ReceiptConfirmationItemRecords => Set<ReceiptConfirmationItem>();
    public DbSet<ReceiptConfirmationQrLabel> ReceiptConfirmationQrLabelRecords => Set<ReceiptConfirmationQrLabel>();
    public DbSet<ReceiptDiscrepancy> ReceiptDiscrepancyRecords => Set<ReceiptDiscrepancy>();
    public DbSet<ReceiptDiscrepancyItem> ReceiptDiscrepancyItemRecords => Set<ReceiptDiscrepancyItem>();
    public DbSet<Payment> PaymentRecords => Set<Payment>();
    public DbSet<ManualPayment> ManualPaymentRecords => Set<ManualPayment>();
    public DbSet<ManualRefund> ManualRefundRecords => Set<ManualRefund>();
    public DbSet<OrderStatusHistory> OrderStatusHistoryRecords => Set<OrderStatusHistory>();
    public DbSet<Notification> NotificationRecords => Set<Notification>();
    public DbSet<AuditLog> AuditLogRecords => Set<AuditLog>();
    public DbSet<RetailerLoyalty> RetailerLoyaltyRecords => Set<RetailerLoyalty>();
    public DbSet<LoyaltyTransaction> LoyaltyTransactionRecords => Set<LoyaltyTransaction>();
    public DbSet<BatchPromotion> BatchPromotionRecords => Set<BatchPromotion>();
    public DbSet<MapLocation> MapLocationRecords => Set<MapLocation>();
    public DbSet<MapDistanceCache> MapDistanceCacheRecords => Set<MapDistanceCache>();
    public DbSet<Driver> DriverRecords => Set<Driver>();
    public DbSet<Vehicle> VehicleRecords => Set<Vehicle>();
    public DbSet<Delivery> DeliveryRecords => Set<Delivery>();
    public DbSet<Shipment> ShipmentRecords => Set<Shipment>();
    public DbSet<WriteOff> WriteOffRecords => Set<WriteOff>();
    public DbSet<WriteOffItem> WriteOffItemRecords => Set<WriteOffItem>();
    public DbSet<WriteOffAttachment> WriteOffAttachmentRecords => Set<WriteOffAttachment>();
    public DbSet<SalesOrderAttachment> SalesOrderAttachmentRecords => Set<SalesOrderAttachment>();
    public DbSet<WarehouseTransfer> WarehouseTransferRecords => Set<WarehouseTransfer>();
    public DbSet<WarehouseTransferItem> WarehouseTransferItemRecords => Set<WarehouseTransferItem>();
    public DbSet<WarehouseTransferEvent> WarehouseTransferEventRecords => Set<WarehouseTransferEvent>();
    public DbSet<WarehouseTransferLabel> WarehouseTransferLabelRecords => Set<WarehouseTransferLabel>();
    public DbSet<LoyaltyProgram> LoyaltyProgramRecords => Set<LoyaltyProgram>();
    public DbSet<LoyaltyProgramRetailer> LoyaltyProgramRetailerRecords => Set<LoyaltyProgramRetailer>();
    public DbSet<LoyaltyReward> LoyaltyRewardRecords => Set<LoyaltyReward>();
    public DbSet<RewardRedemption> RewardRedemptionRecords => Set<RewardRedemption>();
    public DbSet<SystemConfiguration> SystemConfigurationRecords => Set<SystemConfiguration>();
    public DbSet<IdempotencyKey> IdempotencyKeyRecords => Set<IdempotencyKey>();
    public DbSet<OutboxMessage> OutboxMessageRecords => Set<OutboxMessage>();

    /// <summary>
    /// Every instant reaches PostgreSQL in UTC.
    ///
    /// Npgsql refuses to bind a <see cref="DateTimeOffset"/> whose offset is not zero to
    /// <c>timestamp with time zone</c>, and the column stores an instant anyway - the offset is
    /// never persisted. Without this, any value built in Vietnam time crashes the save, and the
    /// places that legitimately build one are exactly the places this domain cares about: the run
    /// cutoff, the schedule milestones, the delivery ETA. Fixing those one at a time is how the
    /// same crash was found twice in one day, in two different writes, so it is settled here once
    /// at the boundary rather than at each call site - which also leaves the services free to go on
    /// returning Vietnam-offset values to callers that have to display them.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        base.ConfigureConventions(builder);
        builder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
        builder.Properties<DateTimeOffset?>().HaveConversion<UtcDateTimeOffsetConverter>();
    }

    /// <summary>Same instant, offset zero. Reading back needs no change: it already is UTC.</summary>
    private sealed class UtcDateTimeOffsetConverter()
        : ValueConverter<DateTimeOffset, DateTimeOffset>(
            value => value.ToUniversalTime(), stored => stored);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasSequence<long>("RetailerDeclarationCodeSequence").StartsAt(100);
        modelBuilder.HasSequence<long>("RetailerBusinessCodeSequence").StartsAt(10005000);
        modelBuilder.HasSequence<long>("QrLabelCodeSequence").StartsAt(1000);
        modelBuilder.HasSequence<long>("CategoryCodeSequence").StartsAt(10);
        modelBuilder.HasSequence<long>("PromotionCodeSequence").StartsAt(4);
        modelBuilder.HasSequence<long>("SalesOrderCodeSequence").StartsAt(1030);
        modelBuilder.HasSequence<long>("DriverCodeSequence").StartsAt(10);
        modelBuilder.HasSequence<long>("ShipmentCodeSequence").StartsAt(1000);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
