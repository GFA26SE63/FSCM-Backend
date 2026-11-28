using System.Linq.Expressions;
using FSCM.Application.Features.EntityQueries;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.EntityQueries;

public sealed class EntityQueryService(AppDbContext dbContext) : IEntityQueryService
{
    public IQueryable<TEntity> Query<TEntity>(EntityQueryAccess access) where TEntity : class =>
        OrderByPrimaryKey(QueryWithGrants<TEntity>(access));

    private IQueryable<TEntity> OrderByPrimaryKey<TEntity>(IQueryable<TEntity> query) where TEntity : class
    {
        var key = dbContext.Model.FindEntityType(typeof(TEntity))?.FindPrimaryKey()
            ?? throw new InvalidOperationException($"Entity '{typeof(TEntity).Name}' must have a primary key for paging.");
        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        var expression = query.Expression;
        for (var index = 0; index < key.Properties.Count; index++)
        {
            var property = key.Properties[index];
            var selector = Expression.Lambda(
                Expression.Call(typeof(EF), nameof(EF.Property), [property.ClrType],
                    parameter, Expression.Constant(property.Name)), parameter);
            expression = Expression.Call(typeof(Queryable),
                index == 0 ? nameof(Queryable.OrderBy) : nameof(Queryable.ThenBy),
                [typeof(TEntity), property.ClrType], expression, Expression.Quote(selector));
        }
        return query.Provider.CreateQuery<TEntity>(expression);
    }

    private IQueryable<TEntity> QueryWithGrants<TEntity>(EntityQueryAccess access) where TEntity : class
    {
        var query = QueryScoped<TEntity>(access);
        if (access.Role == SystemRoles.Admin || access.Permissions is null) return query;
        var grants = access.Permissions;
        var operations = grants.Contains(SystemScreenKeys.MobileWkOperations);
        var orders = grants.Contains(SystemScreenKeys.WarehouseOutbound) || grants.Contains(SystemScreenKeys.MobileWkPicking);
        var shipped = grants.Contains(SystemScreenKeys.MobileWkShipped);
        var transfers = grants.Contains(SystemScreenKeys.WarehouseTransfers);
        var factoryRequests = grants.Contains(SystemScreenKeys.WarehouseFactoryRequests);
        var inbound = grants.Contains(SystemScreenKeys.WarehouseInbound);
        var documentIds = dbContext.WarehouseDocuments.Where(d => operations ||
            (d.Reason == "Order" && (orders || (shipped && d.Status == "Posted"))) ||
            (d.Reason == "Transfer" && transfers) ||
            (d.Reason == "Factory" && ((d.Kind == "Request" && factoryRequests) || (d.Kind != "Request" && inbound))))
            .Select(d => d.Id);
        if (typeof(TEntity) == typeof(WarehouseDocument))
            return Cast<TEntity, WarehouseDocument>(((IQueryable<WarehouseDocument>)(object)query).Where(d => documentIds.Contains(d.Id)));
        if (typeof(TEntity) == typeof(WarehouseDocumentLine))
            return Cast<TEntity, WarehouseDocumentLine>(((IQueryable<WarehouseDocumentLine>)(object)query).Where(d => documentIds.Contains(d.DocumentId)));
        if (typeof(TEntity) == typeof(WarehouseDocumentCarton))
            return Cast<TEntity, WarehouseDocumentCarton>(((IQueryable<WarehouseDocumentCarton>)(object)query).Where(d => documentIds.Contains(d.DocumentId)));
        return query;
    }

    private IQueryable<TEntity> QueryScoped<TEntity>(EntityQueryAccess access) where TEntity : class
    {
        var query = dbContext.Set<TEntity>().AsNoTracking();
        // Notifications are a private inbox.  Administrative access to other
        // entities must never turn this into a system-wide notification feed.
        if (typeof(TEntity) == typeof(Notification))
            return Cast<TEntity, Notification>(dbContext.NotificationRecords
                .AsNoTracking()
                .Where(x => x.UserAccountId == access.UserId));
        if (access.Role == SystemRoles.Admin)
            return query;
        if (access.Role == SystemRoles.Operator && !IsAdminOnlyEntity<TEntity>())
            return query;

        // Administrative reads are global only when explicitly delegated. Without
        // the corresponding operation grant the normal ownership scope still applies.
        if (HasGlobalManagementGrant<TEntity>(access.Permissions)) return query;

        if (IsSharedMasterData<TEntity>())
            return query;

        var scoped = CreateScopedQuery<TEntity>(access);
        return scoped ?? query.Where(_ => false);
    }

