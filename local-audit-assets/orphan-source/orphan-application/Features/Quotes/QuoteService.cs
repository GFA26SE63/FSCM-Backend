using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Quotes;

public sealed class QuoteService(IQuoteStore store, TimeProvider timeProvider)
{
    public async Task<IReadOnlyCollection<SalesOrderQuoteResult>> ListForOrderAsync(Guid orderId, Guid actor, string role, CancellationToken ct)
    {
        var rows = await store.ListForOrderAsync(orderId, ct);
        if (role == SystemRoles.Retailer)
        {
            var allowed = new List<QuoteView>();
            foreach (var row in rows)
                if (await store.CanRetailerAccessAsync(row.Quote.Id, actor, ct)) allowed.Add(row);
            rows = allowed;
        }
        return rows.Select(ToResult).ToArray();
    }

    public async Task<IReadOnlyCollection<SalesOrderQuoteResult>> ListForRunAsync(Guid runId, CancellationToken ct) =>
        (await store.ListForRunAsync(runId, ct)).Select(ToResult).ToArray();

    public async Task<SalesOrderQuoteResult> AcceptAsync(Guid id, int expectedVersion, Guid actor, string role, CancellationToken ct)
    {
        EnsureRetailer(role);
        if (!await store.CanRetailerAccessAsync(id, actor, ct)) throw QuoteOperationException.Forbidden("Retailer cannot access this quote.");
        var view = await store.FindAsync(id, true, ct) ?? throw QuoteOperationException.NotFound("Quote was not found.");
        if (view.Quote.Version != expectedVersion) throw QuoteOperationException.Conflict("Quote changed; reload before accepting.");
        if (view.Quote.Status != SalesOrderQuoteStatuses.PendingRetailerAcceptance) throw QuoteOperationException.Conflict("Only a pending quote can be accepted.");
        var now = timeProvider.GetUtcNow();
        if (now > view.Quote.ExpiresAt) { view.Quote.Status = SalesOrderQuoteStatuses.Expired; view.Quote.Version++; view.Quote.UpdatedAt = now; await store.SaveChangesAsync(ct); throw QuoteOperationException.Conflict("Quote acceptance deadline has passed."); }
        view.Quote.Status = SalesOrderQuoteStatuses.Accepted;
        view.Quote.AcceptedByUserId = actor; view.Quote.AcceptedAt = now; view.Quote.Version++; view.Quote.UpdatedAt = now;
        var po = new PurchaseOrder { Id = Guid.NewGuid(), PurchaseOrderCode = $"PO-{view.Order.OrderCode}", SalesOrderId = view.Order.Id, SalesOrderQuoteId = view.Quote.Id, Status = PurchaseOrderStatuses.Issued, PaymentMethod = view.Order.PaymentMethod, IssuedAt = now, IssuedByUserId = actor, Version = 0, CreatedAt = now, UpdatedAt = now };
        view.Quote.PurchaseOrderId = po.Id;
        store.AddPurchaseOrder(po);
        await store.SaveChangesAsync(ct);
        return ToResult(await store.FindAsync(id, false, ct) ?? throw QuoteOperationException.Conflict("Accepted quote could not be reloaded."));
    }

    public async Task<SalesOrderQuoteResult> RejectAsync(Guid id, int expectedVersion, string reason, Guid actor, string role, CancellationToken ct)
    {
        EnsureRetailer(role);
        if (!await store.CanRetailerAccessAsync(id, actor, ct)) throw QuoteOperationException.Forbidden("Retailer cannot access this quote.");
        var view = await store.FindAsync(id, true, ct) ?? throw QuoteOperationException.NotFound("Quote was not found.");
        if (view.Quote.Version != expectedVersion) throw QuoteOperationException.Conflict("Quote changed; reload before rejecting.");
        if (view.Quote.Status != SalesOrderQuoteStatuses.PendingRetailerAcceptance) throw QuoteOperationException.Conflict("Only a pending quote can be rejected.");
        reason = string.IsNullOrWhiteSpace(reason) ? throw QuoteOperationException.Validation("Rejection reason is required.") : reason.Trim();
        var now = timeProvider.GetUtcNow(); view.Quote.Status = SalesOrderQuoteStatuses.Rejected; view.Quote.RejectionReason = reason; view.Quote.AcceptedByUserId = actor; view.Quote.AcceptedAt = now; view.Quote.Version++; view.Quote.UpdatedAt = now;
        await store.SaveChangesAsync(ct);
        return ToResult(await store.FindAsync(id, false, ct) ?? throw QuoteOperationException.Conflict("Rejected quote could not be reloaded."));
    }

    private static void EnsureRetailer(string role) { if (role != SystemRoles.Retailer) throw QuoteOperationException.Forbidden("Only the retailer can decide a quote."); }
    private static SalesOrderQuoteResult ToResult(QuoteView v) => new(v.Quote.Id, v.Order.Id, v.Order.OrderCode, v.Quote.AllocationRunId, v.Quote.AllocationPlanVersionId, v.Quote.RevisionNumber, v.Quote.Status, v.Quote.TotalAmount, v.Quote.IssuedAt, v.Quote.ExpiresAt, v.Quote.AcceptedAt, v.Quote.RejectionReason, v.Quote.PurchaseOrderId, v.PurchaseOrder?.PurchaseOrderCode, v.Quote.Version);
}
