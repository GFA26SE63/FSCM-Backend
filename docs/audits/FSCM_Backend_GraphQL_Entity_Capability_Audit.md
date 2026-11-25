# FSCM Backend GraphQL Entity Capability Audit

**Document Version:** 1.0.0  
**Date:** November 18, 2026  
**Author:** Senior .NET Backend Architect (GraphQL & HotChocolate Specialist)  
**Scope:** FSCM Backend (`FSCM-Backend`) with Reference to FSCM Frontend (`FSCM-Frontend`)  
**Status:** Complete Architecture & Entity Audit  

---

## SECTION 1 – Executive Summary

### 1.1 Overview
This audit provides an exhaustive evaluation of GraphQL capabilities across all domain entities in the FSCM Backend. FSCM utilizes **HotChocolate 15 (v15.1.12)** hosted on **ASP.NET Core (.NET 10)** backed by **Entity Framework Core** with **PostgreSQL (Npgsql)**. The architecture adheres to a CQRS pattern where GraphQL is used strictly as a composable, read-only entity query layer (`entities.*`), while all write operations, mutations, and business state transitions are handled exclusively through REST API controllers.

### 1.2 Quantitative Audit Summary

| Metric | Count | Percentage | Architectural Notes |
| :--- | :--- | :--- | :--- |
| **Total Domain Entities Discovered** | **109** | 100.0% | Located in `src/FSCM.Domain/Entities/*.cs` |
| **Total Entities Mapped in EF Core** | **103** | 94.5% | 99 in `AppDbContext`, 4 in `TraceabilityDbContext` |
| **Entities Ignored / Deprecated in EF Core** | **10** | 9.2% | Explicitly ignored via `modelBuilder.Ignore<T>()` in `AppDbContext` |
| **GraphQL-Exposed Entities (Generic Connection)** | **71** | 65.1% | Registered via `EntityQuery.cs` with `DomainEntityObjectType<T>` |
| **Entities with Dedicated GraphQL Resolvers** | **9** | 8.3% | `SalesOrder`, `Warehouse`, `Retailer`, `RetailerProductMlor`, `Product`, `ProductPrice`, `Payment`, `PaymentReceipt`, `Notification` |
| **Unexposed Domain Entities** | **38** | 34.9% | 10 ignored/legacy, 4 factory traceability, 4 auth/security, 6 tech/infra, 14 REST-only |
| **GraphQL Mutations Available** | **0** | **0.0%** | Zero mutations configured; `AddMutationType()` is completely absent |
| **Domain Entities with Navigation Properties** | **0** | **0.0%** | All domain entities are flat relational records; zero nested GraphQL graphs |

### 1.3 Key Architectural Findings

1. **Strictly Read-Only GraphQL Schema:** The HotChocolate pipeline registers `SystemQuery` as the root query type and mounts `EntityQuery` under `SystemQuery.Entities`. No mutation types are configured (`AddMutationType` does not exist). Every create, update, delete, review, approval, and payment operation requires REST.
2. **Flat Relational Projections (No Object Graphs):** Domain entities in `FSCM.Domain.Entities` do not contain EF Core navigation properties (no `ICollection<T>` or reference properties). They are 100% flat records with scalar foreign keys (`ProductId`, `WarehouseId`, etc.). As a result, HotChocolate generates flat object types (`{Entity}Entity`) with scalar fields only. There is **zero nested query support** (`product { category { ... } }` is impossible without separate client-side queries).
3. **Role-Based Row-Level Scoping (`IEntityQueryService`):** Every generic connection query in `EntityQuery` passes through `IEntityQueryService.Query<T>(access)`. This applies server-side LINQ filtering based on the caller's JWT claims (`UserId`, `Role`, `Scope`). Admin and Operator have global access, 16 entities are designated as Shared Master Data, while transactional entities are scoped to the caller's assigned warehouses, regions, sales groups, or retailer accounts.
4. **The `BLOCKER-07` Order Projection Gap:** The Frontend team successfully adopted GraphQL cursor pagination for Sales Orders (`entities.salesOrders`) and Products (`entities.products`), but encountered a critical gap: REST view models (`OrderSummaryResult`) return computed fields (`allocationState`, `allocationResult`, `allocationRunId`, `shipmentCount`, `totalCartons`, `lineCount`) which do not exist on the raw `SalesOrder` entity. Frontend currently defaults these to blank/zero values.
5. **Client-Side Join Workarounds in Frontend:** Due to the absence of GraphQL navigation resolvers, the Frontend has implemented client-side data enrichment: batch-fetching active prices (`entities.productPrices(where: { productId: { in: $ids }, isActive: { eq: true } })`), fetching categories from local cache, and resolving warehouse keepers via separate user list lookups.

---

## SECTION 2 – Complete Entity Inventory

The following table provides the exhaustive inventory of all 109 Domain Entities discovered in `src/FSCM.Domain/Entities`, detailing their physical database mapping, GraphQL ObjectType, resolver exposure, and accessibility.

