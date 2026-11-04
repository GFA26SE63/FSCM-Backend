using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Payments;
using FSCM.Application.Features.Refunds;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Payments;

public sealed class RetailerReceivableStore(AppDbContext dbContext) : IRetailerReceivableStore
{
    public async Task<IReadOnlyCollection<RetailerReceivableView>> ListAsync(
        Guid retailerUserId, CancellationToken cancellationToken)
    {
        var orderIds = await (from payment in dbContext.PaymentRecords.AsNoTracking()
                              join order in dbContext.SalesOrderRecords.AsNoTracking()
                                  on payment.SalesOrderId equals order.Id
                              join retailer in dbContext.RetailerRecords.AsNoTracking()
                                  on order.RetailerId equals retailer.Id
                              where retailer.UserAccountId == retailerUserId
                                    && payment.Method == OrderPaymentMethods.Credit
                              orderby payment.DueDate, order.OrderCode
                              select order.Id).ToArrayAsync(cancellationToken);
        var results = new List<RetailerReceivableView>(orderIds.Length);
        foreach (var orderId in orderIds)
        {
            var view = await LoadAsync(retailerUserId, orderId, cancellationToken);
            if (view is not null) results.Add(view);
        }
        return results;
    }

    public Task<RetailerReceivableView?> FindByOrderAsync(
        Guid retailerUserId, Guid orderId, CancellationToken cancellationToken) =>
        LoadAsync(retailerUserId, orderId, cancellationToken);

    private async Task<RetailerReceivableView?> LoadAsync(
        Guid retailerUserId, Guid orderId, CancellationToken cancellationToken)
    {
        var core = await (from payment in dbContext.PaymentRecords.AsNoTracking()
                          join order in dbContext.SalesOrderRecords.AsNoTracking()
                              on payment.SalesOrderId equals order.Id
                          join retailer in dbContext.RetailerRecords.AsNoTracking()
                              on order.RetailerId equals retailer.Id
                          where order.Id == orderId && retailer.UserAccountId == retailerUserId
                          select new { payment, order, retailer }).SingleOrDefaultAsync(cancellationToken);
        if (core is null) return null;

        var refunds = await dbContext.ManualRefundRecords.AsNoTracking()
            .Where(item => item.PaymentId == core.payment.Id)
            .OrderByDescending(item => item.CreatedAt)
            .Select(item => new ReceivableRefundHistory(item.Id, item.Amount, item.Status,
                item.TransferredAt, item.TransactionReference, item.VerifiedAt, item.Reason))
            .ToArrayAsync(cancellationToken);
        var pendingRefund = refunds.Where(item => RefundStatuses.Open.Contains(item.Status))
            .Sum(item => item.Amount);
        var completedRefund = refunds.Where(item => item.Status == RefundStatuses.Refunded)
            .Sum(item => item.Amount);

        var receiptRows = await (from allocation in dbContext.PaymentReceiptAllocationRecords.AsNoTracking()
            join receipt in dbContext.PaymentReceiptRecords.AsNoTracking() on allocation.PaymentReceiptId equals receipt.Id
            where allocation.PaymentId == core.payment.Id
            orderby receipt.CreatedAt descending
            select new { Receipt = receipt, allocation.Amount }).ToArrayAsync(cancellationToken);
        var collections = receiptRows.Select(item => new ReceivableCollectionHistory(
            item.Receipt.Id, "PaymentReceipt", item.Amount, item.Receipt.Status.ToString(),
            item.Receipt.ConfirmedAt, item.Receipt.Reference, item.Receipt.Note)).ToList();
        if (core.payment.ConfirmedAmount > 0 && collections.All(item => item.Status != PaymentReceiptStatus.Confirmed.ToString()))
        {
            collections.Add(new(core.payment.Id, core.payment.CollectionMethod ?? core.payment.Method,
                core.payment.ConfirmedAmount, PaymentStatuses.Paid, core.payment.ConfirmedAt,
                core.payment.PayOsReference, core.payment.Note));
        }

        var discrepancyRows = await dbContext.ReceiptDiscrepancyRecords.AsNoTracking()
            .Where(item => item.SalesOrderId == orderId)
            .OrderByDescending(item => item.ReportedAt)
            .ToArrayAsync(cancellationToken);
        var disputes = new List<ReceivableDisputeHistory>(discrepancyRows.Length);
        foreach (var discrepancy in discrepancyRows)
        {
            // Single source of truth: the adjustment the claim already stored at the final price
            // of exactly the affected shipment part (no recomputation from current tables).
            var amount = await dbContext.ReceiptDiscrepancyItemRecords.AsNoTracking()
                .Where(item => item.ReceiptDiscrepancyId == discrepancy.Id)
                .SumAsync(item => (decimal?)item.AdjustmentAmount, cancellationToken) ?? 0m;
            disputes.Add(new(discrepancy.Id, discrepancy.Status, decimal.Round(amount, 2),
                discrepancy.ReportedAt, discrepancy.ResolvedAt, discrepancy.Note));
        }

        var paymentView = new PaymentView(core.payment, core.order, core.retailer,
            pendingRefund, completedRefund, disputes.Any(item => item.ResolvedAt == null));
        return new(paymentView, collections, disputes, refunds);
    }
}