    private IQueryable<TEntity>? CreateScopedQuery<TEntity>(EntityQueryAccess access) where TEntity : class
    {
        var warehouseIds = access.Scope.WarehouseIds;
        var regionIds = access.Scope.RegionIds;
        var salesGroupIds = access.Scope.SalesGroupIds;
        var retailerIds = access.Scope.RetailerIds;
        var orderIds = ScopedOrders(access).Select(order => order.Id);

        if (typeof(TEntity) == typeof(UserAccount))
            return Cast<TEntity, UserAccount>(dbContext.UserAccountRecords.Where(x => x.Id == access.UserId));
        if (typeof(TEntity) == typeof(UserWarehouseAssignment))
            return Cast<TEntity, UserWarehouseAssignment>(dbContext.UserWarehouseAssignmentRecords.Where(x => x.UserAccountId == access.UserId));
        if (typeof(TEntity) == typeof(UserRegionAssignment))
            return Cast<TEntity, UserRegionAssignment>(dbContext.UserRegionAssignmentRecords.Where(x => x.UserAccountId == access.UserId));
        if (typeof(TEntity) == typeof(SalesGroupMember))
            return Cast<TEntity, SalesGroupMember>(dbContext.SalesGroupMemberRecords.Where(x => x.UserAccountId == access.UserId));
        if (typeof(TEntity) == typeof(Retailer))
            return Cast<TEntity, Retailer>(dbContext.RetailerRecords.Where(x => retailerIds.Contains(x.Id) || x.DeclaredByUserId == access.UserId));
        if (typeof(TEntity) == typeof(RetailerSalesAssignment))
            return Cast<TEntity, RetailerSalesAssignment>(dbContext.RetailerSalesAssignmentRecords.Where(x =>
                retailerIds.Contains(x.RetailerId) || salesGroupIds.Contains(x.SalesGroupId)));
        if (typeof(TEntity) == typeof(RetailerProductMlor))
            return Cast<TEntity, RetailerProductMlor>(dbContext.RetailerProductMlorRecords.Where(x => retailerIds.Contains(x.RetailerId)));
        if (typeof(TEntity) == typeof(RetailerDeclarationContact))
            return Cast<TEntity, RetailerDeclarationContact>(dbContext.RetailerDeclarationContactRecords.Where(x => retailerIds.Contains(x.RetailerId)));
        if (typeof(TEntity) == typeof(TransitTimeRule))
            return Cast<TEntity, TransitTimeRule>(dbContext.TransitTimeRuleRecords.Where(x => warehouseIds.Contains(x.WarehouseId)));
        if (typeof(TEntity) == typeof(SalesOrder))
            return Cast<TEntity, SalesOrder>(ScopedOrders(access));
        if (typeof(TEntity) == typeof(SalesOrderLine))
            return Cast<TEntity, SalesOrderLine>(dbContext.SalesOrderLineRecords.Where(x => orderIds.Contains(x.SalesOrderId)));
        if (typeof(TEntity) == typeof(OrderPromotion))
            return Cast<TEntity, OrderPromotion>(dbContext.OrderPromotionRecords.Where(x =>
                dbContext.SalesOrderLineRecords.Any(line => line.Id == x.SalesOrderLineId && orderIds.Contains(line.SalesOrderId))));
        if (typeof(TEntity) == typeof(OrderStatusHistory))
            return Cast<TEntity, OrderStatusHistory>(dbContext.OrderStatusHistoryRecords.Where(x => orderIds.Contains(x.SalesOrderId)));
        if (typeof(TEntity) == typeof(SalesOrderAttachment))
            return Cast<TEntity, SalesOrderAttachment>(dbContext.SalesOrderAttachmentRecords.Where(x => orderIds.Contains(x.SalesOrderId)));
        if (typeof(TEntity) == typeof(SalesOrderQuote))
            return Cast<TEntity, SalesOrderQuote>(dbContext.SalesOrderQuoteRecords.Where(x => orderIds.Contains(x.SalesOrderId)));
        if (typeof(TEntity) == typeof(PurchaseOrder))
            return Cast<TEntity, PurchaseOrder>(dbContext.PurchaseOrderRecords.Where(x => orderIds.Contains(x.SalesOrderId)));
        if (typeof(TEntity) == typeof(Payment))
            return Cast<TEntity, Payment>(dbContext.PaymentRecords.Where(x => orderIds.Contains(x.SalesOrderId)));
        if (typeof(TEntity) == typeof(PaymentReceipt))
            return Cast<TEntity, PaymentReceipt>(dbContext.PaymentReceiptRecords.Where(x => retailerIds.Contains(x.RetailerId)));
        if (typeof(TEntity) == typeof(PaymentReceiptAllocation))
            return Cast<TEntity, PaymentReceiptAllocation>(dbContext.PaymentReceiptAllocationRecords.Where(x =>
                dbContext.PaymentReceiptRecords.Any(receipt => receipt.Id == x.PaymentReceiptId && retailerIds.Contains(receipt.RetailerId))));
        if (typeof(TEntity) == typeof(ManualRefund))
            return Cast<TEntity, ManualRefund>(dbContext.ManualRefundRecords.Where(x => orderIds.Contains(x.SalesOrderId)));
        if (typeof(TEntity) == typeof(Delivery))
            return Cast<TEntity, Delivery>(ScopedDeliveries(access, orderIds));
        if (typeof(TEntity) == typeof(Shipment))
            return Cast<TEntity, Shipment>(dbContext.ShipmentRecords.Where(x =>
                access.Role == SystemRoles.WarehouseKeeper
                    ? warehouseIds.Contains(x.WarehouseId)
                    : orderIds.Contains(x.SalesOrderId)));
        if (typeof(TEntity) == typeof(PickingList))
            return Cast<TEntity, PickingList>(dbContext.PickingListRecords.Where(x =>
                dbContext.PurchaseOrderRecords.Any(po => po.Id == x.PurchaseOrderId && orderIds.Contains(po.SalesOrderId)) ||
                warehouseIds.Contains(x.WarehouseId)));
        if (typeof(TEntity) == typeof(PickingItem))
        {
            var pickingIds = dbContext.PickingListRecords
                .Where(x => dbContext.PurchaseOrderRecords.Any(po =>
                        po.Id == x.PurchaseOrderId && orderIds.Contains(po.SalesOrderId)) ||
                    warehouseIds.Contains(x.WarehouseId))
                .Select(x => x.Id);
            return Cast<TEntity, PickingItem>(dbContext.PickingItemRecords.Where(x => pickingIds.Contains(x.PickingListId)));
        }
        if (typeof(TEntity) == typeof(ReceiptConfirmation))
            return Cast<TEntity, ReceiptConfirmation>(dbContext.ReceiptConfirmationRecords.Where(x => orderIds.Contains(x.SalesOrderId)));
        if (typeof(TEntity) == typeof(ReceiptConfirmationItem))
        {
            var confirmationIds = dbContext.ReceiptConfirmationRecords
                .Where(x => orderIds.Contains(x.SalesOrderId)).Select(x => x.Id);
            return Cast<TEntity, ReceiptConfirmationItem>(dbContext.ReceiptConfirmationItemRecords.Where(x =>
                confirmationIds.Contains(x.ReceiptConfirmationId)));
        }
        if (typeof(TEntity) == typeof(ReceiptConfirmationQrLabel))
        {
            var confirmationIds = dbContext.ReceiptConfirmationRecords
                .Where(x => orderIds.Contains(x.SalesOrderId)).Select(x => x.Id);
            return Cast<TEntity, ReceiptConfirmationQrLabel>(dbContext.ReceiptConfirmationQrLabelRecords.Where(x =>
                confirmationIds.Contains(x.ReceiptConfirmationId)));
        }
        if (typeof(TEntity) == typeof(ReceiptDiscrepancy))
            return Cast<TEntity, ReceiptDiscrepancy>(dbContext.ReceiptDiscrepancyRecords.Where(x => orderIds.Contains(x.SalesOrderId)));
        if (typeof(TEntity) == typeof(ReceiptDiscrepancyItem))
        {
            var discrepancyIds = dbContext.ReceiptDiscrepancyRecords
                .Where(x => orderIds.Contains(x.SalesOrderId)).Select(x => x.Id);
            return Cast<TEntity, ReceiptDiscrepancyItem>(dbContext.ReceiptDiscrepancyItemRecords.Where(x =>
                discrepancyIds.Contains(x.ReceiptDiscrepancyId)));
        }
        if (typeof(TEntity) == typeof(ReplacementCase))
            return Cast<TEntity, ReplacementCase>(dbContext.ReplacementCaseRecords.Where(x =>
                dbContext.ReceiptDiscrepancyRecords.Any(claim => claim.Id == x.ReceiptDiscrepancyId && orderIds.Contains(claim.SalesOrderId))));
        if (typeof(TEntity) == typeof(ReturnHandlingDocument))
            return Cast<TEntity, ReturnHandlingDocument>(dbContext.ReturnHandlingDocumentRecords.Where(x =>
                dbContext.ReceiptDiscrepancyRecords.Any(claim => claim.Id == x.ReceiptDiscrepancyId && orderIds.Contains(claim.SalesOrderId))));
        if (typeof(TEntity) == typeof(ComplaintEvidence))
            return Cast<TEntity, ComplaintEvidence>(dbContext.ComplaintEvidenceRecords.Where(x =>
                dbContext.ReceiptDiscrepancyRecords.Any(claim => claim.Id == x.ReceiptDiscrepancyId && orderIds.Contains(claim.SalesOrderId))));
        if (typeof(TEntity) == typeof(Allocation))
            return Cast<TEntity, Allocation>(dbContext.AllocationRecords.Where(x =>
                warehouseIds.Contains(x.WarehouseId) || dbContext.SalesOrderLineRecords.Any(line =>
                    line.Id == x.SalesOrderLineId && orderIds.Contains(line.SalesOrderId))));
        if (typeof(TEntity) == typeof(Reservation))
            return Cast<TEntity, Reservation>(dbContext.ReservationRecords.Where(x =>
                (x.AllocationId != null && dbContext.AllocationRecords.Any(a => a.Id == x.AllocationId &&
                    (warehouseIds.Contains(a.WarehouseId) || dbContext.SalesOrderLineRecords.Any(line =>
                        line.Id == a.SalesOrderLineId && orderIds.Contains(line.SalesOrderId))))) ||
                (x.AllocationRunAllocationId != null && dbContext.AllocationRunAllocationRecords.Any(a =>
                    a.Id == x.AllocationRunAllocationId && (warehouseIds.Contains(a.WarehouseId) ||
                        dbContext.AllocationRunLineRecords.Any(line => line.Id == a.AllocationRunLineId &&
                            dbContext.SalesOrderLineRecords.Any(orderLine => orderLine.Id == line.SalesOrderLineId &&
                                orderIds.Contains(orderLine.SalesOrderId))))))));
        if (typeof(TEntity) == typeof(StockHold))
            return Cast<TEntity, StockHold>(dbContext.StockHoldRecords.Where(x =>
                dbContext.AllocationRecords.Any(allocation => allocation.Id == x.AllocationId &&
                    (warehouseIds.Contains(allocation.WarehouseId) || dbContext.SalesOrderLineRecords.Any(line =>
                        line.Id == allocation.SalesOrderLineId && orderIds.Contains(line.SalesOrderId))))));

        var runIds = ScopedRunIds(access, orderIds);
        var runOrderIds = dbContext.AllocationRunOrderRecords.Where(x => runIds.Contains(x.AllocationRunId) &&
            (access.Role == SystemRoles.WarehouseKeeper || orderIds.Contains(x.SalesOrderId))).Select(x => x.Id);
        var runLineIds = dbContext.AllocationRunLineRecords.Where(x => runOrderIds.Contains(x.AllocationRunOrderId)).Select(x => x.Id);
        if (typeof(TEntity) == typeof(AllocationRun))
            return Cast<TEntity, AllocationRun>(dbContext.AllocationRunRecords.Where(x => runIds.Contains(x.Id)));
        if (typeof(TEntity) == typeof(AllocationRunOrder))
            return Cast<TEntity, AllocationRunOrder>(dbContext.AllocationRunOrderRecords.Where(x => runOrderIds.Contains(x.Id)));
        if (typeof(TEntity) == typeof(AllocationRunLine))
            return Cast<TEntity, AllocationRunLine>(dbContext.AllocationRunLineRecords.Where(x => runOrderIds.Contains(x.AllocationRunOrderId)));
        if (typeof(TEntity) == typeof(AllocationRunStock))
            return Cast<TEntity, AllocationRunStock>(dbContext.AllocationRunStockRecords.Where(x =>
                access.Role == SystemRoles.WarehouseKeeper && runIds.Contains(x.AllocationRunId) && warehouseIds.Contains(x.WarehouseId)));
        if (typeof(TEntity) == typeof(AllocationRunAllocation))
            return Cast<TEntity, AllocationRunAllocation>(dbContext.AllocationRunAllocationRecords.Where(x =>
                runLineIds.Contains(x.AllocationRunLineId) &&
                (access.Role != SystemRoles.WarehouseKeeper || warehouseIds.Contains(x.WarehouseId))));
        if (typeof(TEntity) == typeof(AllocationPlanPrice))
            return Cast<TEntity, AllocationPlanPrice>(dbContext.AllocationPlanPriceRecords.Where(x =>
                runIds.Contains(x.AllocationRunId) && runLineIds.Contains(x.AllocationRunLineId) &&
                (access.Role != SystemRoles.WarehouseKeeper || warehouseIds.Contains(x.WarehouseId))));
        if (typeof(TEntity) == typeof(AllocationPlanVersion))
            return Cast<TEntity, AllocationPlanVersion>(dbContext.AllocationPlanVersionRecords.Where(x => runIds.Contains(x.AllocationRunId)));
        if (typeof(TEntity) == typeof(AllocationLineTrace))
            return Cast<TEntity, AllocationLineTrace>(dbContext.AllocationLineTraceRecords.Where(x => runIds.Contains(x.AllocationRunId)));
        if (typeof(TEntity) == typeof(AllocationTraceCandidate))
            return Cast<TEntity, AllocationTraceCandidate>(dbContext.AllocationTraceCandidateRecords.Where(x =>
                dbContext.AllocationLineTraceRecords.Any(trace => trace.Id == x.AllocationLineTraceId && runIds.Contains(trace.AllocationRunId))));
        if (typeof(TEntity) == typeof(AllocationTraceScore))
            return Cast<TEntity, AllocationTraceScore>(dbContext.AllocationTraceScoreRecords.Where(x =>
                dbContext.AllocationLineTraceRecords.Any(trace => trace.Id == x.AllocationLineTraceId && runIds.Contains(trace.AllocationRunId))));
        if (typeof(TEntity) == typeof(RunPickingList))
            return Cast<TEntity, RunPickingList>(dbContext.RunPickingListRecords.Where(x =>
                runIds.Contains(x.AllocationRunId) && (access.Role != SystemRoles.WarehouseKeeper || warehouseIds.Contains(x.WarehouseId))));
        if (typeof(TEntity) == typeof(RunPickingGather))
            return Cast<TEntity, RunPickingGather>(dbContext.RunPickingGatherRecords.Where(x =>
                dbContext.RunPickingListRecords.Any(list => list.Id == x.RunPickingListId && runIds.Contains(list.AllocationRunId) &&
                    (access.Role != SystemRoles.WarehouseKeeper || warehouseIds.Contains(list.WarehouseId)))));
        if (typeof(TEntity) == typeof(RunPickingSplit))
            return Cast<TEntity, RunPickingSplit>(dbContext.RunPickingSplitRecords.Where(x =>
                orderIds.Contains(x.SalesOrderId) && (access.Role != SystemRoles.WarehouseKeeper ||
                    dbContext.AllocationRunAllocationRecords.Any(allocation => allocation.Id == x.AllocationRunAllocationId && warehouseIds.Contains(allocation.WarehouseId)))));

        if (typeof(TEntity) == typeof(GoodsReceipt))
            return Cast<TEntity, GoodsReceipt>(dbContext.GoodsReceiptRecords.Where(x => warehouseIds.Contains(x.WarehouseId)));
        if (typeof(TEntity) == typeof(GoodsReceiptLine))
            return Cast<TEntity, GoodsReceiptLine>(dbContext.GoodsReceiptLineRecords.Where(x =>
                dbContext.GoodsReceiptRecords.Any(receipt => receipt.Id == x.GoodsReceiptId && warehouseIds.Contains(receipt.WarehouseId))));
        if (typeof(TEntity) == typeof(Batch))
            return Cast<TEntity, Batch>(dbContext.BatchRecords.Where(x => warehouseIds.Contains(x.WarehouseId)));
        if (typeof(TEntity) == typeof(BatchQrLabel))
            return Cast<TEntity, BatchQrLabel>(dbContext.BatchQrLabelRecords.Where(x => warehouseIds.Contains(x.CurrentWarehouseId)));
        if (typeof(TEntity) == typeof(QrLabelEvent))
            return Cast<TEntity, QrLabelEvent>(dbContext.QrLabelEventRecords.Where(x =>
                dbContext.BatchQrLabelRecords.Any(label => label.Id == x.BatchQrLabelId && warehouseIds.Contains(label.CurrentWarehouseId))));
        if (typeof(TEntity) == typeof(InventoryBalance))
            return Cast<TEntity, InventoryBalance>(dbContext.InventoryBalanceRecords.Where(x =>
                dbContext.BatchRecords.Any(batch => batch.Id == x.BatchId && warehouseIds.Contains(batch.WarehouseId))));
        if (typeof(TEntity) == typeof(InventoryTransaction))
            return Cast<TEntity, InventoryTransaction>(dbContext.InventoryTransactionRecords.Where(x => warehouseIds.Contains(x.WarehouseId)));
        if (typeof(TEntity) == typeof(Driver))
            return Cast<TEntity, Driver>(dbContext.DriverRecords.Where(x => warehouseIds.Contains(x.WarehouseId)));
        if (typeof(TEntity) == typeof(Vehicle))
            return Cast<TEntity, Vehicle>(dbContext.VehicleRecords.Where(x => warehouseIds.Contains(x.WarehouseId)));
        if (typeof(TEntity) == typeof(WarehouseTransfer))
            return Cast<TEntity, WarehouseTransfer>(dbContext.WarehouseTransferRecords.Where(x =>
                warehouseIds.Contains(x.SourceWarehouseId) || warehouseIds.Contains(x.DestinationWarehouseId)));
        if (typeof(TEntity) == typeof(WarehouseTransferItem))
            return Cast<TEntity, WarehouseTransferItem>(dbContext.WarehouseTransferItemRecords.Where(x =>
                dbContext.WarehouseTransferRecords.Any(transfer => transfer.Id == x.WarehouseTransferId &&
                    (warehouseIds.Contains(transfer.SourceWarehouseId) || warehouseIds.Contains(transfer.DestinationWarehouseId)))));
        if (typeof(TEntity) == typeof(WarehouseTransferEvent))
            return Cast<TEntity, WarehouseTransferEvent>(dbContext.WarehouseTransferEventRecords.Where(x =>
                dbContext.WarehouseTransferRecords.Any(transfer => transfer.Id == x.WarehouseTransferId &&
                    (warehouseIds.Contains(transfer.SourceWarehouseId) || warehouseIds.Contains(transfer.DestinationWarehouseId)))));
        if (typeof(TEntity) == typeof(WarehouseTransferLabel))
            return Cast<TEntity, WarehouseTransferLabel>(dbContext.WarehouseTransferLabelRecords.Where(x =>
                dbContext.WarehouseTransferItemRecords.Any(item => item.Id == x.WarehouseTransferItemId &&
                    dbContext.WarehouseTransferRecords.Any(transfer => transfer.Id == item.WarehouseTransferId &&
                        (warehouseIds.Contains(transfer.SourceWarehouseId) || warehouseIds.Contains(transfer.DestinationWarehouseId))))));
        if (typeof(TEntity) == typeof(WarehouseDocument))
            return Cast<TEntity, WarehouseDocument>(dbContext.WarehouseDocuments.Where(x =>
                warehouseIds.Contains(x.WarehouseId) || (x.DestinationWarehouseId != null && warehouseIds.Contains(x.DestinationWarehouseId.Value))));
        if (typeof(TEntity) == typeof(WarehouseDocumentLine))
            return Cast<TEntity, WarehouseDocumentLine>(dbContext.WarehouseDocumentLines.Where(x =>
                dbContext.WarehouseDocuments.Any(document => document.Id == x.DocumentId &&
                    (warehouseIds.Contains(document.WarehouseId) || (document.DestinationWarehouseId != null && warehouseIds.Contains(document.DestinationWarehouseId.Value))))));
        if (typeof(TEntity) == typeof(WarehouseDocumentCarton))
            return Cast<TEntity, WarehouseDocumentCarton>(dbContext.WarehouseDocumentCartons.Where(x =>
                dbContext.WarehouseDocuments.Any(document => document.Id == x.DocumentId &&
                    (warehouseIds.Contains(document.WarehouseId) || (document.DestinationWarehouseId != null && warehouseIds.Contains(document.DestinationWarehouseId.Value))))));
        if (typeof(TEntity) == typeof(WriteOff))
            return Cast<TEntity, WriteOff>(dbContext.WriteOffRecords.Where(x => warehouseIds.Contains(x.WarehouseId)));
        if (typeof(TEntity) == typeof(WriteOffItem))
            return Cast<TEntity, WriteOffItem>(dbContext.WriteOffItemRecords.Where(x =>
                dbContext.WriteOffRecords.Any(writeOff => writeOff.Id == x.WriteOffId && warehouseIds.Contains(writeOff.WarehouseId))));
        if (typeof(TEntity) == typeof(WriteOffAttachment))
            return Cast<TEntity, WriteOffAttachment>(dbContext.WriteOffAttachmentRecords.Where(x =>
                dbContext.WriteOffRecords.Any(writeOff => writeOff.Id == x.WriteOffId && warehouseIds.Contains(writeOff.WarehouseId))));
        if (typeof(TEntity) == typeof(Notification))
            return Cast<TEntity, Notification>(dbContext.NotificationRecords.Where(x => x.UserAccountId == access.UserId));
        if (typeof(TEntity) == typeof(RetailerLoyalty))
            return Cast<TEntity, RetailerLoyalty>(dbContext.RetailerLoyaltyRecords.Where(x => retailerIds.Contains(x.RetailerId)));
        if (typeof(TEntity) == typeof(LoyaltyTransaction))
            return Cast<TEntity, LoyaltyTransaction>(dbContext.LoyaltyTransactionRecords.Where(x =>
                dbContext.RetailerLoyaltyRecords.Any(loyalty => loyalty.Id == x.RetailerLoyaltyId && retailerIds.Contains(loyalty.RetailerId))));
        if (typeof(TEntity) == typeof(RewardRedemption))
            return Cast<TEntity, RewardRedemption>(dbContext.RewardRedemptionRecords.Where(x => retailerIds.Contains(x.RetailerId)));
        if (typeof(TEntity) == typeof(LoyaltyProgramRetailer))
            return Cast<TEntity, LoyaltyProgramRetailer>(dbContext.LoyaltyProgramRetailerRecords.Where(x => retailerIds.Contains(x.RetailerId)));
        if (typeof(TEntity) == typeof(RetailerDebtPayOsCheckout))
            return Cast<TEntity, RetailerDebtPayOsCheckout>(dbContext.RetailerDebtPayOsCheckoutRecords.Where(x => retailerIds.Contains(x.RetailerId)));
        if (typeof(TEntity) == typeof(RetailerDebtPayOsAllocation))
            return Cast<TEntity, RetailerDebtPayOsAllocation>(dbContext.RetailerDebtPayOsAllocationRecords.Where(x =>
                dbContext.RetailerDebtPayOsCheckoutRecords.Any(checkout => checkout.Id == x.CheckoutId && retailerIds.Contains(checkout.RetailerId))));
        if (typeof(TEntity) == typeof(BatchPromotion))
            return Cast<TEntity, BatchPromotion>(dbContext.BatchPromotionRecords.Where(x =>
                dbContext.BatchRecords.Any(batch => batch.Id == x.BatchId && warehouseIds.Contains(batch.WarehouseId))));
        if (typeof(TEntity) == typeof(MapLocation))
            return Cast<TEntity, MapLocation>(dbContext.MapLocationRecords.Where(x =>
                (x.WarehouseId != null && (access.Role != SystemRoles.WarehouseKeeper || warehouseIds.Contains(x.WarehouseId.Value))) ||
                (x.RetailerId != null && retailerIds.Contains(x.RetailerId.Value))));
        if (typeof(TEntity) == typeof(MapDistanceCache))
            return Cast<TEntity, MapDistanceCache>(dbContext.MapDistanceCacheRecords.Where(x =>
                (access.Role == SystemRoles.WarehouseKeeper && warehouseIds.Contains(x.WarehouseId)) || retailerIds.Contains(x.RetailerId)));

        return null;
    }

