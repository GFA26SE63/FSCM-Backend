using FSCM.Application.Features.Payments;
using FSCM.Application.Features.Picking;
using FSCM.Application.Features.Quotes;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Orders;

public sealed record PurchaseOrderGateView(
    SalesOrder Order,
    SalesOrderQuote Quote,
    Payment? Payment,
    PurchaseOrder? PurchaseOrder,
    IReadOnlyCollection<Allocation> Allocations,
    IReadOnlyDictionary<Guid, string> WarehouseCodes);

public interface IPurchaseOrderGateStore
{
    Task<PurchaseOrderGateView?> LoadForUpdateAsync(Guid orderId, CancellationToken cancellationToken);
    void AddPurchaseOrder(PurchaseOrder purchaseOrder);
    void AddPickingList(PickingList pickingList);
    void AddPickingItem(PickingItem pickingItem);
    void AddHistory(OrderStatusHistory history);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
    void ClearTracking();
}

/// <summary>
/// The only gate that converts an accepted quotation into a PO. Credit orders pass immediately;
/// prepaid orders pass only after the payment provider confirms the full amount. Picking records
/// are created from the exact reserved allocations, so warehouse work cannot start from a draft SO.
/// </summary>
public sealed class PurchaseOrderGateService(IPurchaseOrderGateStore store, TimeProvider timeProvider)
{
    public async Task TryCreateAsync(Guid orderId, CancellationToken cancellationToken)
    {
        await store.ExecuteInTransactionAsync(async token =>
        {
            var view = await store.LoadForUpdateAsync(orderId, token)
                ?? throw new InvalidOperationException("Sales order was not found for PO conversion.");
            if (view.PurchaseOrder is not null) return;
            if (view.Quote.Status != SalesOrderQuoteStatuses.Accepted)
                throw new InvalidOperationException("An accepted quotation is required before PO conversion.");
            if (view.Order.PaymentMethod is not (OrderPaymentMethods.Prepaid or OrderPaymentMethods.Credit))
                throw new InvalidOperationException("Retailer payment method has not been selected.");

            var eligible = view.Order.PaymentMethod == OrderPaymentMethods.Credit ||
                           view.Payment?.Status == PaymentStatuses.Paid;
            if (!eligible) return;
            if (view.Allocations.Count == 0)
                throw new InvalidOperationException("Reserved allocations are required before PO conversion.");

            var now = timeProvider.GetUtcNow();
            var actor = view.Quote.AcceptedByUserId
                ?? throw new InvalidOperationException("Accepted quotation has no retailer actor.");
            var po = new PurchaseOrder
            {
                Id = Guid.NewGuid(),
                PurchaseOrderCode = $"PO-{view.Order.OrderCode}-{view.Quote.RevisionNumber:00}",
                SalesOrderId = view.Order.Id,
                SalesOrderQuoteId = view.Quote.Id,
                Status = PurchaseOrderStatuses.Issued,
                PaymentMethod = view.Order.PaymentMethod,
                IssuedAt = now,
                IssuedByUserId = actor,
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now
            };
            store.AddPurchaseOrder(po);
            view.Quote.PurchaseOrderId = po.Id;
            view.Quote.Version++;
            view.Quote.UpdatedAt = now;

            foreach (var warehouseGroup in view.Allocations.GroupBy(item => item.WarehouseId))
            {
                var warehouseCode = view.WarehouseCodes[warehouseGroup.Key];
                var rawCode = $"PK-{view.Order.OrderCode}-{warehouseCode}";
                var list = new PickingList
                {
                    Id = Guid.NewGuid(),
                    PickingCode = rawCode[..Math.Min(rawCode.Length, 40)],
                    PurchaseOrderId = po.Id,
                    WarehouseId = warehouseGroup.Key,
                    Status = PickingListStatuses.Pending,
                    Version = 1,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                store.AddPickingList(list);
                foreach (var allocation in warehouseGroup)
                {
                    store.AddPickingItem(new PickingItem
                    {
                        Id = Guid.NewGuid(),
                        PickingListId = list.Id,
                        AllocationId = allocation.Id,
                        SourceBatchId = allocation.BatchId,
                        RequestedQuantity = allocation.AllocatedQuantity,
                        PickedQuantity = 0,
                        Status = PickingItemStatuses.Pending
                    });
                }
            }

            var fromStatus = view.Order.Status;
            view.Order.Status = OrderStatuses.ConvertedToPo;
            view.Order.Version++;
            view.Order.UpdatedAt = now;
            store.AddHistory(new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                SalesOrderId = view.Order.Id,
                FromStatus = fromStatus,
                ToStatus = OrderStatuses.ConvertedToPo,
                ChangedByUserId = actor,
                Reason = view.Order.PaymentMethod == OrderPaymentMethods.Credit
                    ? "Retailer accepted quotation with approved credit"
                    : "Prepaid quotation was paid in full",
                ChangedAt = now
            });
            await store.SaveChangesAsync(token);
        }, cancellationToken);
        store.ClearTracking();
    }
}
