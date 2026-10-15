using FSCM.Application.Features.Payments;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FSCM.Persistence.Payments;

public sealed class PaymentStore(AppDbContext dbContext) : IPaymentStore
{
    public async Task<PaymentView?> FindByOrderAsync(
        Guid orderId, bool forUpdate, CancellationToken cancellationToken)
    {
        var query = dbContext.PaymentRecords.Where(item => item.SalesOrderId == orderId);
        if (!forUpdate) query = query.AsNoTracking();
        var payment = await query.SingleOrDefaultAsync(cancellationToken);
        return payment is null ? null : await LoadAsync(payment, forUpdate, cancellationToken);
    }

    public async Task<PaymentView?> FindByOperationKeyAsync(
        string operationKey, CancellationToken cancellationToken)
    {
        var orderId = await dbContext.PaymentRecords.AsNoTracking()
            .Where(item => item.OperationKey == operationKey)
            .Select(item => (Guid?)item.SalesOrderId)
            .SingleOrDefaultAsync(cancellationToken);
        return orderId.HasValue ? await FindByOrderAsync(orderId.Value, false, cancellationToken) : null;
    }

    public async Task<PaymentView?> FindByPayOsOrderCodeAsync(
        long payOsOrderCode, bool forUpdate, CancellationToken cancellationToken)
    {
        var query = dbContext.PaymentRecords.Where(item => item.PayOsOrderCode == payOsOrderCode);
        if (!forUpdate) query = query.AsNoTracking();
        var payment = await query.SingleOrDefaultAsync(cancellationToken);
        return payment is null ? null : await LoadAsync(payment, forUpdate, cancellationToken);
    }

    public async Task<IReadOnlyCollection<PaymentView>> ListAsync(
        Guid actorUserId,
        string actorRole,
        CancellationToken cancellationToken)
    {
        var query = dbContext.PaymentRecords.AsNoTracking().AsQueryable();
        query = actorRole switch
        {
            SystemRoles.Admin or SystemRoles.Operator => query,
            SystemRoles.Sales => query.Where(item => dbContext.SalesOrderRecords.Any(order =>
                order.Id == item.SalesOrderId && order.CreatedByUserId == actorUserId)),
            SystemRoles.SalesManager => query.Where(item => dbContext.SalesOrderRecords.Any(order =>
                order.Id == item.SalesOrderId && dbContext.SalesGroupMemberRecords.Any(membership =>
                    membership.UserAccountId == actorUserId && membership.SalesGroupId == order.SalesGroupId &&
                    membership.IsActive))),
            SystemRoles.Retailer => query.Where(item => dbContext.SalesOrderRecords.Any(order =>
                order.Id == item.SalesOrderId && dbContext.RetailerRecords.Any(retailer =>
                    retailer.Id == order.RetailerId && retailer.UserAccountId == actorUserId))),
            _ => query.Where(_ => false)
        };
        var orderIds = await query.OrderBy(item => item.DueDate).ThenBy(item => item.CreatedAt)
            .Select(item => item.SalesOrderId).ToArrayAsync(cancellationToken);
        var results = new List<PaymentView>(orderIds.Length);
        foreach (var orderId in orderIds)
        {
            var view = await FindByOrderAsync(orderId, false, cancellationToken);
            if (view is not null) results.Add(view);
        }
        return results;
    }

    public Task<bool> CanViewAsync(
        PaymentView view,
        Guid actorUserId,
        string actorRole,
        CancellationToken cancellationToken) => actorRole switch
        {
            SystemRoles.Admin or SystemRoles.Operator => Task.FromResult(true),
            SystemRoles.Sales => Task.FromResult(view.Order.CreatedByUserId == actorUserId),
            SystemRoles.SalesManager => dbContext.SalesGroupMemberRecords.AsNoTracking().AnyAsync(item =>
                item.UserAccountId == actorUserId && item.SalesGroupId == view.Order.SalesGroupId && item.IsActive,
                cancellationToken),
            SystemRoles.Retailer => Task.FromResult(view.Retailer.UserAccountId == actorUserId),
            _ => Task.FromResult(false)
        };

    public void Add(Payment payment) => dbContext.PaymentRecords.Add(payment);

    public void AddAuditLog(AuditLog auditLog) => dbContext.AuditLogRecords.Add(auditLog);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw PaymentOperationException.Conflict(
                "Payment changed concurrently. Refresh and retry.");
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw PaymentOperationException.Conflict(
                "Payment operation conflicts with an existing business record.");
        }
    }

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await operation(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    public void ClearTracking() => dbContext.ChangeTracker.Clear();

    private async Task<PaymentView> LoadAsync(
        Payment payment, bool forUpdate, CancellationToken cancellationToken)
    {
        var orderQuery = dbContext.SalesOrderRecords.Where(item => item.Id == payment.SalesOrderId);
        if (!forUpdate) orderQuery = orderQuery.AsNoTracking();
        var order = await orderQuery.SingleAsync(cancellationToken);
        var retailer = await dbContext.RetailerRecords.AsNoTracking()
            .SingleAsync(item => item.Id == order.RetailerId, cancellationToken);
        return new(payment, order, retailer);
    }
}