| # | Entity Name | Domain Source File | Database Mapping (Table / DbContext / DbSet) | GraphQL ObjectType | Generic Query | Dedicated Resolver | Accessibility Status |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | **AccountToken** | `AccountToken.cs` | `ACCOUNT_TOKEN`<br/>AppDbContext<br/>`AccountTokens` | `—` | No | No | Unexposed (REST Only) |
| 2 | **Allocation** | `Allocation.cs` | `ALLOCATION`<br/>AppDbContext<br/>`AllocationRecords` | `AllocationEntity` | `entities.Allocations` | No | Exposed (Active) - Role Scoped |
| 3 | **AllocationLineTrace** | `AllocationLineTrace.cs` | `ALLOCATION_LINE_TRACE`<br/>AppDbContext<br/>`AllocationLineTraceRecords` | `—` | No | No | Unexposed (REST Only) |
| 4 | **AllocationPlanPrice** | `AllocationPlanPrice.cs` | `ALLOCATION_PLAN_PRICE`<br/>AppDbContext<br/>`AllocationPlanPriceRecords` | `AllocationPlanPriceEntity` | `entities.AllocationPlanPrices` | No | Exposed (Active) - Role Scoped |
| 5 | **AllocationPlanVersion** | `AllocationPlanVersion.cs` | `— (Ignored)`<br/>AppDbContext<br/>`AllocationPlanVersionRecords` | `—` | No | No | Unmapped (Ignored in OnModelCreating) |
| 6 | **AllocationRun** | `AllocationRun.cs` | `ALLOCATION_RUN`<br/>AppDbContext<br/>`AllocationRunRecords` | `AllocationRunEntity` | `entities.AllocationRuns` | No | Exposed (Active) - Role Scoped |
| 7 | **AllocationRunAllocation** | `AllocationRunAllocation.cs` | `ALLOCATION_RUN_ALLOCATION`<br/>AppDbContext<br/>`AllocationRunAllocationRecords` | `AllocationRunAllocationEntity` | `entities.AllocationRunAllocations` | No | Exposed (Active) - Role Scoped |
| 8 | **AllocationRunLine** | `AllocationRunLine.cs` | `ALLOCATION_RUN_LINE`<br/>AppDbContext<br/>`AllocationRunLineRecords` | `AllocationRunLineEntity` | `entities.AllocationRunLines` | No | Exposed (Active) - Role Scoped |
| 9 | **AllocationRunOrder** | `AllocationRunOrder.cs` | `ALLOCATION_RUN_ORDER`<br/>AppDbContext<br/>`AllocationRunOrderRecords` | `AllocationRunOrderEntity` | `entities.AllocationRunOrders` | No | Exposed (Active) - Role Scoped |
| 10 | **AllocationRunStock** | `AllocationRunStock.cs` | `ALLOCATION_RUN_STOCK`<br/>AppDbContext<br/>`AllocationRunStockRecords` | `AllocationRunStockEntity` | `entities.AllocationRunStocks` | No | Exposed (Active) - Role Scoped |
| 11 | **AllocationTraceCandidate** | `AllocationTraceCandidate.cs` | `ALLOCATION_TRACE_CANDIDATE`<br/>AppDbContext<br/>`AllocationTraceCandidateRecords` | `—` | No | No | Unexposed (REST Only) |
| 12 | **AllocationTraceScore** | `AllocationTraceScore.cs` | `ALLOCATION_TRACE_SCORE`<br/>AppDbContext<br/>`AllocationTraceScoreRecords` | `—` | No | No | Unexposed (REST Only) |
| 13 | **AuditLog** | `AuditLog.cs` | `AUDIT_LOG`<br/>AppDbContext<br/>`AuditLogRecords` | `AuditLogEntity` | `entities.AuditLogs` | No | Exposed (Active) - Admin Only (Admin) |
| 14 | **Batch** | `Batch.cs` | `BATCH`<br/>AppDbContext<br/>`BatchRecords` | `BatchEntity` | `entities.Batches` | No | Exposed (Active) - Role Scoped |
| 15 | **BatchPromotion** | `BatchPromotion.cs` | `— (Ignored)`<br/>AppDbContext<br/>`BatchPromotionRecords` | `—` | No | No | Unmapped (Ignored in OnModelCreating) |
| 16 | **BatchQrLabel** | `BatchQrLabel.cs` | `BATCH_QR_LABEL`<br/>AppDbContext<br/>`BatchQrLabelRecords` | `BatchQrLabelEntity` | `entities.BatchQrLabels` | No | Exposed (Active) - Role Scoped |
| 17 | **BusinessDocumentSequence** | `BusinessDocumentSequence.cs` | `BUSINESS_DOCUMENT_SEQUENCE`<br/>AppDbContext<br/>`BusinessDocumentSequenceRecords` | `—` | No | No | Unexposed (REST Only) |
| 18 | **ComplaintEvidence** | `ComplaintEvidence.cs` | `COMPLAINT_EVIDENCE`<br/>AppDbContext<br/>`ComplaintEvidenceRecords` | `—` | No | No | Unexposed (REST Only) |
| 19 | **Delivery** | `Delivery.cs` | `DELIVERY`<br/>AppDbContext<br/>`DeliveryRecords` | `DeliveryEntity` | `entities.Deliveries` | No | Exposed (Active) - Role Scoped |
| 20 | **Driver** | `Driver.cs` | `DRIVER`<br/>AppDbContext<br/>`DriverRecords` | `DriverEntity` | `entities.Drivers` | No | Exposed (Active) - Role Scoped |
| 21 | **ExpirySweepRun** | `ExpirySweepRun.cs` | `EXPIRY_SWEEP_RUN`<br/>AppDbContext<br/>`ExpirySweepRunRecords` | `ExpirySweepRunEntity` | `entities.ExpirySweepRuns` | No | Exposed (Active) - Unscoped / Denied to non-admin |
| 22 | **FactoryCarton** | `FactoryTraceability.cs` | `FACTORY_CARTON`<br/>TraceabilityDbContext<br/>`—` | `—` | No | No | Unexposed (Factory Traceability DB) |
| 23 | **FactoryPallet** | `FactoryTraceability.cs` | `FACTORY_PALLET`<br/>TraceabilityDbContext<br/>`—` | `—` | No | No | Unexposed (Factory Traceability DB) |
| 24 | **FactoryProductUnit** | `FactoryTraceability.cs` | `FACTORY_PRODUCT_UNIT`<br/>TraceabilityDbContext<br/>`—` | `—` | No | No | Unexposed (Factory Traceability DB) |
| 25 | **FactoryProductionBatch** | `FactoryTraceability.cs` | `FACTORY_PRODUCTION_BATCH`<br/>TraceabilityDbContext<br/>`—` | `—` | No | No | Unexposed (Factory Traceability DB) |
| 26 | **FactoryReceiptSync** | `FactoryTraceability.cs` | `FACTORY_RECEIPT_SYNC`<br/>AppDbContext<br/>`Set<FactoryReceiptSync>()` | `—` | No | No | Unexposed (REST Only) |
| 27 | **GoodsReceipt** | `GoodsReceipt.cs` | `GOODS_RECEIPT`<br/>AppDbContext<br/>`GoodsReceiptRecords` | `GoodsReceiptEntity` | `entities.GoodsReceipts` | No | Exposed (Active) - Role Scoped |
| 28 | **GoodsReceiptLine** | `GoodsReceiptLine.cs` | `GOODS_RECEIPT_LINE`<br/>AppDbContext<br/>`GoodsReceiptLineRecords` | `GoodsReceiptLineEntity` | `entities.GoodsReceiptLines` | No | Exposed (Active) - Role Scoped |
| 29 | **IdempotencyKey** | `IdempotencyKey.cs` | `IDEMPOTENCY_KEY`<br/>AppDbContext<br/>`IdempotencyKeyRecords` | `—` | No | No | Unexposed (REST Only) |
| 30 | **InventoryBalance** | `InventoryBalance.cs` | `INVENTORY_BALANCE`<br/>AppDbContext<br/>`InventoryBalanceRecords` | `InventoryBalanceEntity` | `entities.InventoryBalances` | No | Exposed (Active) - Role Scoped |
| 31 | **InventoryLock** | `InventoryLock.cs` | `INVENTORY_LOCK`<br/>AppDbContext<br/>`InventoryLockRecords` | `—` | No | No | Unexposed (REST Only) |
| 32 | **InventoryTransaction** | `InventoryTransaction.cs` | `INVENTORY_TRANSACTION`<br/>AppDbContext<br/>`InventoryTransactionRecords` | `InventoryTransactionEntity` | `entities.InventoryTransactions` | No | Exposed (Active) - Role Scoped |
| 33 | **LoyaltyProgram** | `LoyaltyProgram.cs` | `LOYALTY_PROGRAM`<br/>AppDbContext<br/>`LoyaltyProgramRecords` | `LoyaltyProgramEntity` | `entities.LoyaltyPrograms` | No | Exposed (Active) - Shared Master Data (All Roles) |
| 34 | **LoyaltyProgramRetailer** | `LoyaltyProgramRetailer.cs` | `LOYALTY_PROGRAM_RETAILER`<br/>AppDbContext<br/>`LoyaltyProgramRetailerRecords` | `—` | No | No | Unexposed (REST Only) |
| 35 | **LoyaltyReward** | `LoyaltyReward.cs` | `LOYALTY_REWARD`<br/>AppDbContext<br/>`LoyaltyRewardRecords` | `LoyaltyRewardEntity` | `entities.LoyaltyRewards` | No | Exposed (Active) - Shared Master Data (All Roles) |
| 36 | **LoyaltyTransaction** | `LoyaltyTransaction.cs` | `LOYALTY_TRANSACTION`<br/>AppDbContext<br/>`LoyaltyTransactionRecords` | `LoyaltyTransactionEntity` | `entities.LoyaltyTransactions` | No | Exposed (Active) - Role Scoped |
| 37 | **ManualRefund** | `ManualRefund.cs` | `MANUAL_REFUND`<br/>AppDbContext<br/>`ManualRefundRecords` | `ManualRefundEntity` | `entities.ManualRefunds` | No | Exposed (Active) - Role Scoped |
| 38 | **MapDistanceCache** | `MapDistanceCache.cs` | `MAP_DISTANCE_CACHE`<br/>AppDbContext<br/>`MapDistanceCacheRecords` | `MapDistanceCacheEntity` | `entities.MapDistanceCaches` | No | Exposed (Active) - Role Scoped |
| 39 | **MapLocation** | `MapLocation.cs` | `MAP_LOCATION`<br/>AppDbContext<br/>`MapLocationRecords` | `MapLocationEntity` | `entities.MapLocations` | No | Exposed (Active) - Role Scoped |
| 40 | **Notification** | `Notification.cs` | `NOTIFICATION`<br/>AppDbContext<br/>`NotificationRecords` | `NotificationEntity` | `entities.Notifications` | SystemQuery.Notifications, NotificationBadges | Exposed (Active) - Role Scoped |
| 41 | **OrderPromotion** | `OrderPromotion.cs` | `ORDER_PROMOTION`<br/>AppDbContext<br/>`OrderPromotionRecords` | `OrderPromotionEntity` | `entities.OrderPromotions` | No | Exposed (Active) - Role Scoped |
| 42 | **OrderStatusHistory** | `OrderStatusHistory.cs` | `ORDER_STATUS_HISTORY`<br/>AppDbContext<br/>`OrderStatusHistoryRecords` | `OrderStatusHistoryEntity` | `entities.OrderStatusHistories` | No | Exposed (Active) - Role Scoped |
| 43 | **OutboxMessage** | `OutboxMessage.cs` | `OUTBOX_MESSAGE`<br/>AppDbContext<br/>`OutboxMessageRecords` | `—` | No | No | Unexposed (REST Only) |
| 44 | **Payment** | `Payment.cs` | `PAYMENT`<br/>AppDbContext<br/>`PaymentRecords` | `PaymentEntity` | `entities.Payments` | SystemQuery.Receivables, RetailerReceivables, RetailerReceivable | Exposed (Active) - Role Scoped |
| 45 | **PaymentReceipt** | `PaymentReceipt.cs` | `PAYMENT_RECEIPT`<br/>AppDbContext<br/>`PaymentReceiptRecords` | `PaymentReceiptEntity` | `entities.PaymentReceipts` | SystemQuery.Receivables, RetailerReceivables, RetailerReceivable | Exposed (Active) - Role Scoped |
| 46 | **PaymentReceiptAllocation** | `PaymentReceiptAllocation.cs` | `PAYMENT_RECEIPT_ALLOCATION`<br/>AppDbContext<br/>`PaymentReceiptAllocationRecords` | `PaymentReceiptAllocationEntity` | `entities.PaymentReceiptAllocations` | No | Exposed (Active) - Role Scoped |
| 47 | **PickingItem** | `PickingItem.cs` | `PICKING_ITEM`<br/>AppDbContext<br/>`PickingItemRecords` | `PickingItemEntity` | `entities.PickingItems` | No | Exposed (Active) - Role Scoped |
| 48 | **PickingList** | `PickingList.cs` | `PICKING_LIST`<br/>AppDbContext<br/>`PickingListRecords` | `PickingListEntity` | `entities.PickingLists` | No | Exposed (Active) - Role Scoped |
| 49 | **Product** | `Product.cs` | `PRODUCT`<br/>AppDbContext<br/>`ProductRecords` | `ProductEntity` | `entities.Products` | EntityQuery.PricingSnapshot | Exposed (Active) - Shared Master Data (All Roles) |
| 50 | **ProductCategory** | `ProductCategory.cs` | `PRODUCT_CATEGORY`<br/>AppDbContext<br/>`ProductCategoryRecords` | `ProductCategoryEntity` | `entities.ProductCategories` | No | Exposed (Active) - Shared Master Data (All Roles) |
| 51 | **ProductPrice** | `ProductPrice.cs` | `PRODUCT_PRICE`<br/>AppDbContext<br/>`ProductPriceRecords` | `ProductPriceEntity` | `entities.ProductPrices` | EntityQuery.PricingSnapshot | Exposed (Active) - Shared Master Data (All Roles) |
| 52 | **Promotion** | `Promotion.cs` | `PROMOTION`<br/>AppDbContext<br/>`PromotionRecords` | `PromotionEntity` | `entities.Promotions` | No | Exposed (Active) - Shared Master Data (All Roles) |
| 53 | **PromotionCategory** | `PromotionCategory.cs` | `PROMOTION_CATEGORY`<br/>AppDbContext<br/>`PromotionCategoryRecords` | `PromotionCategoryEntity` | `entities.PromotionCategories` | No | Exposed (Active) - Shared Master Data (All Roles) |
| 54 | **PromotionProduct** | `PromotionProduct.cs` | `PROMOTION_PRODUCT`<br/>AppDbContext<br/>`PromotionProductRecords` | `PromotionProductEntity` | `entities.PromotionProducts` | No | Exposed (Active) - Shared Master Data (All Roles) |
| 55 | **PromotionRetailer** | `PromotionRetailer.cs` | `PROMOTION_RETAILER`<br/>AppDbContext<br/>`PromotionRetailerRecords` | `PromotionRetailerEntity` | `entities.PromotionRetailers` | No | Exposed (Active) - Shared Master Data (All Roles) |
| 56 | **PurchaseOrder** | `PurchaseOrder.cs` | `PURCHASE_ORDER`<br/>AppDbContext<br/>`PurchaseOrderRecords` | `—` | No | No | Unexposed (REST Only) |
| 57 | **QrLabelEvent** | `QrLabelEvent.cs` | `QR_LABEL_EVENT`<br/>AppDbContext<br/>`QrLabelEventRecords` | `QrLabelEventEntity` | `entities.QrLabelEvents` | No | Exposed (Active) - Role Scoped |
| 58 | **ReceiptConfirmation** | `ReceiptConfirmation.cs` | `RECEIPT_CONFIRMATION`<br/>AppDbContext<br/>`ReceiptConfirmationRecords` | `ReceiptConfirmationEntity` | `entities.ReceiptConfirmations` | No | Exposed (Active) - Role Scoped |
| 59 | **ReceiptConfirmationItem** | `ReceiptConfirmationItem.cs` | `RECEIPT_CONFIRMATION_ITEM`<br/>AppDbContext<br/>`ReceiptConfirmationItemRecords` | `ReceiptConfirmationItemEntity` | `entities.ReceiptConfirmationItems` | No | Exposed (Active) - Role Scoped |
| 60 | **ReceiptConfirmationQrLabel** | `ReceiptConfirmationQrLabel.cs` | `RECEIPT_CONFIRMATION_QR_LABEL`<br/>AppDbContext<br/>`ReceiptConfirmationQrLabelRecords` | `ReceiptConfirmationQrLabelEntity` | `entities.ReceiptConfirmationQrLabels` | No | Exposed (Active) - Role Scoped |
| 61 | **ReceiptDiscrepancy** | `ReceiptDiscrepancy.cs` | `RECEIPT_DISCREPANCY`<br/>AppDbContext<br/>`ReceiptDiscrepancyRecords` | `ReceiptDiscrepancyEntity` | `entities.ReceiptDiscrepancies` | No | Exposed (Active) - Role Scoped |
| 62 | **ReceiptDiscrepancyItem** | `ReceiptDiscrepancyItem.cs` | `RECEIPT_DISCREPANCY_ITEM`<br/>AppDbContext<br/>`ReceiptDiscrepancyItemRecords` | `ReceiptDiscrepancyItemEntity` | `entities.ReceiptDiscrepancyItems` | No | Exposed (Active) - Role Scoped |
| 63 | **RefreshToken** | `RefreshToken.cs` | `REFRESH_TOKEN`<br/>AppDbContext<br/>`RefreshTokens` | `—` | No | No | Unexposed (REST Only) |
| 64 | **Region** | `Region.cs` | `REGION`<br/>AppDbContext<br/>`RegionRecords` | `RegionEntity` | `entities.Regions` | No | Exposed (Active) - Shared Master Data (All Roles) |
| 65 | **ReplacementCase** | `ReplacementCase.cs` | `REPLACEMENT_CASE`<br/>AppDbContext<br/>`ReplacementCaseRecords` | `ReplacementCaseEntity` | `entities.ReplacementCases` | No | Exposed (Active) - Role Scoped |
| 66 | **Reservation** | `Reservation.cs` | `RESERVATION`<br/>AppDbContext<br/>`ReservationRecords` | `ReservationEntity` | `entities.Reservations` | No | Exposed (Active) - Role Scoped |
| 67 | **Retailer** | `Retailer.cs` | `RETAILER`<br/>AppDbContext<br/>`RetailerRecords` | `RetailerEntity` | `entities.Retailers` | EntityQuery.RetailerProfiles, RetailerProfile | Exposed (Active) - Role Scoped |
| 68 | **RetailerActivationChallenge** | `RetailerActivationChallenge.cs` | `RETAILER_ACTIVATION_CHALLENGE`<br/>AppDbContext<br/>`RetailerActivationChallenges` | `—` | No | No | Unexposed (REST Only) |
| 69 | **RetailerDebtPayOsAllocation** | `PaymentReceipt.cs` | `RETAILER_DEBT_PAYOS_ALLOCATION`<br/>AppDbContext<br/>`RetailerDebtPayOsAllocationRecords` | `—` | No | No | Unexposed (REST Only) |
| 70 | **RetailerDebtPayOsCheckout** | `PaymentReceipt.cs` | `RETAILER_DEBT_PAYOS_CHECKOUT`<br/>AppDbContext<br/>`RetailerDebtPayOsCheckoutRecords` | `—` | No | No | Unexposed (REST Only) |
| 71 | **RetailerDeclarationContact** | `RetailerDeclarationContact.cs` | `RETAILER_DECLARATION_CONTACT`<br/>AppDbContext<br/>`RetailerDeclarationContactRecords` | `—` | No | No | Unexposed (REST Only) |
| 72 | **RetailerLoyalty** | `RetailerLoyalty.cs` | `RETAILER_LOYALTY`<br/>AppDbContext<br/>`RetailerLoyaltyRecords` | `RetailerLoyaltyEntity` | `entities.RetailerLoyalties` | No | Exposed (Active) - Role Scoped |
| 73 | **RetailerProductMlor** | `RetailerProductMlor.cs` | `RETAILER_PRODUCT_MLOR`<br/>AppDbContext<br/>`RetailerProductMlorRecords` | `RetailerProductMlorEntity` | `entities.RetailerProductMlors` | EntityQuery.RetailerMlorResolution | Exposed (Active) - Role Scoped |
| 74 | **RetailerSalesAssignment** | `RetailerSalesAssignment.cs` | `RETAILER_SALES_ASSIGNMENT`<br/>AppDbContext<br/>`RetailerSalesAssignmentRecords` | `RetailerSalesAssignmentEntity` | `entities.RetailerSalesAssignments` | No | Exposed (Active) - Role Scoped |
| 75 | **ReturnHandlingDocument** | `ReturnHandlingDocument.cs` | `RETURN_HANDLING_DOCUMENT`<br/>AppDbContext<br/>`ReturnHandlingDocumentRecords` | `—` | No | No | Unexposed (REST Only) |
| 76 | **RewardRedemption** | `RewardRedemption.cs` | `REWARD_REDEMPTION`<br/>AppDbContext<br/>`RewardRedemptionRecords` | `RewardRedemptionEntity` | `entities.RewardRedemptions` | No | Exposed (Active) - Role Scoped |
| 77 | **RunPickingGather** | `RunPickingGather.cs` | `— (Ignored)`<br/>AppDbContext<br/>`RunPickingGatherRecords` | `—` | No | No | Unmapped (Ignored in OnModelCreating) |
| 78 | **RunPickingList** | `RunPickingList.cs` | `— (Ignored)`<br/>AppDbContext<br/>`RunPickingListRecords` | `—` | No | No | Unmapped (Ignored in OnModelCreating) |
| 79 | **RunPickingSplit** | `RunPickingSplit.cs` | `— (Ignored)`<br/>AppDbContext<br/>`RunPickingSplitRecords` | `—` | No | No | Unmapped (Ignored in OnModelCreating) |
| 80 | **SalesGroup** | `SalesGroup.cs` | `SALES_GROUP`<br/>AppDbContext<br/>`SalesGroupRecords` | `SalesGroupEntity` | `entities.SalesGroups` | No | Exposed (Active) - Shared Master Data (All Roles) |
| 81 | **SalesGroupMember** | `SalesGroupMember.cs` | `SALES_GROUP_MEMBER`<br/>AppDbContext<br/>`SalesGroupMemberRecords` | `SalesGroupMemberEntity` | `entities.SalesGroupMembers` | No | Exposed (Active) - Admin Only (Admin) |
| 82 | **SalesOrder** | `SalesOrder.cs` | `SALES_ORDER`<br/>AppDbContext<br/>`SalesOrderRecords` | `SalesOrderEntity` | `entities.SalesOrders` | EntityQuery.OrderReads, OrderRead | Exposed (Active) - Role Scoped |
| 83 | **SalesOrderAttachment** | `SalesOrderAttachment.cs` | `SALES_ORDER_ATTACHMENT`<br/>AppDbContext<br/>`SalesOrderAttachmentRecords` | `SalesOrderAttachmentEntity` | `entities.SalesOrderAttachments` | No | Exposed (Active) - Role Scoped |
| 84 | **SalesOrderLine** | `SalesOrderLine.cs` | `SALES_ORDER_LINE`<br/>AppDbContext<br/>`SalesOrderLineRecords` | `SalesOrderLineEntity` | `entities.SalesOrderLines` | No | Exposed (Active) - Role Scoped |
| 85 | **SalesOrderQuote** | `SalesOrderQuote.cs` | `SALES_ORDER_QUOTE`<br/>AppDbContext<br/>`SalesOrderQuoteRecords` | `—` | No | No | Unexposed (REST Only) |
| 86 | **ShelfLifeDiscountTable** | `ShelfLifeDiscountTable.cs` | `SHELF_LIFE_DISCOUNT_TABLE`<br/>AppDbContext<br/>`ShelfLifeDiscountTableRecords` | `ShelfLifeDiscountTableEntity` | `entities.ShelfLifeDiscountTables` | No | Exposed (Active) - Shared Master Data (All Roles) |
| 87 | **ShelfLifeDiscountTier** | `ShelfLifeDiscountTier.cs` | `SHELF_LIFE_DISCOUNT_TIER`<br/>AppDbContext<br/>`ShelfLifeDiscountTierRecords` | `ShelfLifeDiscountTierEntity` | `entities.ShelfLifeDiscountTiers` | No | Exposed (Active) - Shared Master Data (All Roles) |
| 88 | **Shipment** | `Shipment.cs` | `SHIPMENT`<br/>AppDbContext<br/>`ShipmentRecords` | `ShipmentEntity` | `entities.Shipments` | No | Exposed (Active) - Role Scoped |
| 89 | **StockHold** | `StockHold.cs` | `— (Ignored)`<br/>AppDbContext<br/>`StockHoldRecords` | `—` | No | No | Unmapped (Ignored in OnModelCreating) |
| 90 | **SystemConfiguration** | `SystemConfiguration.cs` | `SYSTEM_CONFIGURATION`<br/>AppDbContext<br/>`SystemConfigurationRecords` | `SystemConfigurationEntity` | `entities.SystemConfigurations` | No | Exposed (Active) - Admin Only (Admin) |
| 91 | **TransitTable** | `TransitTable.cs` | `TRANSIT_TABLE`<br/>AppDbContext<br/>`TransitTableRecords` | `TransitTableEntity` | `entities.TransitTables` | No | Exposed (Active) - Shared Master Data (All Roles) |
| 92 | **TransitTableBand** | `TransitTableBand.cs` | `TRANSIT_TABLE_BAND`<br/>AppDbContext<br/>`TransitTableBandRecords` | `TransitTableBandEntity` | `entities.TransitTableBands` | No | Exposed (Active) - Shared Master Data (All Roles) |
| 93 | **TransitTimeRule** | `TransitTimeRule.cs` | `TRANSIT_TIME_RULE`<br/>AppDbContext<br/>`TransitTimeRuleRecords` | `—` | No | No | Unexposed (REST Only) |
| 94 | **UserAccount** | `UserAccount.cs` | `USER_ACCOUNT`<br/>AppDbContext<br/>`UserAccountRecords` | `UserAccountEntity` | `entities.UserAccounts` | No | Exposed (Active) - Admin Only (Admin) |
| 95 | **UserCredential** | `UserCredential.cs` | `USER_CREDENTIAL`<br/>AppDbContext<br/>`UserCredentials` | `—` | No | No | Unexposed (REST Only) |
| 96 | **UserRegionAssignment** | `UserRegionAssignment.cs` | `USER_REGION_ASSIGNMENT`<br/>AppDbContext<br/>`UserRegionAssignmentRecords` | `UserRegionAssignmentEntity` | `entities.UserRegionAssignments` | No | Exposed (Active) - Admin Only (Admin) |
| 97 | **UserWarehouseAssignment** | `UserWarehouseAssignment.cs` | `USER_WAREHOUSE_ASSIGNMENT`<br/>AppDbContext<br/>`UserWarehouseAssignmentRecords` | `UserWarehouseAssignmentEntity` | `entities.UserWarehouseAssignments` | No | Exposed (Active) - Admin Only (Admin) |
| 98 | **Vehicle** | `Vehicle.cs` | `VEHICLE`<br/>AppDbContext<br/>`VehicleRecords` | `VehicleEntity` | `entities.Vehicles` | No | Exposed (Active) - Role Scoped |
| 99 | **Warehouse** | `Warehouse.cs` | `WAREHOUSE`<br/>AppDbContext<br/>`WarehouseRecords` | `WarehouseEntity` | `entities.Warehouses` | EntityQuery.WarehouseProfiles, WarehouseProfile | Exposed (Active) - Shared Master Data (All Roles) |
| 100 | **WarehouseDocument** | `WarehouseDocument.cs` | `WAREHOUSE_DOCUMENT`<br/>AppDbContext<br/>`WarehouseDocuments` | `—` | No | No | Unexposed (REST Only) |
| 101 | **WarehouseDocumentCarton** | `WarehouseDocument.cs` | `WAREHOUSE_DOCUMENT_CARTON`<br/>AppDbContext<br/>`WarehouseDocumentCartons` | `—` | No | No | Unexposed (REST Only) |
| 102 | **WarehouseDocumentLine** | `WarehouseDocument.cs` | `WAREHOUSE_DOCUMENT_LINE`<br/>AppDbContext<br/>`WarehouseDocumentLines` | `—` | No | No | Unexposed (REST Only) |
| 103 | **WarehouseTransfer** | `WarehouseTransfer.cs` | `— (Ignored)`<br/>AppDbContext<br/>`WarehouseTransferRecords` | `—` | No | No | Unmapped (Ignored in OnModelCreating) |
| 104 | **WarehouseTransferEvent** | `WarehouseTransferEvent.cs` | `— (Ignored)`<br/>AppDbContext<br/>`WarehouseTransferEventRecords` | `—` | No | No | Unmapped (Ignored in OnModelCreating) |
| 105 | **WarehouseTransferItem** | `WarehouseTransferItem.cs` | `— (Ignored)`<br/>AppDbContext<br/>`WarehouseTransferItemRecords` | `—` | No | No | Unmapped (Ignored in OnModelCreating) |
| 106 | **WarehouseTransferLabel** | `WarehouseTransferLabel.cs` | `— (Ignored)`<br/>AppDbContext<br/>`WarehouseTransferLabelRecords` | `—` | No | No | Unmapped (Ignored in OnModelCreating) |
| 107 | **WriteOff** | `WriteOff.cs` | `WRITE_OFF`<br/>AppDbContext<br/>`WriteOffRecords` | `WriteOffEntity` | `entities.WriteOffs` | No | Exposed (Active) - Role Scoped |
| 108 | **WriteOffAttachment** | `WriteOffAttachment.cs` | `WRITE_OFF_ATTACHMENT`<br/>AppDbContext<br/>`WriteOffAttachmentRecords` | `WriteOffAttachmentEntity` | `entities.WriteOffAttachments` | No | Exposed (Active) - Role Scoped |
| 109 | **WriteOffItem** | `WriteOffItem.cs` | `WRITE_OFF_ITEM`<br/>AppDbContext<br/>`WriteOffItemRecords` | `WriteOffItemEntity` | `entities.WriteOffItems` | No | Exposed (Active) - Role Scoped |

