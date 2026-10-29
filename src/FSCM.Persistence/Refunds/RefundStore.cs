using FSCM.Application.Features.Refunds;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FSCM.Persistence.Refunds;

public sealed class RefundStore(AppDbContext db) : IRefundStore
{
    public async Task<RefundView?> FindAsync(Guid id, bool forUpdate, CancellationToken ct)
    {
        var query = db.ManualRefundRecords.Where(x => x.Id == id);
        if (!forUpdate) query = query.AsNoTracking();
        var refund = await query.SingleOrDefaultAsync(ct);
        return refund is null ? null : await LoadAsync(refund, forUpdate, ct);
    }

    public async Task<RefundView?> FindByOperationKeyAsync(string key, CancellationToken ct)
    {
        var id = await db.ManualRefundRecords.AsNoTracking()
            .Where(x => x.CreateOperationKey == key || x.EvidenceOperationKey == key || x.VerificationOperationKey == key)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        return id.HasValue ? await FindAsync(id.Value, false, ct) : null;
    }

    public async Task<IReadOnlyCollection<RefundView>> ListAsync(Guid? orderId, string? status,
        Guid actorUserId, string actorRole, CancellationToken ct)
    {
        var query = db.ManualRefundRecords.AsNoTracking().AsQueryable();
        if (orderId.HasValue) query = query.Where(x => x.SalesOrderId == orderId);
        if (status is not null) query = query.Where(x => x.Status == status);
        query = actorRole switch
        {
            SystemRoles.Admin or SystemRoles.Operator => query,
            SystemRoles.Sales => query.Where(x => db.SalesOrderRecords.Any(o => o.Id == x.SalesOrderId && o.CreatedByUserId == actorUserId)),
            SystemRoles.SalesManager => query.Where(x => db.SalesOrderRecords.Any(o => o.Id == x.SalesOrderId && db.SalesGroupMemberRecords.Any(m => m.UserAccountId == actorUserId && m.SalesGroupId == o.SalesGroupId && m.IsActive))),
            SystemRoles.Retailer => query.Where(x => db.SalesOrderRecords.Any(o => o.Id == x.SalesOrderId && db.RetailerRecords.Any(r => r.Id == o.RetailerId && r.UserAccountId == actorUserId))),
            _ => query.Where(_ => false)
        };
        var ids = await query.OrderByDescending(x => x.CreatedAt).Select(x => x.Id).ToArrayAsync(ct);
        var result = new List<RefundView>(ids.Length);
        foreach (var id in ids) { var view = await FindAsync(id, false, ct); if (view is not null) result.Add(view); }
        return result;
    }

    public async Task<(decimal Pending, decimal Refunded)> TotalsAsync(Guid paymentId, CancellationToken ct)
    {
        var pending = await db.ManualRefundRecords.Where(x => x.PaymentId == paymentId &&
            (x.Status == RefundStatuses.PendingRefund || x.Status == RefundStatuses.AwaitingVerification)).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        var refunded = await db.ManualRefundRecords.Where(x => x.PaymentId == paymentId && x.Status == RefundStatuses.Refunded)
            .SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        return (pending, refunded);
    }

    public void Add(ManualRefund refund) => db.ManualRefundRecords.Add(refund);
    public void AddAuditLog(AuditLog auditLog) => db.AuditLogRecords.Add(auditLog);
    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw RefundOperationException.Conflict("Refund changed concurrently. Refresh and retry."); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw RefundOperationException.Conflict("Refund operation conflicts with an existing request."); }
    }

    private async Task<RefundView> LoadAsync(ManualRefund refund, bool forUpdate, CancellationToken ct)
    {
        var paymentQuery = db.PaymentRecords.Where(x => x.Id == refund.PaymentId);
        var orderQuery = db.SalesOrderRecords.Where(x => x.Id == refund.SalesOrderId);
        if (!forUpdate) { paymentQuery = paymentQuery.AsNoTracking(); orderQuery = orderQuery.AsNoTracking(); }
        var payment = await paymentQuery.SingleAsync(ct);
        var order = await orderQuery.SingleAsync(ct);
        var retailer = await db.RetailerRecords.AsNoTracking().SingleAsync(x => x.Id == order.RetailerId, ct);
        var totals = await TotalsAsync(payment.Id, ct);
        return new(refund, payment, order, retailer, totals.Pending, totals.Refunded);
    }
}