    private IQueryable<SalesOrder> ScopedOrders(EntityQueryAccess access) => access.Role switch
    {
        SystemRoles.Sales => dbContext.SalesOrderRecords.Where(x =>
            x.CreatedByUserId == access.UserId || access.Scope.SalesGroupIds.Contains(x.SalesGroupId)),
        SystemRoles.SalesManager => dbContext.SalesOrderRecords.Where(x => access.Scope.SalesGroupIds.Contains(x.SalesGroupId)),
        SystemRoles.Retailer => dbContext.SalesOrderRecords.Where(x => access.Scope.RetailerIds.Contains(x.RetailerId)),
        SystemRoles.WarehouseKeeper => dbContext.SalesOrderRecords.Where(order =>
            dbContext.ShipmentRecords.Any(part => part.SalesOrderId == order.Id && access.Scope.WarehouseIds.Contains(part.WarehouseId)) ||
            dbContext.PickingListRecords.Any(list =>
                dbContext.PurchaseOrderRecords.Any(po => po.Id == list.PurchaseOrderId && po.SalesOrderId == order.Id) &&
                access.Scope.WarehouseIds.Contains(list.WarehouseId)) ||
            dbContext.AllocationRecords.Any(allocation => access.Scope.WarehouseIds.Contains(allocation.WarehouseId) &&
                dbContext.SalesOrderLineRecords.Any(line => line.Id == allocation.SalesOrderLineId && line.SalesOrderId == order.Id)) ||
            // The run allocates into its own table; without this a keeper's GraphQL order scope is
            // limited to whatever the older allocation path happened to write.
            dbContext.AllocationRunAllocationRecords.Any(allocation => access.Scope.WarehouseIds.Contains(allocation.WarehouseId) &&
                dbContext.AllocationRunLineRecords.Any(runLine => runLine.Id == allocation.AllocationRunLineId &&
                    dbContext.SalesOrderLineRecords.Any(line => line.Id == runLine.SalesOrderLineId && line.SalesOrderId == order.Id)))),
        _ => dbContext.SalesOrderRecords.Where(_ => false)
    };