---

## SECTION 3 – GraphQL Query Capability Matrix

### 3.1 Generic Entity Query Pipeline Architecture
All 71 generic entity queries in `EntityQuery.cs` share a standardized HotChocolate execution pipeline:
```csharp
[UsePaging(typeof(DomainEntityObjectType<TEntity>), ConnectionName = "Entity...", DefaultPageSize = 50, MaxPageSize = int.MaxValue, IncludeTotalCount = true, RequirePagingBoundaries = false)]
[UseProjection]
[UseFiltering]
[UseSorting]
public IQueryable<TEntity> MethodName([Service] IEntityQueryService source) => source.Query<TEntity>(access);
```

#### Pipeline Stage Capabilities:
- **Cursor-Based Pagination (`[UsePaging]`):** Returns a Relay-compliant connection (`Entity{T}sConnection`). Supports `first`, `after`, `last`, `before`. Returns `pageInfo { hasNextPage, hasPreviousPage, startCursor, endCursor }` and `totalCount`. Default page size is 50. Maximum page size is set to `int.MaxValue`.
- **Projection Engine (`[UseProjection]`):** Translates GraphQL field selection sets directly into SQL `SELECT` projections via EF Core LINQ, minimizing database network bandwidth.
- **Filter Engine (`[UseFiltering]`):** Generates `{Entity}EntityFilterInput`. Supports `eq`, `neq`, `in`, `nin`, `contains`, `startsWith`, `endsWith`, `gt`, `gte`, `lt`, `lte`, and boolean combinations (`and`, `or`). Translated server-side to PostgreSQL `WHERE` clauses.
- **Sorting Engine (`[UseSorting]`):** Generates `{Entity}EntitySortInput`. Supports sorting on any exposed scalar field in `ASC` or `DESC` order. Translated server-side to PostgreSQL `ORDER BY` clauses.

