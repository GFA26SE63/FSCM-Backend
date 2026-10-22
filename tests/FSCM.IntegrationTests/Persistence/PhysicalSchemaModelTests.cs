using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.IntegrationTests.Persistence;

public sealed class PhysicalSchemaModelTests
{
    private static readonly string[] ExpectedTables =
    [
        "ALLOCATION",
        "AUDIT_LOG",
        "BATCH",
        "BATCH_PROMOTION",
        "BATCH_QR_LABEL",
        "BUSINESS_DOCUMENT_SEQUENCE",
        "DELIVERY",
        "DRIVER",
        "GOODS_RECEIPT",
        "GOODS_RECEIPT_LINE",
        "IDEMPOTENCY_KEY",
        "INVENTORY_BALANCE",
        "INVENTORY_TRANSACTION",
        "LOYALTY_PROGRAM",
        "LOYALTY_REWARD",
        "LOYALTY_TRANSACTION",
        "MAP_DISTANCE_CACHE",
        "MAP_LOCATION",
        "NOTIFICATION",
        "ORDER_PROMOTION",
        "ORDER_STATUS_HISTORY",
        "OUTBOX_MESSAGE",
        "PAYMENT",
        "PICKING_ITEM",
        "PICKING_LIST",
        "PRODUCT",
        "PRODUCT_CATEGORY",
        "PRODUCT_PRICE",
        "PROMOTION",
        "PROMOTION_CATEGORY",
        "PROMOTION_PRODUCT",
        "PROMOTION_RETAILER",
        "QR_LABEL_EVENT",
        "RECEIPT_CONFIRMATION",
        "RECEIPT_CONFIRMATION_ITEM",
        "RECEIPT_DISCREPANCY",
        "RECEIPT_DISCREPANCY_ITEM",
        "REGION",
        "RESERVATION",
        "RETAILER",
        "RETAILER_LOYALTY",
        "RETAILER_PRODUCT_MLOR",
        "RETAILER_SALES_ASSIGNMENT",
        "REWARD_REDEMPTION",
        "SALES_GROUP",
        "SALES_GROUP_MEMBER",
        "SALES_ORDER",
        "SALES_ORDER_ATTACHMENT",
        "SALES_ORDER_LINE",
        "STOCK_HOLD",
        "SYSTEM_CONFIGURATION",
        "TRANSIT_TABLE",
        "TRANSIT_TABLE_BAND",
        "TRANSIT_TIME_RULE",
        "USER_ACCOUNT",
        "USER_REGION_ASSIGNMENT",
        "USER_WAREHOUSE_ASSIGNMENT",
        "VEHICLE",
        "WAREHOUSE",
        "WAREHOUSE_TRANSFER",
        "WAREHOUSE_TRANSFER_EVENT",
        "WAREHOUSE_TRANSFER_ITEM",
        "WAREHOUSE_TRANSFER_LABEL",
        "WRITE_OFF",
        "WRITE_OFF_ATTACHMENT",
        "WRITE_OFF_ITEM"
    ];

