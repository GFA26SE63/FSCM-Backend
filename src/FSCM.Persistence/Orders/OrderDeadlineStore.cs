using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Payments;
using FSCM.Application.Features.Quotes;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Orders;

public sealed class OrderDeadlineStore(AppDbContext db) : IOrderDeadlineStore
{
    public async Task<IReadOnlyCollection<Guid>> ListExpiredQuoteOrderIdsAsync(
        DateTimeOffset now, CancellationToken ct) =>
        await (from quote in db.SalesOrderQuoteRecords.AsNoTracking()
               join order in db.SalesOrderRecords.AsNoTracking() on quote.SalesOrderId equals order.Id
               where quote.Status == SalesOrderQuoteStatuses.Issued && quote.ExpiresAt <= now &&
                     order.Status == OrderStatuses.Quoted
               select order.Id).ToArrayAsync(ct);

    public async Task<IReadOnlyCollection<Guid>> ListExpiredPrepaidOrderIdsAsync(
        DateTimeOffset acceptedBefore, CancellationToken ct) =>
        await (from quote in db.SalesOrderQuoteRecords.AsNoTracking()
               join order in db.SalesOrderRecords.AsNoTracking() on quote.SalesOrderId equals order.Id
               join payment in db.PaymentRecords.AsNoTracking() on order.Id equals payment.SalesOrderId
               where quote.Status == SalesOrderQuoteStatuses.Accepted &&
                     quote.AcceptedAt <= acceptedBefore &&
                     order.Status == OrderStatuses.PaymentPending &&
                     order.PaymentMethod == OrderPaymentMethods.Prepaid &&
                     payment.Status == PaymentStatuses.Unpaid
               select order.Id).ToArrayAsync(ct);

    public async Task<IReadOnlyCollection<Guid>> ListPrepaidReminderOrderIdsAsync(
        DateTimeOffset acceptedAfter, DateTimeOffset acceptedBefore, CancellationToken ct) =>
        await (from quote in db.SalesOrderQuoteRecords.AsNoTracking()
               join order in db.SalesOrderRecords.AsNoTracking() on quote.SalesOrderId equals order.Id
               join payment in db.PaymentRecords.AsNoTracking() on order.Id equals payment.SalesOrderId
               where quote.Status == SalesOrderQuoteStatuses.Accepted &&
                     quote.AcceptedAt > acceptedAfter && quote.AcceptedAt <= acceptedBefore &&
                     order.Status == OrderStatuses.PaymentPending &&
                     order.PaymentMethod == OrderPaymentMethods.Prepaid &&
                     payment.Status == PaymentStatuses.Unpaid
               select order.Id).ToArrayAsync(ct);

    public async Task<OrderDeadlineView?> LoadForUpdateAsync(Guid orderId, CancellationToken ct)
    {
        var order = await db.SalesOrderRecords.SingleOrDefaultAsync(item => item.Id == orderId, ct);
        if (order is null) return null;
        var quote = await db.SalesOrderQuoteRecords
            .OrderByDescending(item => item.RevisionNumber)
            .FirstOrDefaultAsync(item => item.SalesOrderId == orderId, ct);
        if (quote is null) return null;
        var payment = await db.PaymentRecords.SingleOrDefaultAsync(item => item.SalesOrderId == orderId, ct);
        return new(order, quote, payment);
    }

    public void AddHistory(OrderStatusHistory history) => db.OrderStatusHistoryRecords.Add(history);
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try { await operation(ct); await transaction.CommitAsync(ct); }
            catch { await transaction.RollbackAsync(ct); throw; }
        });
    }
    public void ClearTracking() => db.ChangeTracker.Clear();
}