### 3.2 Detail Query Capabilities
There are two mechanisms for retrieving an individual entity by ID:
1. **Generic Connection ID Filter (Supported on all 71 exposed entities):**
   ```graphql
   query GetEntityById($id: UUID!) {
     entities {
       warehouses(where: { id: { eq: $id } }, first: 1) {
         nodes { id code name address status }
       }
     }
   }
   ```
2. **Dedicated Single-Entity Resolvers (Supported on select entities):**
   - `entities.orderRead(id: UUID!)` -> Returns `OrderDetailResult` (includes lines, pricing breakdown, allocation summary).
   - `entities.warehouseProfile(warehouseId: UUID!)` -> Returns `WarehouseResult`.
   - `entities.retailerProfile(retailerId: UUID!)` -> Returns `RetailerDetails`.
   - `retailerReceivable(orderId: UUID!)` -> Returns `RetailerReceivableResult` (Root `SystemQuery`).

### 3.3 Complete Capability Matrix for Exposed Entities

| Entity Name | GraphQL Field Name | List Query | Detail by ID | Search & Filter Supported | Sorting Enabled | Cursor Paging | TotalCount | Server-Side Execution |
| :--- | :--- | :---: | :---: | :--- | :---: | :---: | :---: | :---: |
| **Allocation** | `entities.Allocations` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **AllocationPlanPrice** | `entities.AllocationPlanPrices` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **AllocationRun** | `entities.AllocationRuns` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **AllocationRunAllocation** | `entities.AllocationRunAllocations` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **AllocationRunLine** | `entities.AllocationRunLines` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **AllocationRunOrder** | `entities.AllocationRunOrders` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **AllocationRunStock** | `entities.AllocationRunStocks` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **AuditLog** | `entities.AuditLogs` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **Batch** | `entities.Batches` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **BatchQrLabel** | `entities.BatchQrLabels` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **Delivery** | `entities.Deliveries` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **Driver** | `entities.Drivers` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **ExpirySweepRun** | `entities.ExpirySweepRuns` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **GoodsReceipt** | `entities.GoodsReceipts` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **GoodsReceiptLine** | `entities.GoodsReceiptLines` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **InventoryBalance** | `entities.InventoryBalances` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **InventoryTransaction** | `entities.InventoryTransactions` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **LoyaltyProgram** | `entities.LoyaltyPrograms` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **LoyaltyReward** | `entities.LoyaltyRewards` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **LoyaltyTransaction** | `entities.LoyaltyTransactions` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **ManualRefund** | `entities.ManualRefunds` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **MapDistanceCache** | `entities.MapDistanceCaches` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **MapLocation** | `entities.MapLocations` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **Notification** | `entities.Notifications` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **OrderPromotion** | `entities.OrderPromotions` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **OrderStatusHistory** | `entities.OrderStatusHistories` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **Payment** | `entities.Payments` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **PaymentReceipt** | `entities.PaymentReceipts` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **PaymentReceiptAllocation** | `entities.PaymentReceiptAllocations` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **PickingItem** | `entities.PickingItems` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **PickingList** | `entities.PickingLists` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **Product** | `entities.Products` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **ProductCategory** | `entities.ProductCategories` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **ProductPrice** | `entities.ProductPrices` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **Promotion** | `entities.Promotions` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **PromotionCategory** | `entities.PromotionCategories` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **PromotionProduct** | `entities.PromotionProducts` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **PromotionRetailer** | `entities.PromotionRetailers` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **QrLabelEvent** | `entities.QrLabelEvents` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **ReceiptConfirmation** | `entities.ReceiptConfirmations` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **ReceiptConfirmationItem** | `entities.ReceiptConfirmationItems` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **ReceiptConfirmationQrLabel** | `entities.ReceiptConfirmationQrLabels` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **ReceiptDiscrepancy** | `entities.ReceiptDiscrepancies` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **ReceiptDiscrepancyItem** | `entities.ReceiptDiscrepancyItems` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **Region** | `entities.Regions` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **ReplacementCase** | `entities.ReplacementCases` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **Reservation** | `entities.Reservations` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **Retailer** | `entities.Retailers` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **RetailerLoyalty** | `entities.RetailerLoyalties` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **RetailerProductMlor** | `entities.RetailerProductMlors` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **RetailerSalesAssignment** | `entities.RetailerSalesAssignments` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **RewardRedemption** | `entities.RewardRedemptions` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **SalesGroup** | `entities.SalesGroups` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **SalesGroupMember** | `entities.SalesGroupMembers` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **SalesOrder** | `entities.SalesOrders` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **SalesOrderAttachment** | `entities.SalesOrderAttachments` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **SalesOrderLine** | `entities.SalesOrderLines` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **ShelfLifeDiscountTable** | `entities.ShelfLifeDiscountTables` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **ShelfLifeDiscountTier** | `entities.ShelfLifeDiscountTiers` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **Shipment** | `entities.Shipments` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **SystemConfiguration** | `entities.SystemConfigurations` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **TransitTable** | `entities.TransitTables` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **TransitTableBand** | `entities.TransitTableBands` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **UserAccount** | `entities.UserAccounts` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **UserRegionAssignment** | `entities.UserRegionAssignments` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **UserWarehouseAssignment** | `entities.UserWarehouseAssignments` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **Vehicle** | `entities.Vehicles` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **Warehouse** | `entities.Warehouses` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **WriteOff** | `entities.WriteOffs` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **WriteOffAttachment** | `entities.WriteOffAttachments` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |
| **WriteOffItem** | `entities.WriteOffItems` | Yes | ID Filter (`where: { id: { eq: $id } }`) | All scalar fields (`eq, in, contains, gte, ...`) | Yes (ASC/DESC) | Yes (`first/after, last/before`) | Yes | 100% EF Core SQL |