    [Fact]
    public void Model_matches_approved_physical_erd_shape()
    {
        using var context = CreateContext();

        var entityTypes = context.Model.GetEntityTypes()
            .Where(entityType => ExpectedTables.Contains(entityType.GetTableName(), StringComparer.Ordinal))
            .ToArray();
        var tableNames = entityTypes
            .Select(entityType => entityType.GetTableName())
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedTables, tableNames);
        Assert.Equal(66, entityTypes.Length);
        Assert.Equal(708, entityTypes.Sum(entityType => entityType.GetProperties().Count()));
        Assert.Equal(150, entityTypes.Sum(entityType => entityType.GetForeignKeys().Count()));
        Assert.All(
            entityTypes.SelectMany(entityType => entityType.GetForeignKeys()),
            foreignKey => Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));
    }

    [Fact]
    public void Model_enforces_delivery_and_receipt_idempotency()
    {
        using var context = CreateContext();

        var delivery = context.Model.FindEntityType(typeof(Delivery))!;
        Assert.True(delivery.FindProperty(nameof(Delivery.Version))!.IsConcurrencyToken);
        Assert.True(delivery.GetIndexes().Single(index => index.Properties
            .Single().Name == nameof(Delivery.SalesOrderId)).IsUnique);
        Assert.True(delivery.GetIndexes().Single(index => index.Properties
            .Single().Name == nameof(Delivery.OperationKey)).IsUnique);

        var receipt = context.Model.FindEntityType(typeof(ReceiptConfirmation))!;
        Assert.True(receipt.GetIndexes().Single(index => index.Properties
            .Single().Name == nameof(ReceiptConfirmation.OperationKey)).IsUnique);

        var driver = context.Model.FindEntityType(typeof(Driver))!;
        Assert.True(driver.GetIndexes().Single(index => index.Properties
            .Single().Name == nameof(Driver.Code)).IsUnique);
    }

    [Fact]
    public void Model_enforces_single_idempotent_payment_per_order()
    {
        using var context = CreateContext();

        var payment = context.Model.FindEntityType(typeof(Payment))!;
        Assert.True(payment.FindProperty(nameof(Payment.Version))!.IsConcurrencyToken);
        Assert.True(payment.GetIndexes().Single(index => index.Properties.Count == 1 && index.Properties[0].Name ==
            nameof(Payment.SalesOrderId)).IsUnique);
        var operationIndex = payment.GetIndexes().Single(index => index.Properties.Count == 1 &&
            index.Properties[0].Name == nameof(Payment.OperationKey));
        Assert.True(operationIndex.IsUnique);
        Assert.Equal("\"OperationKey\" IS NOT NULL", operationIndex.GetFilter());
        foreach (var propertyName in new[]
                 {
                     nameof(Payment.PayOsOrderCode),
                     nameof(Payment.PayOsPaymentLinkId),
                     nameof(Payment.PayOsReference),
                     nameof(Payment.PayOsWebhookHash)
                 })
        {
            var index = payment.GetIndexes().Single(candidate => candidate.Properties.Count == 1 &&
                candidate.Properties[0].Name == propertyName);
            Assert.True(index.IsUnique);
            Assert.Equal($"\"{propertyName}\" IS NOT NULL", index.GetFilter());
        }

        Assert.False(payment.FindProperty(nameof(Payment.ConfirmedAmount))!.IsNullable);
        Assert.False(payment.FindProperty(nameof(Payment.FinalApprovedAt))!.IsNullable);
        Assert.False(payment.FindProperty(nameof(Payment.FinalApprovalReference))!.IsNullable);
        Assert.True(payment.FindProperty(nameof(Payment.DeliveredDate))!.IsNullable);

        var manualPayment = context.Model.FindEntityType(typeof(ManualPayment))!;
        Assert.True(manualPayment.FindProperty(nameof(ManualPayment.Version))!.IsConcurrencyToken);
        Assert.True(manualPayment.GetIndexes().Single(index => index.Properties.Count == 1 &&
            index.Properties[0].Name == nameof(ManualPayment.OperationKey)).IsUnique);
    }

    [Fact]
    public void Model_contains_identity_authentication_tables()
    {
        using var context = CreateContext();

        var identityTables = context.Model.GetEntityTypes()
            .Select(entityType => entityType.GetTableName())
            .Where(tableName => tableName is "ACCOUNT_TOKEN" or "REFRESH_TOKEN" or "USER_CREDENTIAL")
            .Select(tableName => tableName!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["ACCOUNT_TOKEN", "REFRESH_TOKEN", "USER_CREDENTIAL"], identityTables);

        var credential = context.Model.FindEntityType(typeof(UserCredential));
        var userAccountId = credential!.FindProperty(nameof(UserCredential.UserAccountId));
        Assert.True(credential.GetIndexes().Single(index => index.Properties.Contains(userAccountId!)).IsUnique);

        var refreshToken = context.Model.FindEntityType(typeof(RefreshToken));
        var tokenHash = refreshToken!.FindProperty(nameof(RefreshToken.TokenHash));
        Assert.True(refreshToken.GetIndexes().Single(index => index.Properties.Contains(tokenHash!)).IsUnique);
    }

    [Fact]
    public void Model_preserves_representative_postgresql_facets()
    {
        using var context = CreateContext();

        var product = context.Model.FindEntityType(typeof(Product));
        var sku = product!.FindProperty(nameof(Product.Sku));
        Assert.Equal(50, sku!.GetMaxLength());
        Assert.True(product.GetIndexes().Single(index => index.Properties.Contains(sku)).IsUnique);
        var replacesProductId = product.FindProperty(nameof(Product.ReplacesProductId));
        Assert.True(product.GetIndexes().Single(index => index.Properties.Contains(replacesProductId!)).IsUnique);

        var inventoryBalance = context.Model.FindEntityType(typeof(InventoryBalance));
        var physicalQuantity = inventoryBalance!.FindProperty(nameof(InventoryBalance.PhysicalQuantity));
        Assert.Equal(18, physicalQuantity!.GetPrecision());
        Assert.Equal(3, physicalQuantity.GetScale());

        var auditLog = context.Model.FindEntityType(typeof(AuditLog));
        Assert.Equal("jsonb", auditLog!.FindProperty(nameof(AuditLog.OldValues))!.GetColumnType());
        Assert.Equal("jsonb", auditLog.FindProperty(nameof(AuditLog.NewValues))!.GetColumnType());
    }

    [Fact]
    public void Model_enforces_current_territory_assignments_and_order_creator_snapshot()
    {
        using var context = CreateContext();

        var membership = context.Model.FindEntityType(typeof(SalesGroupMember))!;
        var memberUserId = membership.FindProperty(nameof(SalesGroupMember.UserAccountId))!;
        var memberIndex = membership.GetIndexes().Single(index => index.Properties.SequenceEqual([memberUserId]));
        Assert.True(memberIndex.IsUnique);
        Assert.Equal("\"IsActive\" = TRUE", memberIndex.GetFilter());

        var retailerAssignment = context.Model.FindEntityType(typeof(RetailerSalesAssignment))!;
        var retailerId = retailerAssignment.FindProperty(nameof(RetailerSalesAssignment.RetailerId))!;
        var retailerIndex = retailerAssignment.GetIndexes().Single(index => index.Properties.SequenceEqual([retailerId]));
        Assert.True(retailerIndex.IsUnique);
        Assert.Equal("\"IsActive\" = TRUE", retailerIndex.GetFilter());

        var order = context.Model.FindEntityType(typeof(SalesOrder))!;
        Assert.False(order.FindProperty(nameof(SalesOrder.CreatedByUserId))!.IsNullable);
        Assert.False(order.FindProperty(nameof(SalesOrder.SalesGroupId))!.IsNullable);

        var userRegion = context.Model.FindEntityType(typeof(UserRegionAssignment))!;
        var regionUserId = userRegion.FindProperty(nameof(UserRegionAssignment.UserAccountId))!;
        var assignedRegionId = userRegion.FindProperty(nameof(UserRegionAssignment.RegionId))!;
        Assert.True(userRegion.GetIndexes().Single(index =>
            index.Properties.SequenceEqual([regionUserId, assignedRegionId])).IsUnique);

        var userAccount = context.Model.FindEntityType(typeof(UserAccount))!;
        Assert.True(userAccount.FindProperty(nameof(UserAccount.EntraObjectId))!.IsNullable);
        Assert.False(userAccount.FindProperty(nameof(UserAccount.IdentityProvider))!.IsNullable);
    }

    [Fact]
    public void Model_contains_retailer_evaluation_and_payment_term_fields()
    {
        using var context = CreateContext();
        var retailer = context.Model.FindEntityType(typeof(Retailer))!;

        Assert.False(retailer.FindProperty(nameof(Retailer.RetailerType))!.IsNullable);
        Assert.False(retailer.FindProperty(nameof(Retailer.StoreSizeCategory))!.IsNullable);
        Assert.False(retailer.FindProperty(nameof(Retailer.PurchaseFrequencyPerMonth))!.IsNullable);
        Assert.False(retailer.FindProperty(nameof(Retailer.CreditTermDays))!.IsNullable);
        Assert.True(retailer.FindProperty(nameof(Retailer.RequestedCreditTermDays))!.IsNullable);
    }

    [Fact]
    public void Model_versions_map_distance_and_transit_inputs()
    {
        using var context = CreateContext();
        var distance = context.Model.FindEntityType(typeof(MapDistanceCache))!;
        var warehouseId = distance.FindProperty(nameof(MapDistanceCache.WarehouseId))!;
        var retailerId = distance.FindProperty(nameof(MapDistanceCache.RetailerId))!;
        Assert.True(distance.GetIndexes().Single(index =>
            index.Properties.SequenceEqual([warehouseId, retailerId]) &&
            index.GetFilter() == "\"IsActive\" = TRUE").IsUnique);

        var location = context.Model.FindEntityType(typeof(MapLocation))!;
        Assert.False(location.FindProperty(nameof(MapLocation.Version))!.IsNullable);
        var transit = context.Model.FindEntityType(typeof(TransitTimeRule))!;
        Assert.False(transit.FindProperty(nameof(TransitTimeRule.Version))!.IsNullable);
        Assert.False(transit.FindProperty(nameof(TransitTimeRule.EffectiveFrom))!.IsNullable);
        Assert.False(transit.FindProperty(nameof(TransitTimeRule.EstimatedTransportCost))!.IsNullable);

        var warehouse = context.Model.FindEntityType(typeof(Warehouse))!;
        Assert.False(warehouse.FindProperty(nameof(Warehouse.MinimumStockThresholdCartons))!.IsNullable);

        var mlor = context.Model.FindEntityType(typeof(RetailerProductMlor))!;
        Assert.True(mlor.FindProperty(nameof(RetailerProductMlor.ProductCategoryId))!.IsNullable);
        Assert.True(mlor.FindProperty(nameof(RetailerProductMlor.ProductId))!.IsNullable);
        Assert.False(mlor.FindProperty(nameof(RetailerProductMlor.Version))!.IsNullable);
        Assert.False(mlor.FindProperty(nameof(RetailerProductMlor.UpdatedByUserId))!.IsNullable);
    }

    [Fact]
    public void Model_uses_date_only_expiry_and_optimistic_inventory_versions()
    {
        using var context = CreateContext();
        var batch = context.Model.FindEntityType(typeof(Batch))!;
        var balance = context.Model.FindEntityType(typeof(InventoryBalance))!;
        var receiptLine = context.Model.FindEntityType(typeof(GoodsReceiptLine))!;

        Assert.Equal("date", batch.FindProperty(nameof(Batch.ExpirationDate))!.GetColumnType());
        Assert.Equal("date", receiptLine.FindProperty(nameof(GoodsReceiptLine.ExpirationDate))!.GetColumnType());
        Assert.True(batch.FindProperty(nameof(Batch.Version))!.IsConcurrencyToken);
        Assert.True(balance.FindProperty(nameof(InventoryBalance.Version))!.IsConcurrencyToken);
        Assert.Null(balance.FindProperty("HeldQuantity"));
        var transaction = context.Model.FindEntityType(typeof(InventoryTransaction))!;
        Assert.Null(transaction.FindProperty("HeldQuantityDelta"));
        Assert.Null(transaction.FindProperty("HeldQuantityAfter"));
        Assert.False(transaction.FindProperty(nameof(InventoryTransaction.SourceType))!.IsNullable);
        Assert.False(transaction.FindProperty(nameof(InventoryTransaction.SourceId))!.IsNullable);

        var expiryRun = context.Model.FindEntityType(typeof(ExpirySweepRun))!;
        Assert.Equal("date", expiryRun.FindProperty(nameof(ExpirySweepRun.BusinessDate))!.GetColumnType());

        var notification = context.Model.FindEntityType(typeof(Notification))!;
        var userId = notification.FindProperty(nameof(Notification.UserAccountId))!;
        var outboxId = notification.FindProperty(nameof(Notification.OutboxMessageId))!;
        Assert.True(notification.GetIndexes().Single(index =>
            index.Properties.SequenceEqual([userId, outboxId])).IsUnique);
    }

    [Fact]
    public void Model_enforces_qr_lineage_and_duplicate_operation_protection()
    {
        using var context = CreateContext();
        var label = context.Model.FindEntityType(typeof(BatchQrLabel))!;
        var labelEvent = context.Model.FindEntityType(typeof(QrLabelEvent))!;

        Assert.True(label.FindProperty(nameof(BatchQrLabel.Version))!.IsConcurrencyToken);
        Assert.False(label.FindProperty(nameof(BatchQrLabel.CurrentWarehouseId))!.IsNullable);
        Assert.NotNull(label.FindProperty(nameof(BatchQrLabel.ParentLabelId)));
        Assert.NotNull(label.FindProperty(nameof(BatchQrLabel.RootLabelId)));
        var operationKey = labelEvent.FindProperty(nameof(QrLabelEvent.OperationKey))!;
        Assert.True(labelEvent.GetIndexes().Single(index => index.Properties.SequenceEqual([operationKey])).IsUnique);
    }

    [Fact]
    public void Model_enforces_transfer_concurrency_idempotency_and_label_binding()
    {
        using var context = CreateContext();
        var transfer = context.Model.FindEntityType(typeof(WarehouseTransfer))!;
        var transferEvent = context.Model.FindEntityType(typeof(WarehouseTransferEvent))!;
        var transferLabel = context.Model.FindEntityType(typeof(WarehouseTransferLabel))!;

        Assert.True(transfer.FindProperty(nameof(WarehouseTransfer.Version))!.IsConcurrencyToken);
        var operationKey = transferEvent.FindProperty(nameof(WarehouseTransferEvent.OperationKey))!;
        Assert.True(transferEvent.GetIndexes().Single(index => index.Properties.SequenceEqual([operationKey])).IsUnique);
        var itemId = transferLabel.FindProperty(nameof(WarehouseTransferLabel.WarehouseTransferItemId))!;
        var labelId = transferLabel.FindProperty(nameof(WarehouseTransferLabel.BatchQrLabelId))!;
        Assert.True(transferLabel.GetIndexes().Single(index => index.Properties.SequenceEqual([itemId, labelId])).IsUnique);
    }

    [Fact]
    public void Model_enforces_unique_promotion_scopes_and_business_code_sequence()
    {
        using var context = CreateContext();
        var productLink = context.Model.FindEntityType(typeof(PromotionProduct))!;
        var categoryLink = context.Model.FindEntityType(typeof(PromotionCategory))!;
        var retailerLink = context.Model.FindEntityType(typeof(PromotionRetailer))!;
        var batchLink = context.Model.FindEntityType(typeof(BatchPromotion))!;

        Assert.Contains(productLink.GetIndexes(), index => index.IsUnique && index.Properties.Count == 2);
        Assert.Contains(categoryLink.GetIndexes(), index => index.IsUnique && index.Properties.Count == 2);
        Assert.Contains(retailerLink.GetIndexes(), index => index.IsUnique && index.Properties.Count == 2);
        Assert.Contains(batchLink.GetIndexes(), index => index.IsUnique && index.Properties.Count == 2);
        Assert.Contains(context.Model.GetSequences(), sequence => sequence.Name == "PromotionCodeSequence");
    }

    [Fact]
    public void Model_preserves_assisted_order_snapshots_trace_and_concurrency()
    {
        using var context = CreateContext();
        var order = context.Model.FindEntityType(typeof(SalesOrder))!;
        var line = context.Model.FindEntityType(typeof(SalesOrderLine))!;
        var orderPromotion = context.Model.FindEntityType(typeof(OrderPromotion))!;
        var allocation = context.Model.FindEntityType(typeof(Allocation))!;
        var retailer = context.Model.FindEntityType(typeof(Retailer))!;

        Assert.Equal("jsonb", order.FindProperty(nameof(SalesOrder.AllocationTraceJson))!.GetColumnType());
        Assert.True(order.FindProperty(nameof(SalesOrder.Version))!.IsConcurrencyToken);
        Assert.False(order.FindProperty(nameof(SalesOrder.RetailerCodeSnapshot))!.IsNullable);
        Assert.False(order.FindProperty(nameof(SalesOrder.RetailerNameSnapshot))!.IsNullable);
        Assert.False(order.FindProperty(nameof(SalesOrder.FinalPricePendingAllocation))!.IsNullable);
        Assert.False(line.FindProperty(nameof(SalesOrderLine.FinalPricePendingAllocation))!.IsNullable);
        Assert.False(line.FindProperty(nameof(SalesOrderLine.ProductPriceId))!.IsNullable);
        Assert.False(line.FindProperty(nameof(SalesOrderLine.MlorSourceScope))!.IsNullable);
        Assert.False(line.FindProperty(nameof(SalesOrderLine.MlorSourceVersion))!.IsNullable);
        Assert.False(line.FindProperty(nameof(SalesOrderLine.MlorMinimumLifeRatio))!.IsNullable);
        Assert.False(orderPromotion.FindProperty(nameof(OrderPromotion.PromotionCodeSnapshot))!.IsNullable);
        Assert.False(orderPromotion.FindProperty(nameof(OrderPromotion.EffectiveFromSnapshot))!.IsNullable);

        var orderId = line.FindProperty(nameof(SalesOrderLine.SalesOrderId))!;
        var lineNumber = line.FindProperty(nameof(SalesOrderLine.LineNumber))!;
        Assert.True(line.GetIndexes().Single(index =>
            index.Properties.SequenceEqual([orderId, lineNumber])).IsUnique);

        var salesOrderLineId = allocation.FindProperty(nameof(Allocation.SalesOrderLineId))!;
        var allocationSequence = allocation.FindProperty(nameof(Allocation.AllocationSequence))!;
        Assert.True(allocation.GetIndexes().Single(index =>
            index.Properties.SequenceEqual([salesOrderLineId, allocationSequence])).IsUnique);
        Assert.NotNull(allocation.FindProperty(nameof(Allocation.PromotionId)));

        var retailerUserId = retailer.FindProperty(nameof(Retailer.UserAccountId))!;
        Assert.True(retailer.GetIndexes().Single(index =>
            index.Properties.SequenceEqual([retailerUserId])).IsUnique);
        Assert.Contains(context.Model.GetSequences(), sequence => sequence.Name == "SalesOrderCodeSequence");
    }

    [Fact]
    public void Model_enforces_one_versioned_picking_list_per_order_warehouse_and_allocation()
    {
        using var context = CreateContext();
        var list = context.Model.FindEntityType(typeof(PickingList))!;
        var item = context.Model.FindEntityType(typeof(PickingItem))!;

        Assert.True(list.FindProperty(nameof(PickingList.Version))!.IsConcurrencyToken);
        var orderId = list.FindProperty(nameof(PickingList.SalesOrderId))!;
        var warehouseId = list.FindProperty(nameof(PickingList.WarehouseId))!;
        Assert.True(list.GetIndexes().Single(index =>
            index.Properties.SequenceEqual([orderId, warehouseId])).IsUnique);

        var allocationId = item.FindProperty(nameof(PickingItem.AllocationId))!;
        Assert.True(item.GetIndexes().Single(index =>
            index.Properties.SequenceEqual([allocationId])).IsUnique);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=fscm_model_validation;Username=fscm;Password=not-used")
            .Options;

        return new AppDbContext(options);
    }
}
