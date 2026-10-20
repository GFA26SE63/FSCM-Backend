using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.DbContexts;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AccountToken> AccountTokens => Set<AccountToken>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserCredential> UserCredentials => Set<UserCredential>();

    public DbSet<UserAccount> UserAccountRecords => Set<UserAccount>();
    public DbSet<Warehouse> WarehouseRecords => Set<Warehouse>();
    public DbSet<UserWarehouseAssignment> UserWarehouseAssignmentRecords => Set<UserWarehouseAssignment>();
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
    public DbSet<GoodsReceipt> GoodsReceiptRecords => Set<GoodsReceipt>();
    public DbSet<GoodsReceiptLine> GoodsReceiptLineRecords => Set<GoodsReceiptLine>();
    public DbSet<Batch> BatchRecords => Set<Batch>();
    public DbSet<BatchQrLabel> BatchQrLabelRecords => Set<BatchQrLabel>();
    public DbSet<QrLabelEvent> QrLabelEventRecords => Set<QrLabelEvent>();
    public DbSet<InventoryBalance> InventoryBalanceRecords => Set<InventoryBalance>();
    public DbSet<InventoryTransaction> InventoryTransactionRecords => Set<InventoryTransaction>();
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
    public DbSet<ReceiptDiscrepancy> ReceiptDiscrepancyRecords => Set<ReceiptDiscrepancy>();
    public DbSet<ReceiptDiscrepancyItem> ReceiptDiscrepancyItemRecords => Set<ReceiptDiscrepancyItem>();
    public DbSet<Payment> PaymentRecords => Set<Payment>();
    public DbSet<ManualPayment> ManualPaymentRecords => Set<ManualPayment>();
    public DbSet<OrderStatusHistory> OrderStatusHistoryRecords => Set<OrderStatusHistory>();
    public DbSet<Notification> NotificationRecords => Set<Notification>();
    public DbSet<AuditLog> AuditLogRecords => Set<AuditLog>();
    public DbSet<RetailerLoyalty> RetailerLoyaltyRecords => Set<RetailerLoyalty>();
    public DbSet<LoyaltyTransaction> LoyaltyTransactionRecords => Set<LoyaltyTransaction>();
    public DbSet<BatchPromotion> BatchPromotionRecords => Set<BatchPromotion>();
    public DbSet<MapLocation> MapLocationRecords => Set<MapLocation>();
    public DbSet<Driver> DriverRecords => Set<Driver>();
    public DbSet<Vehicle> VehicleRecords => Set<Vehicle>();
    public DbSet<Delivery> DeliveryRecords => Set<Delivery>();
    public DbSet<WriteOff> WriteOffRecords => Set<WriteOff>();
    public DbSet<WriteOffItem> WriteOffItemRecords => Set<WriteOffItem>();
    public DbSet<WriteOffAttachment> WriteOffAttachmentRecords => Set<WriteOffAttachment>();
    public DbSet<SalesOrderAttachment> SalesOrderAttachmentRecords => Set<SalesOrderAttachment>();
    public DbSet<WarehouseTransfer> WarehouseTransferRecords => Set<WarehouseTransfer>();
    public DbSet<WarehouseTransferItem> WarehouseTransferItemRecords => Set<WarehouseTransferItem>();
    public DbSet<WarehouseTransferEvent> WarehouseTransferEventRecords => Set<WarehouseTransferEvent>();
    public DbSet<WarehouseTransferLabel> WarehouseTransferLabelRecords => Set<WarehouseTransferLabel>();
    public DbSet<LoyaltyProgram> LoyaltyProgramRecords => Set<LoyaltyProgram>();
    public DbSet<LoyaltyReward> LoyaltyRewardRecords => Set<LoyaltyReward>();
    public DbSet<RewardRedemption> RewardRedemptionRecords => Set<RewardRedemption>();
    public DbSet<SystemConfiguration> SystemConfigurationRecords => Set<SystemConfiguration>();
    public DbSet<IdempotencyKey> IdempotencyKeyRecords => Set<IdempotencyKey>();
    public DbSet<OutboxMessage> OutboxMessageRecords => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasSequence<long>("RetailerDeclarationCodeSequence").StartsAt(100);
        modelBuilder.HasSequence<long>("RetailerBusinessCodeSequence").StartsAt(10005000);
        modelBuilder.HasSequence<long>("QrLabelCodeSequence").StartsAt(1000);
        modelBuilder.HasSequence<long>("PromotionCodeSequence").StartsAt(4);
        modelBuilder.HasSequence<long>("SalesOrderCodeSequence").StartsAt(1030);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