---

## SECTION 4 – Relationship Matrix

### 4.1 Relationship Architecture & The Missing Navigation Gap
A critical architectural discovery of this audit is that **FSCM Domain Entities are designed as pure flat relational records** mirroring the physical ERD table columns. They deliberately do not declare EF Core virtual navigation properties (`ICollection<T>` or child reference objects).

Because HotChocolate reflects directly on the CLR type definitions within `DomainEntityObjectType<TEntity>`, the generated GraphQL schema contains **only scalar fields and foreign key IDs** (`Guid`, `string`, `int`, etc.).

### 4.2 Major Entity Relationships & Resolution Strategy

| Source Entity | Foreign Key Field | Target Entity | DB FK Constraint | GraphQL Nested Query Support | Frontend Resolution Strategy |
| :--- | :--- | :--- | :--- | :---: | :--- |
| **Product** | `productCategoryId` | `ProductCategory` | `FK_PRODUCT_ProductCategoryId` | **No** | Populated client-side from cached category dictionary |
| **Product** | `replacesProductId` | `Product` | `FK_PRODUCT_ReplacesProductId` | **No** | Separate query to `entities.products` |
| **ProductPrice** | `productId` | `Product` | `FK_PRODUCT_PRICE_ProductId` | **No** | Batch queried: `entities.productPrices(where: { productId: { in: $ids } })` |
| **Warehouse** | `regionId` | `Region` | `FK_WAREHOUSE_RegionId` | **No** | Client-side lookup against Region master data |
| **Retailer** | `regionId` | `Region` | `FK_RETAILER_RegionId` | **No** | Client-side lookup against Region master data |
| **SalesOrder** | `retailerId` | `Retailer` | `FK_SALES_ORDER_RetailerId` | **No** | Entity contains denormalized snapshots (`retailerCodeSnapshot`, `retailerNameSnapshot`) |
| **SalesOrder** | `salesGroupId` | `SalesGroup` | `FK_SALES_ORDER_SalesGroupId` | **No** | Separate query to `entities.salesGroups` |
| **SalesOrder** | `createdByUserId` | `UserAccount` | `FK_SALES_ORDER_CreatedByUserId` | **No** | Separate query to `entities.userAccounts` |
| **SalesOrderLine** | `salesOrderId` | `SalesOrder` | `FK_SALES_ORDER_LINE_SalesOrderId` | **No** | Separate query to `entities.salesOrderLines` or REST `/orders/{id}` |
| **SalesOrderLine** | `productId` | `Product` | `FK_SALES_ORDER_LINE_ProductId` | **No** | Client-side join with `entities.products` |
| **Batch** | `productId` | `Product` | `FK_BATCH_ProductId` | **No** | Separate query or REST `/inventory/batches` |
| **Batch** | `warehouseId` | `Warehouse` | `FK_BATCH_WarehouseId` | **No** | Separate query or REST `/inventory/batches` |
| **InventoryBalance** | `batchId` | `Batch` | `FK_INVENTORY_BALANCE_BatchId` | **No** | Separate query to `entities.batches` |
| **Shipment** | `salesOrderId` | `SalesOrder` | `FK_SHIPMENT_SalesOrderId` | **No** | Separate query to `entities.salesOrders` |
| **Shipment** | `warehouseId` | `Warehouse` | `FK_SHIPMENT_WarehouseId` | **No** | Separate query to `entities.warehouses` |
| **Shipment** | `driverId` | `Driver` | `FK_SHIPMENT_DriverId` | **No** | Separate query or REST `/deliveries/drivers` |
| **Shipment** | `vehicleId` | `Vehicle` | `FK_SHIPMENT_VehicleId` | **No** | Separate query or REST `/deliveries/vehicles` |
| **PickingList** | `warehouseId` | `Warehouse` | `FK_PICKING_LIST_WarehouseId` | **No** | Scope check + client-side warehouse lookup |
| **PickingItem** | `pickingListId` | `PickingList` | `FK_PICKING_ITEM_PickingListId` | **No** | Separate query to `entities.pickingItems` |
| **PickingItem** | `batchId` | `Batch` | `FK_PICKING_ITEM_BatchId` | **No** | Separate query to `entities.batches` |
| **Delivery** | `salesOrderId` | `SalesOrder` | `FK_DELIVERY_SalesOrderId` | **No** | Separate query to `entities.salesOrders` |
| **GoodsReceipt** | `warehouseId` | `Warehouse` | `FK_GOODS_RECEIPT_WarehouseId` | **No** | Separate query to `entities.warehouses` |
| **GoodsReceiptLine** | `goodsReceiptId` | `GoodsReceipt` | `FK_GOODS_RECEIPT_LINE_GoodsReceiptId` | **No** | Separate query to `entities.goodsReceiptLines` |
| **PaymentReceipt** | `retailerId` | `Retailer` | `FK_PAYMENT_RECEIPT_RetailerId` | **No** | Separate query to `entities.retailers` |
| **PaymentReceiptAllocation** | `paymentReceiptId` | `PaymentReceipt` | `FK_PAYMENT_RECEIPT_ALLOCATION_PaymentReceiptId` | **No** | Separate query to `entities.paymentReceiptAllocations` |
| **WriteOffItem** | `writeOffId` | `WriteOff` | `FK_WRITE_OFF_ITEM_WriteOffId` | **No** | Separate query to `entities.writeOffItems` |