    private IQueryable<Guid> ScopedRunIds(EntityQueryAccess access, IQueryable<Guid> orderIds) =>
        access.Role == SystemRoles.WarehouseKeeper
            ? dbContext.AllocationRunStockRecords.Where(x => access.Scope.WarehouseIds.Contains(x.WarehouseId)).Select(x => x.AllocationRunId).Distinct()
            : dbContext.AllocationRunRecords.Where(x => orderIds.Contains(x.SalesOrderId)).Select(x => x.Id).Distinct();

    private IQueryable<Delivery> ScopedDeliveries(EntityQueryAccess access, IQueryable<Guid> orderIds) =>
        access.Role == SystemRoles.WarehouseKeeper
            ? dbContext.DeliveryRecords.Where(x =>
                dbContext.DriverRecords.Any(driver => driver.Id == x.DriverId && access.Scope.WarehouseIds.Contains(driver.WarehouseId)))
            : dbContext.DeliveryRecords.Where(x => orderIds.Contains(x.SalesOrderId));

    private static bool IsSharedMasterData<TEntity>() => typeof(TEntity) == typeof(Warehouse) ||
        typeof(TEntity) == typeof(Region) || typeof(TEntity) == typeof(SalesGroup) ||
        typeof(TEntity) == typeof(ProductCategory) || typeof(TEntity) == typeof(Product) ||
        typeof(TEntity) == typeof(ProductPrice) || typeof(TEntity) == typeof(Promotion) ||
        typeof(TEntity) == typeof(PromotionProduct) || typeof(TEntity) == typeof(PromotionCategory) ||
        typeof(TEntity) == typeof(PromotionRetailer) || typeof(TEntity) == typeof(TransitTable) ||
        typeof(TEntity) == typeof(TransitTableBand) || typeof(TEntity) == typeof(ShelfLifeDiscountTable) ||
        typeof(TEntity) == typeof(ShelfLifeDiscountTier) || typeof(TEntity) == typeof(LoyaltyProgram) ||
        typeof(TEntity) == typeof(LoyaltyReward);

