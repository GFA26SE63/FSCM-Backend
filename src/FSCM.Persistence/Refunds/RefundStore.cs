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
        // EvidenceRejected is still an open obligation (awaiting corrected evidence), so it counts
        // toward pending alongside PendingRefund and AwaitingVerification; only Refunded is settled.
        var pending = await db.ManualRefundRecords.Where(x => x.PaymentId == paymentId &&
            (x.Status == RefundStatuses.PendingRefund
                || x.Status == RefundStatuses.AwaitingVerification
                || x.Status == RefundStatuses.EvidenceRejected)).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        var refunded = await db.ManualRefundRecords.Where(x => x.PaymentId == paymentId && x.Status == RefundStatuses.Refunded)
            .SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        return (pending, refunded);
    }

    public async Task<RefundClaimSource?> FindClaimSourceAsync(Guid claimId, CancellationToken ct)
    {
        var claim = await db.ReceiptDiscrepancyRecords.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == claimId, ct);
        if (claim is null) return null;
        var adjustment = await db.ReceiptDiscrepancyItemRecords.AsNoTracking()
            .Where(x => x.ReceiptDiscrepancyId == claimId)
            .SumAsync(x => (decimal?)x.AdjustmentAmount, ct) ?? 0m;
        // Every refund record raised for this claim counts against it, whether or not it has been
        // transferred yet, so the same claim cannot be refunded twice.
        var linked = await db.ManualRefundRecords.AsNoTracking()
            .Where(x => x.ReceiptDiscrepancyId == claimId)
            .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
        return new RefundClaimSource(claim.Id, claim.SalesOrderId, claim.ResolvedAt.HasValue,
            claim.ResolutionType, adjustment, linked);
    }

    public async Task<IReadOnlyCollection<RefundObligationResult>> ListObligationsAsync(CancellationToken ct)
    {
        var rows = await (from payment in db.PaymentRecords.AsNoTracking()
                          where payment.ConfirmedAmount > payment.AdjustedAmount
                          join order in db.SalesOrderRecords.AsNoTracking() on payment.SalesOrderId equals order.Id
                          select new { payment, order.OrderCode, order.RetailerNameSnapshot }).ToArrayAsync(ct);
        var paymentIds = rows.Select(x => x.payment.Id).ToArray();
        var refunds = await db.ManualRefundRecords.AsNoTracking()
            .Where(x => paymentIds.Contains(x.PaymentId))
            .Select(x => new { x.PaymentId, x.Amount, x.Status }).ToArrayAsync(ct);
        var orderIds = rows.Select(x => x.payment.SalesOrderId).ToArray();
        var claims = await db.ReceiptDiscrepancyRecords.AsNoTracking()
            .Where(x => orderIds.Contains(x.SalesOrderId) && x.ResolutionType == "PaymentAdjusted")
            .Select(x => new { x.Id, x.SalesOrderId }).ToArrayAsync(ct);
        var result = new List<RefundObligationResult>();
        foreach (var row in rows)
        {
            var mine = refunds.Where(x => x.PaymentId == row.payment.Id).ToArray();
            var refunded = mine.Where(x => x.Status == RefundStatuses.Refunded).Sum(x => x.Amount);
            var pending = mine.Where(x => RefundStatuses.Open.Contains(x.Status)).Sum(x => x.Amount);
            var unrequested = row.payment.ConfirmedAmount - row.payment.AdjustedAmount - refunded - pending;
            if (unrequested <= 0) continue;
            result.Add(new RefundObligationResult(row.payment.Id, row.payment.SalesOrderId, row.OrderCode,
                row.RetailerNameSnapshot, row.payment.ConfirmedAmount, row.payment.AdjustedAmount, refunded,
                pending, unrequested,
                claims.Where(x => x.SalesOrderId == row.payment.SalesOrderId).Select(x => x.Id).ToArray()));
        }
        return result.OrderBy(x => x.OrderCode, StringComparer.Ordinal).ToArray();
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