---

## SECTION 5 – Mutation Capability Matrix

### 5.1 GraphQL Mutation Audit Finding: 0% Mutation Coverage
Inspection of `src/FSCM.API/Extensions/ServiceCollectionExtensions.cs` confirms that **no GraphQL mutations exist in the FSCM Backend**:
- `AddMutationType()` is **not called** anywhere in the solution.
- No `[ExtendObjectType]` classes with mutation methods exist.
- All data modification operations (Create, Update, Delete, Workflow Transitions) are routed through ASP.NET Core REST Controllers.

### 5.2 Enterprise Write Operations Mapping (GraphQL vs REST)

| Business Domain | Target Entity | Operation | GraphQL Mutation Name | Existing REST Implementation | Controller Location |
| :--- | :--- | :--- | :---: | :--- | :--- |
| **Products** | `Product` | Create Product | **None** | `POST /api/v1/products` | `ProductsController.cs` |
| **Products** | `Product` | Update Product | **None** | `PUT /api/v1/products/{id}` | `ProductsController.cs` |
| **Products** | `Product` | Upload Product Image | **None** | `POST /api/v1/products/{id}/image` | `ProductsController.cs` |
| **Pricing** | `ProductPrice` | Set Price | **None** | `POST /api/v1/pricing/products/{id}/prices` | `ProductsController.cs` |
| **Warehouses** | `Warehouse` | Create Warehouse | **None** | `POST /api/v1/warehouses` | `WarehousesController.cs` |
| **Warehouses** | `Warehouse` | Update Warehouse | **None** | `PUT /api/v1/warehouses/{id}` | `WarehousesController.cs` |
| **Retailers** | `Retailer` | Register Retailer | **None** | `POST /api/v1/retailers` | `RetailersController.cs` |
| **Retailers** | `Retailer` | Verify OTP Activation | **None** | `POST /api/v1/retailers/{id}/activation/verify` | `RetailersController.cs` |
| **Orders** | `SalesOrder` | Create Order (Assisted) | **None** | `POST /api/v1/orders/assisted` | `OrdersController.cs` |
| **Orders** | `SalesOrder` | Create Order (Offline Sync) | **None** | `POST /api/v1/orders/offline` | `OrdersController.cs` |
| **Orders** | `SalesOrder` | Review / Approve Order | **None** | `POST /api/v1/orders/{id}/review` | `OrdersController.cs` |
| **Orders** | `SalesOrder` | Cancel Order | **None** | `POST /api/v1/orders/{id}/cancel` | `OrdersController.cs` |
| **Allocations** | `AllocationRun` | Trigger Allocation Run | **None** | `POST /api/v1/allocation-runs/trigger` | `AllocationRunsController.cs` |
| **Picking** | `PickingList` | Confirm Picking Sheet | **None** | `POST /api/v1/picking-lists/{id}/confirm` | `WarehouseWorkController.cs` |
| **Shipments** | `Shipment` | Assign Driver & Vehicle | **None** | `PATCH /api/v1/shipments/{id}/assignment` | `ShipmentsController.cs` |
| **Shipments** | `Shipment` | Issue / Dispatch Shipment | **None** | `POST /api/v1/shipments/{id}/issue` | `ShipmentsController.cs` |
| **Receipts** | `ReceiptConfirmation` | Confirm Receipt | **None** | `POST /api/v1/receipt-confirmations` | `WarehouseWorkController.cs` |
| **Payments** | `Payment` | Collect Debt (Cash/Bank) | **None** | `POST /api/v1/payments/orders/{id}/collect` | `PaymentsController.cs` |
| **Payments** | `PaymentReceipt` | Create PayOS Payment URL | **None** | `POST /api/v1/payments/payos/checkout` | `PaymentReceiptsController.cs` |
| **Write-Offs** | `WriteOff` | Create Write-Off Request | **None** | `POST /api/v1/write-offs` | `DisposalsController.cs` |
| **Write-Offs** | `WriteOff` | Approve Write-Off | **None** | `POST /api/v1/write-offs/{id}/approve` | `DisposalsController.cs` |
| **Users** | `UserAccount` | Create User & Assign Roles | **None** | `POST /api/v1/users` | `UsersController.cs` |
| **Users** | `UserAccount` | Update User Assignments | **None** | `PUT /api/v1/users/{id}/assignments` | `UsersController.cs` |

---

## SECTION 6 – Authorization Matrix

### 6.1 Authentication Requirements
All GraphQL endpoints in the FSCM Backend require authentication:
- `SystemQuery.Entities` is decorated with `[Authorize]`. Unauthenticated requests to `/graphql` querying `entities` immediately receive HTTP 401 Unauthorized.
- Authentication is validated using ASP.NET Core JWT Bearer authentication.