    private static bool HasGlobalManagementGrant<TEntity>(IReadOnlyCollection<string>? grants)
    {
        if (grants is null) return false;
        if (typeof(TEntity) == typeof(UserAccount) || typeof(TEntity) == typeof(UserWarehouseAssignment) ||
            typeof(TEntity) == typeof(UserRegionAssignment) || typeof(TEntity) == typeof(SalesGroupMember) ||
            typeof(TEntity) == typeof(AuditLog))
            return grants.Contains("api:Users.List") || grants.Contains("api:Users.Get");
        if (typeof(TEntity) == typeof(RoleDefinition) || typeof(TEntity) == typeof(RolePermission))
            return grants.Contains("api:Roles.List") || grants.Contains("api:Roles.Get");
        return typeof(TEntity) == typeof(SystemConfiguration) && grants.Contains("config") &&
            grants.Contains("api:BusinessConfigurations.List");
    }

    private static bool IsAdminOnlyEntity<TEntity>() => typeof(TEntity) == typeof(UserAccount) ||
        typeof(TEntity) == typeof(UserWarehouseAssignment) || typeof(TEntity) == typeof(UserRegionAssignment) ||
        typeof(TEntity) == typeof(SalesGroupMember) || typeof(TEntity) == typeof(AuditLog) ||
        typeof(TEntity) == typeof(SystemConfiguration) || typeof(TEntity) == typeof(RoleDefinition) ||
        typeof(TEntity) == typeof(RolePermission);

    private static IQueryable<TEntity> Cast<TEntity, TActual>(IQueryable<TActual> query)
        where TEntity : class where TActual : class => (IQueryable<TEntity>)(object)query;
}
