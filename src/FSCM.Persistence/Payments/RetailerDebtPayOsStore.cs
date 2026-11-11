using FSCM.Application.Features.Payments;
using FSCM.Application.Features.Orders;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FSCM.Persistence.Payments;

public sealed class RetailerDebtPayOsStore(AppDbContext db) : IRetailerDebtPayOsStore
{
    public Task<Retailer?> FindRetailerByUserAsync(Guid userId, CancellationToken cancellationToken) =>
        db.RetailerRecords.AsNoTracking().SingleOrDefaultAsync(x => x.UserAccountId == userId, cancellationToken);

    public async Task<IReadOnlyCollection<ReceiptPaymentView>> ListCollectibleForUpdateAsync(
        Guid retailerId, IReadOnlyCollection<Guid>? paymentIds, CancellationToken cancellationToken)
    {
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var query = from payment in db.PaymentRecords
                    join order in db.SalesOrderRecords on payment.SalesOrderId equals order.Id
                    where order.RetailerId == retailerId && payment.Method == OrderPaymentMethods.Credit
                        && payment.Status != PaymentStatuses.Paid
                        && payment.Status != PaymentStatuses.Cancelled
                        && !db.RetailerDebtPayOsAllocationRecords.Any(a => a.PaymentId == payment.Id &&
                            db.RetailerDebtPayOsCheckoutRecords.Any(c => c.Id == a.CheckoutId &&
                                c.Status == RetailerDebtPayOsStatuses.Pending &&
                                (!c.PayOsExpiredAt.HasValue || c.PayOsExpiredAt > nowUnix)))
                    select new { payment, order };
        if (paymentIds is { Count: > 0 }) query = query.Where(x => paymentIds.Contains(x.payment.Id));
        var rows = await query.ToArrayAsync(cancellationToken);
        var orderIds = rows.Select(x => x.order.Id).ToArray();
        var disputes = await (from d in db.ReceiptDiscrepancyRecords.AsNoTracking()
                              join i in db.ReceiptDiscrepancyItemRecords.AsNoTracking()
                                  on d.Id equals i.ReceiptDiscrepancyId
                              where orderIds.Contains(d.SalesOrderId) && d.ResolvedAt == null
                              select new { d.SalesOrderId, i.AdjustmentAmount })
            .ToArrayAsync(cancellationToken);
        return rows.Select(x => new ReceiptPaymentView(
                x.payment, x.order,
                disputes.Where(d => d.SalesOrderId == x.order.Id).Sum(d => d.AdjustmentAmount)))
            .Where(x => x.Payment.AdjustedAmount - x.Payment.ConfirmedAmount - x.OpenDisputedAmount > 0)
            .ToArray();
    }

    public async Task<IReadOnlyCollection<RetailerDebtPayOsCheckout>> ListPendingForPaymentsAsync(
        Guid retailerId, IReadOnlyCollection<Guid>? paymentIds, CancellationToken cancellationToken)
    {
        var query = from checkout in db.RetailerDebtPayOsCheckoutRecords.AsNoTracking()
                    join allocation in db.RetailerDebtPayOsAllocationRecords.AsNoTracking()
                        on checkout.Id equals allocation.CheckoutId
                    where checkout.RetailerId == retailerId &&
                        checkout.Status == RetailerDebtPayOsStatuses.Pending
                    select new { checkout, allocation.PaymentId };
        if (paymentIds is { Count: > 0 })
            query = query.Where(x => paymentIds.Contains(x.PaymentId));
        return (await query.Select(x => x.checkout).Distinct().ToArrayAsync(cancellationToken));
    }

    public Task<RetailerDebtCheckoutView?> FindByOperationKeyAsync(
        string operationKey, bool forUpdate, CancellationToken cancellationToken) =>
        LoadAsync(db.RetailerDebtPayOsCheckoutRecords.Where(x => x.OperationKey == operationKey), forUpdate, cancellationToken);

    public Task<RetailerDebtCheckoutView?> FindByPayOsOrderCodeAsync(
        long payOsOrderCode, bool forUpdate, CancellationToken cancellationToken) =>
        LoadAsync(db.RetailerDebtPayOsCheckoutRecords.Where(x => x.PayOsOrderCode == payOsOrderCode), forUpdate, cancellationToken);

    public Task<RetailerDebtCheckoutView?> FindByIdForRetailerAsync(
        Guid checkoutId, Guid retailerId, CancellationToken cancellationToken) =>
        LoadAsync(db.RetailerDebtPayOsCheckoutRecords.Where(x => x.Id == checkoutId && x.RetailerId == retailerId), false, cancellationToken);

    public void AddCheckout(RetailerDebtPayOsCheckout checkout) => db.RetailerDebtPayOsCheckoutRecords.Add(checkout);
    public void AddAllocations(IEnumerable<RetailerDebtPayOsAllocation> allocations) => db.RetailerDebtPayOsAllocationRecords.AddRange(allocations);
    public void AddReceipt(PaymentReceipt receipt, IEnumerable<PaymentReceiptAllocation> allocations)
    {
        db.PaymentReceiptRecords.Add(receipt);
        db.PaymentReceiptAllocationRecords.AddRange(allocations);
    }
    public void AddAuditLog(AuditLog auditLog) => db.AuditLogRecords.Add(auditLog);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            throw PaymentOperationException.Conflict("Debt checkout changed concurrently. Refresh and retry.");
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw PaymentOperationException.Conflict("Debt checkout conflicts with an existing operation.");
        }
    }

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try { await operation(cancellationToken); await transaction.CommitAsync(cancellationToken); }
            catch { await transaction.RollbackAsync(cancellationToken); throw; }
        });
    }

    public void ClearTracking() => db.ChangeTracker.Clear();

    private async Task<RetailerDebtCheckoutView?> LoadAsync(
        IQueryable<RetailerDebtPayOsCheckout> query, bool forUpdate, CancellationToken cancellationToken)
    {
        if (!forUpdate) query = query.AsNoTracking();
        var checkout = await query.SingleOrDefaultAsync(cancellationToken);
        if (checkout is null) return null;
        var retailer = await db.RetailerRecords.AsNoTracking().SingleAsync(x => x.Id == checkout.RetailerId, cancellationToken);
        var allocations = await db.RetailerDebtPayOsAllocationRecords.AsNoTracking()
            .Where(x => x.CheckoutId == checkout.Id).ToArrayAsync(cancellationToken);
        var paymentIds = allocations.Select(x => x.PaymentId).ToArray();
        var rows = await (from payment in forUpdate ? db.PaymentRecords : db.PaymentRecords.AsNoTracking()
                          join order in forUpdate ? db.SalesOrderRecords : db.SalesOrderRecords.AsNoTracking()
                              on payment.SalesOrderId equals order.Id
                          where paymentIds.Contains(payment.Id)
                          select new { payment, order }).ToArrayAsync(cancellationToken);
        var orderIds = rows.Select(x => x.order.Id).ToArray();
        var disputes = await (from d in db.ReceiptDiscrepancyRecords.AsNoTracking()
                              join i in db.ReceiptDiscrepancyItemRecords.AsNoTracking() on d.Id equals i.ReceiptDiscrepancyId
                              where orderIds.Contains(d.SalesOrderId) && d.ResolvedAt == null
                              select new { d.SalesOrderId, i.AdjustmentAmount }).ToArrayAsync(cancellationToken);
        var payments = rows.Select(x => new ReceiptPaymentView(x.payment, x.order,
            disputes.Where(d => d.SalesOrderId == x.order.Id).Sum(d => d.AdjustmentAmount))).ToArray();
        return new(checkout, retailer, allocations, payments);
    }
}