### 6.2 Root Query Role Restrictions

| Resolver | Roles Allowed | Enforcement Mechanism | Failure Response |
| :--- | :--- | :--- | :--- |
| `entities` | Any Authenticated User | `[Authorize]` | HTTP 401 Unauthorized |
| `retailerReceivables` | `Retailer` | `[Authorize(Roles = [SystemRoles.Retailer])]` | GraphQL Authorization Error |
| `retailerReceivable` | `Retailer` | `[Authorize(Roles = [SystemRoles.Retailer])]` | GraphQL Authorization Error |
| `receivables` | `Admin`, `Operator`, `SalesManager`, `Sales` | `[Authorize(Roles = [...])]` | GraphQL Authorization Error |
| `salesKpis` | `Admin`, `Operator`, `SalesManager`, `Sales` | `[Authorize(Roles = [...])]` | GraphQL Authorization Error |
| `bestSellingProducts` | `Admin`, `Operator`, `SalesManager`, `Sales` | `[Authorize(Roles = [...])]` | GraphQL Authorization Error |
| `notifications` | Any Authenticated User | `[Authorize]` | HTTP 401 Unauthorized |
| `notificationBadges` | Any Authenticated User | `[Authorize]` | HTTP 401 Unauthorized |

### 6.3 Data-Access Scoping Matrix (`IEntityQueryService`)

| User Role | Access Level | Scoping Rules Applied by `EntityQueryService` |
| :--- | :--- | :--- |
| **Admin** | **Unrestricted** | Full read access across all 71 exposed entities (`AsNoTracking()`). |
| **Operator** | **Enterprise Scoped** | Unrestricted access across all operational data. **Denied** access to Admin-Only entities (`UserAccount`, `UserWarehouseAssignment`, `UserRegionAssignment`, `SalesGroupMember`, `AuditLog`, `SystemConfiguration`). |
| **Shared Master Data** | **Global Read** | 16 entities accessible by all authenticated roles without row filtering: `Warehouse`, `Region`, `SalesGroup`, `ProductCategory`, `Product`, `ProductPrice`, `Promotion`, `PromotionProduct`, `PromotionCategory`, `PromotionRetailer`, `TransitTable`, `TransitTableBand`, `ShelfLifeDiscountTable`, `ShelfLifeDiscountTier`, `LoyaltyProgram`, `LoyaltyReward`. |
| **Sales** | **Agent Scoped** | Orders: `createdByUserId == UserId || Scope.SalesGroupIds.Contains(SalesGroupId)`. Retailers: assigned `RetailerIds`. Payments/Lines: through scoped orders. |
| **SalesManager** | **Group Scoped** | Orders: `Scope.SalesGroupIds.Contains(SalesGroupId)`. Retailers: assigned `SalesGroupIds`. Payments/Lines: through scoped orders. |
| **Retailer** | **Self Scoped** | Orders: `Scope.RetailerIds.Contains(RetailerId)`. Payments & Receipts: `Scope.RetailerIds.Contains(RetailerId)`. Loyalty: `Scope.RetailerIds.Contains(RetailerId)`. |
| **WarehouseKeeper** | **Facility Scoped** | Inventory, Batches, Receipts, Pickings, Shipments, Transactions, WriteOffs: `Scope.WarehouseIds.Contains(WarehouseId)`. Orders: Scoped to orders that have active picking or allocation in the keeper's warehouses. |
| **Any Role (Unscoped Entity)** | **Denied by Default** | If an entity is requested that has no explicit mapping rule in `CreateScopedQuery()`, `EntityQueryService` returns `query.Where(_ => false)` (0 records). |

### 6.4 Field-Level Security Rules
In `src/FSCM.API/GraphQL/DomainEntityObjectType.cs`, security-sensitive fields are stripped during schema initialization:
- Any property containing `Hash` (e.g. `TokenHash`, `PasswordHash`, `RequestHash`) is explicitly ignored.
- Any property ending with `OperationKey` is explicitly ignored.

---

## SECTION 7 – Frontend Migration Readiness

### 7.1 Readiness Classification Rubric
- **G1 – FULL GRAPHQL READY:** Existing GraphQL implementation supports the required Frontend READ operations.
- **G2 – PARTIALLY GRAPHQL READY:** GraphQL supports some operations but additional REST requests are required for missing fields or relationships.
- **G3 – GRAPHQL QUERY AVAILABLE, MIGRATION REQUIRED:** GraphQL supports the necessary functionality but Frontend is still primarily using REST.
- **G4 – REST REQUIRED:** Required operations or business behavior are not currently available through GraphQL.
- **G5 – NOT EXPOSED:** Entity has no usable GraphQL query implementation.

### 7.2 Functional Capability & Migration Readiness Matrix

| Feature Area | Key Entities Involved | READ Readiness | WRITE Readiness | Current Frontend Pattern | Analysis & Migration Strategy |
| :--- | :--- | :---: | :---: | :--- | :--- |
| **Products Catalog** | `Product`, `ProductPrice`, `ProductCategory` | **G2** | **G4** | Hybrid: GraphQL `entities.products` + Batch `entities.productPrices` + Cached Categories | **Partially Ready.** List read is live via GraphQL. Detail read (`/products/{id}`) and image uploads remain REST. Client-side price join works cleanly. |
| **Warehouses** | `Warehouse`, `Region`, `UserWarehouseAssignment` | **G2** | **G4** | Hybrid: GraphQL `entities.warehouses` list & detail + REST fallback + REST keepers | **Partially Ready.** Warehouse list and detail by ID filter work in GraphQL. Keeper assignment enrichment requires REST `/users`. |
| **Retailers** | `Retailer`, `RetailerSalesAssignment`, `RetailerProductMlor` | **G3** | **G4** | REST (`/retailers`, `/retailers/{id}`) | **Query Available.** `entities.retailers`, `entities.retailerProfiles`, and `entities.retailerProfile` are fully functional in Backend. Frontend can migrate list and detail reads. |
| **Sales Orders** | `SalesOrder`, `SalesOrderLine`, `Allocation` | **G2** | **G4** | Hybrid: GraphQL `entities.salesOrders` list + REST `/orders/{id}` detail | **Partially Ready.** List read works via cursor pagination, but lacks REST-computed fields (`allocationState`, `counts`) - documented in frontend as `BLOCKER-07`. Detail read requires REST. |
| **Batches & Inventory** | `Batch`, `InventoryBalance`, `InventoryTransaction` | **G3** | **G4** | REST (`/inventory/batches`, `/inventory/balance`) | **Query Available.** `entities.batches` and `entities.inventoryBalances` are exposed with warehouse scoping. Frontend can migrate list queries. |
| **Order Picking** | `PickingList`, `PickingItem` | **G2** | **G4** | Hybrid: GraphQL `entities.pickingLists` list + client-side status counts | **Partially Ready.** Picking sheet list runs via GraphQL cursor pagination (`pickingGraphql.ts`). Detailed item verification and completion run via REST. |
| **Shipments & Logistics** | `Shipment`, `Delivery`, `Driver`, `Vehicle` | **G3** | **G4** | REST (`/shipments`, `/deliveries/drivers`) with small GraphQL helper (`exportOrders`) | **Query Available.** `entities.shipments` and `entities.deliveries` are available. Frontend can migrate shipment list and tracking queries. |
| **Receivables & Debt** | `Payment`, `PaymentReceipt`, `PaymentReceiptAllocation` | **G1** | **G4** | GraphQL `query Receivables { receivables { ... } }` in `debtApi.list()` | **Fully Ready (Read).** Debt list is already 100% GraphQL in production frontend. Collection execution is REST. |
| **Loyalty & Redemptions**| `LoyaltyProgram`, `LoyaltyReward`, `RewardRedemption` | **G3** | **G4** | REST (`/loyalty/redemptions`) | **Query Available.** Frontend codebase explicitly notes in comments that GraphQL migration is desired. `entities.rewardRedemptions` is ready. |
| **User Management & Audit** | `UserAccount`, `AuditLog`, `UserRegionAssignment` | **G1 (Audit) / G2 (User)** | **G4** | REST for users (`/users`), GraphQL for user audit (`entities.auditLogs`) | **Partially Ready.** User audit history is already 100% GraphQL in `users/api.ts`. User list uses REST due to role/assignment filtering. |
| **Promotions & Discounts** | `Promotion`, `PromotionProduct`, `ShelfLifeDiscountTable` | **G3** | **G4** | REST (`/promotions`, `/shelf-life-discounts`) | **Query Available.** All promotion and shelf-life discount tables are exposed as Shared Master Data. |
| **Write-Offs & Disposal**| `WriteOff`, `WriteOffItem`, `WriteOffAttachment` | **G3** | **G4** | REST (`/write-offs`) | **Query Available.** `entities.writeOffs` and `entities.writeOffItems` are ready for list migration. |
| **Warehouse Documents** | `WarehouseDocument`, `WarehouseDocumentLine` | **G5** | **G4** | REST (`/warehouse-documents`) | **Not Exposed.** Warehouse receipt/issue documents are unexposed in GraphQL. REST required for both read and write. |
| **Factory Traceability** | `FactoryProductionBatch`, `FactoryPallet` | **G5** | **G4** | REST / Internal Gateway | **Not Exposed.** Resides in separate `TraceabilityDbContext`. Not registered in GraphQL schema. |

