using FSCM.Application.Features.Payments;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FSCM.Persistence.Payments;

public sealed class PaymentStore(AppDbContext dbContext) : IPaymentStore
{
    public Task<SalesOrder?> FindOrderAsync(Guid salesOrderId, CancellationToken cancellationToken) =>
        dbContext.SalesOrderRecords.AsNoTracking().SingleOrDefaultAsync(item => item.Id == salesOrderId, cancellationToken);

    public Task<Payment?> FindPaymentForUpdateAsync(Guid paymentId, CancellationToken cancellationToken) =>
        dbContext.PaymentRecords.SingleOrDefaultAsync(item => item.Id == paymentId, cancellationToken);

    public Task<PaymentView?> FindPaymentAsync(Guid paymentId, CancellationToken cancellationToken) =>
        (from payment in dbContext.PaymentRecords.AsNoTracking()
         join order in dbContext.SalesOrderRecords.AsNoTracking() on payment.SalesOrderId equals order.Id
         where payment.Id == paymentId
         select new PaymentView(payment, order.OrderCode))
        .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyCollection<PaymentView>> ListByOrderAsync(Guid salesOrderId, CancellationToken cancellationToken) =>
        await (from payment in dbContext.PaymentRecords.AsNoTracking()
               join order in dbContext.SalesOrderRecords.AsNoTracking() on payment.SalesOrderId equals order.Id
               where payment.SalesOrderId == salesOrderId
               orderby payment.CreatedAt
               select new PaymentView(payment, order.OrderCode))
            .ToArrayAsync(cancellationToken);

    public async Task<decimal> SumConfirmedAmountAsync(Guid salesOrderId, CancellationToken cancellationToken) =>
        await dbContext.PaymentRecords.AsNoTracking()
            .Where(item => item.SalesOrderId == salesOrderId && item.Status == PaymentStatuses.Paid)
            .SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0m;

    public async Task<bool> CanAccessOrderAsync(
        Guid userId, string role, SalesOrder order, CancellationToken cancellationToken)
    {
        if (role is SystemRoles.Admin or SystemRoles.Operator) return true;
        if (role == SystemRoles.Sales)
        {
            if (order.CreatedByUserId == userId) return true;
            return await dbContext.SalesGroupMemberRecords.AsNoTracking().AnyAsync(
                item => item.UserAccountId == userId && item.SalesGroupId == order.SalesGroupId && item.IsActive,
                cancellationToken);
        }

        return false;
    }

    public void AddPayment(Payment payment) => dbContext.PaymentRecords.Add(payment);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw PaymentOperationException.Conflict("Payment was changed by another operation. Retry the request.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw PaymentOperationException.Conflict("A concurrent payment operation created the same record. Refresh and retry.");
        }
    }
}
