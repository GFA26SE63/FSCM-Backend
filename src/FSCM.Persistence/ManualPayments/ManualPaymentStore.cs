using FSCM.Application.Features.ManualPayments;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FSCM.Persistence.ManualPayments;

public sealed class ManualPaymentStore(AppDbContext dbContext) : IManualPaymentStore
{
    public Task<SalesOrder?> FindOrderAsync(Guid salesOrderId, CancellationToken cancellationToken) =>
        dbContext.SalesOrderRecords.AsNoTracking().SingleOrDefaultAsync(item => item.Id == salesOrderId, cancellationToken);

    public Task<ManualPayment?> FindForUpdateAsync(Guid paymentId, CancellationToken cancellationToken) =>
        dbContext.ManualPaymentRecords.SingleOrDefaultAsync(item => item.Id == paymentId, cancellationToken);

    public Task<ManualPaymentView?> FindAsync(Guid paymentId, CancellationToken cancellationToken) =>
        (from payment in dbContext.ManualPaymentRecords.AsNoTracking()
         join order in dbContext.SalesOrderRecords.AsNoTracking() on payment.SalesOrderId equals order.Id
         where payment.Id == paymentId
         select new ManualPaymentView(payment, order.OrderCode))
        .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyCollection<ManualPaymentView>> ListByOrderAsync(Guid salesOrderId, CancellationToken cancellationToken) =>
        await (from payment in dbContext.ManualPaymentRecords.AsNoTracking()
               join order in dbContext.SalesOrderRecords.AsNoTracking() on payment.SalesOrderId equals order.Id
               where payment.SalesOrderId == salesOrderId
               orderby payment.CreatedAt
               select new ManualPaymentView(payment, order.OrderCode))
            .ToArrayAsync(cancellationToken);

    public async Task<decimal> SumConfirmedAmountAsync(Guid salesOrderId, CancellationToken cancellationToken) =>
        await dbContext.ManualPaymentRecords.AsNoTracking()
            .Where(item => item.SalesOrderId == salesOrderId && item.Status == ManualPaymentStatuses.Paid)
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

    public void Add(ManualPayment payment) => dbContext.ManualPaymentRecords.Add(payment);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ManualPaymentOperationException.Conflict("Payment was changed by another operation. Retry the request.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw ManualPaymentOperationException.Conflict("A concurrent payment operation created the same record. Refresh and retry.");
        }
    }
}