---

## SECTION 8 – Missing GraphQL Capabilities

### 8.1 Missing Computed Fields on Key Entities
- **SalesOrder (The BLOCKER-07 Problem):**
  - `indicativeAmount` (currently derived client-side as equal to `finalAmount`).
  - `officialAmount` (currently derived client-side as `finalPricePendingAllocation ? null : finalAmount`).
  - `allocationState` (string enum representing allocation status: NotAllocated, PartiallyAllocated, Allocated).
  - `allocationResult` (complex allocation outcome summary).
  - `allocationRunId` (GUID of the allocation run that processed this order).
  - `allocationProposalStatus` (proposal review state).
  - `shipmentCount`, `totalCartons`, `lineCount` (aggregated item and packaging metrics).
- **Warehouse:**
  - `assignedKeeperCount` / `keepers` (requires join with `UserWarehouseAssignment` and `UserAccount`).
  - `currentStorageUtilization` (requires aggregation over `InventoryBalance`).
- **Batch:**
  - `productName`, `productSku`, `categoryName` (requires join with `Product` and `ProductCategory`).
  - `daysToExpiration` / `isExpired` (dynamic date calculations).

### 8.2 Missing Relationship Navigations (Zero Child Graph Resolution)
- Inability to execute nested queries:
  ```graphql
  # CURRENTLY UNSUPPORTED (Schema Error):
  entities {
    products {
      nodes {
        name
        category { name }          # FAILS: field does not exist
        currentPrice { unitPrice } # FAILS: field does not exist
      }
    }
  }
  ```

### 8.3 Missing Search & Filter Features
- **Full-Text & Unaccent Search:** HotChocolate's `contains` operator generates PostgreSQL `ILIKE '%...%'`. It does not support unaccented search (crucial for Vietnamese text with diacritics like `sữa`, `đường`).
- **Cross-Entity Filtering:** Cannot filter orders by product name or filter batches by category code directly in the GraphQL query without complex sub-queries.

### 8.4 Complete Mutation Absence
- The GraphQL schema exposes 0 mutations. Any frontend feature requiring user input, approval, or modification must maintain duplicate API client infrastructure (REST + GraphQL).

---

## SECTION 9 – Recommended Migration Roadmap

Based on existing Backend capabilities and Frontend readiness, the following phased migration roadmap is recommended:

```mermaid
flowchart LR
    P1["Phase 1: Zero-Backend Read Migration"] --> P2["Phase 2: Hybrid Gap Resolution"]
    P2 --> P3["Phase 3: Operations & Warehouse Read Consolidation"]
    P3 --> P4["Phase 4: GraphQL Write Evolution (Optional)"]
```

### Phase 1: High-Value Read Migrations (Zero Backend Code Changes Required)
The Backend already exposes the necessary entities with cursor pagination and role scoping. Frontend can immediately migrate the following features from REST to GraphQL:
1. **Retailer Management (`entities.retailers`):** Migrate Retailers list page and detail view. Replace REST `/retailers`.
2. **Gift Redemption Management (`entities.rewardRedemptions`):** Migrate `/loyalty/redemptions` list query. (Directly fulfills developer TODO in `src/features/redeem/api.ts`).
3. **Disposal & Write-Offs (`entities.writeOffs`):** Migrate Write-off list view from REST `/write-offs`.
4. **Shipment Tracking (`entities.shipments`):** Expand GraphQL usage from `exportOrders` to the full shipments dashboard list.
5. **Promotions & Discounts Catalog (`entities.promotions`):** Migrate promotional campaigns overview from REST.

### Phase 2: Resolving Hybrid Read Gaps (Minor Backend Enhancements Required)
1. **Resolve `BLOCKER-07` on SalesOrder:** Add an extension ObjectType or computed property on `SalesOrderEntity` to expose `allocationState`, `allocationRunId`, and line/shipment counts directly in GraphQL.
2. **Introduce High-Frequency Child Resolvers:** Add HotChocolate field resolvers on `DomainEntityObjectType<Product>` for `category` (`ProductCategoryEntity`) and `activePrice` (`ProductPriceEntity`) backed by `IDataLoader<Guid, T>` to eliminate frontend client-side joins.
3. **Warehouse Keeper Extension:** Add a resolver on `WarehouseEntity` for `assignedKeepers` returning `[UserAccountEntity]`.

### Phase 3: Operations & Warehouse Read Consolidation
1. **Expose Warehouse Documents:** Add `WarehouseDocument` and `WarehouseDocumentLine` to `EntityQuery.cs` to eliminate REST read requirements on warehouse document receipts.
2. **Inventory Stock Balance Consolidation:** Migrate Batches and Inventory Balance views to GraphQL with server-side joins.

### Phase 4: Long-Term Architecture Decision on Mutations
- **Option A (Recommended CQRS Pattern):** Retain REST for all writes and GraphQL for all reads. REST controllers already have robust FluentValidation, domain event dispatching, transactional outboxes, and idempotency header verification.
- **Option B (Full GraphQL Evolution):** Implement HotChocolate `MutationType` with dedicated command inputs for high-frequency workflows (Order placement, Picking confirmation).

---

## SECTION 10 – Backend Dependency Backlog

### 10.1 Required Changes (Blockers for Complete GraphQL Migration)

#### Backlog Item BE-GQL-01: SalesOrder Summary Projection (Resolve BLOCKER-07)
- **Priority:** High / Blocker
- **Impact:** Enables Frontend Orders page to display allocation state, allocation run ID, shipment counts, and item counts without falling back to REST.
- **Implementation Plan:** Extend `DomainEntityObjectType<SalesOrder>` in `FSCM.API` with computed field resolvers for:
  - `allocationState`: Query `AllocationRunAllocationRecords` / `AllocationRecords`.
  - `allocationRunId`: Fetch active run ID.
  - `lineCount`: Sub-query `SalesOrderLineRecords.Count(line => line.SalesOrderId == parent.Id)`.
  - `shipmentCount`: Sub-query `ShipmentRecords.Count(s => s.SalesOrderId == parent.Id)`.

#### Backlog Item BE-GQL-02: Product Category & Active Price Field Resolvers
- **Priority:** High
- **Impact:** Eliminates the two-step batching pattern (`fetchProductPrices`) in Frontend `productsGraphql.ts`.
- **Implementation Plan:**
  - Add `descriptor.Field("category").ResolveWith<ProductResolvers>(r => r.GetCategoryAsync(...))` using HotChocolate DataLoader.
  - Add `descriptor.Field("currentPrice").ResolveWith<ProductResolvers>(r => r.GetCurrentPriceAsync(...))`.

#### Backlog Item BE-GQL-03: Vietnamese Unaccent Text Search Filter
- **Priority:** Medium
- **Impact:** Fixes search usability for products, retailers, and users when users type unaccented search terms.
- **Implementation Plan:** Configure custom HotChocolate filter convention or map PostgreSQL `unaccent(lower(text)) LIKE unaccent(lower('%search%'))`.

### 10.2 Optional Improvements (Technical Debt & Scalability)

#### Backlog Item BE-GQL-04: Batch DataLoader Infrastructure
- **Priority:** Medium
- **Impact:** Prevents N+1 database queries when child navigation resolvers are added.
- **Implementation Plan:** Register `BatchDataLoader<Guid, TEntity>` for `ProductCategory`, `Warehouse`, `Region`, and `ProductPrice` using `GreenDonut`.

#### Backlog Item BE-GQL-05: Expose Warehouse Document Read Model
- **Priority:** Low
- **Impact:** Allows warehouse receipt documents to be queried with cursor pagination.
- **Implementation Plan:** Add `[UsePaging] public IQueryable<WarehouseDocument> WarehouseDocuments(...)` to `EntityQuery.cs`.

#### Backlog Item BE-GQL-06: HotChocolate Complexity & Cost Analysis Fine-Tuning
- **Priority:** Low
- **Impact:** Resolves `HC0047` limitation where Frontend must write literal `first: 20` instead of GraphQL variables.
- **Implementation Plan:** Adjust `AddCostAnalysis` and paging cost rules in `ServiceCollectionExtensions.cs`.

---

### Verification & Sign-off
**Architectural Audit Completed by:** Senior .NET Backend Architect  
**Repositories Verified:** `FSCM-Backend` (Source of Truth) & `FSCM-Frontend` (Reference)  
**Compliance:** Zero source modifications performed; 100% read-only verification.