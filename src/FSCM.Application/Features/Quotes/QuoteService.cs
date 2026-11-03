using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Payments;

namespace FSCM.Application.Features.Quotes;

public sealed class QuoteService(
    IQuoteStore store,
    IPaymentPlanService paymentPlanService,
    PurchaseOrderGateService purchaseOrderGateService,
    OrderReleaseService releaseService,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyCollection<SalesOrderQuoteResult>> ListForOrderAsync(Guid orderId, Guid actor, string role, CancellationToken ct)
    {
        var rows = await store.ListForOrderAsync(orderId, ct);
        if (role == SystemRoles.Retailer)
        {
            var visible = new List<QuoteView>();
            foreach (var row in rows)
                if (await store.CanRetailerAccessAsync(row.Quote.Id, actor, ct)) visible.Add(row);
            rows = visible;
        }
        return rows.Select(ToResult).ToArray();
    }

    public async Task<IReadOnlyCollection<SalesOrderQuoteResult>> ListForRunAsync(Guid runId, CancellationToken ct) =>
        (await store.ListForRunAsync(runId, ct)).Select(ToResult).ToArray();

    public async Task<SalesOrderQuoteResult> AcceptAsync(
        Guid id,
        int expectedVersion,
        string paymentMethod,
        Guid actor,
        string role,
        CancellationToken ct)
    {
        EnsureRetailer(role);
        if (!await store.CanRetailerAccessAsync(id, actor, ct)) throw QuoteOperationException.Forbidden("Retailer cannot access this quote.");
        var view = await store.FindAsync(id, true, ct) ?? throw QuoteOperationException.NotFound("Quote was not found.");
        RequirePending(view.Quote, expectedVersion);
        paymentMethod = NormalizePaymentMethod(paymentMethod);
        if (paymentMethod == OrderPaymentMethods.Credit && view.Retailer.CreditTermDays is not (7 or 14 or 30))
            throw QuoteOperationException.Conflict("Retailer is not eligible for an approved credit term.");
        var now = timeProvider.GetUtcNow();
        if (now > view.Quote.ExpiresAt)
        {
            view.Quote.Status = SalesOrderQuoteStatuses.Expired; view.Quote.Version++; view.Quote.UpdatedAt = now;
            await releaseService.ReleaseAsync(view.Order.Id, actor, view.Order.OrderCode, ct);
            var fromStatus = view.Order.Status;
            view.Order.Status = OrderStatuses.QuoteExpired;
            view.Order.Version++; view.Order.UpdatedAt = now;
            store.AddHistory(History(view.Order.Id, fromStatus, OrderStatuses.QuoteExpired, actor,
                "Quote acceptance deadline has passed.", now));
            await store.SaveChangesAsync(ct);
            throw QuoteOperationException.Conflict("Quote acceptance deadline has passed.");
        }
        view.Quote.Status = SalesOrderQuoteStatuses.Accepted;
        view.Quote.AcceptedByUserId = actor; view.Quote.AcceptedAt = now; view.Quote.Version++; view.Quote.UpdatedAt = now;
        view.Order.PaymentMethod = paymentMethod;
        view.Order.CreditTermDays = paymentMethod == OrderPaymentMethods.Credit ? view.Retailer.CreditTermDays : 0;
        view.Order.Status = paymentMethod == OrderPaymentMethods.Credit
            ? OrderStatuses.Quoted
            : OrderStatuses.PaymentPending;
        view.Order.Version++; view.Order.UpdatedAt = now;
        await paymentPlanService.CreateForFinalApprovedOrderAsync(
            view.Order, actor, now, view.Quote.Id.ToString("N"), ct);
        await store.SaveChangesAsync(ct);
        if (paymentMethod == OrderPaymentMethods.Credit)
            await purchaseOrderGateService.TryCreateAsync(view.Order.Id, ct);
        return ToResult(await store.FindAsync(id, false, ct) ?? throw QuoteOperationException.Conflict("Accepted quote could not be reloaded."));
    }

    public async Task<SalesOrderQuoteResult> RejectAsync(Guid id, int expectedVersion, string reason, Guid actor, string role, CancellationToken ct)
    {
        EnsureRetailer(role);
        if (!await store.CanRetailerAccessAsync(id, actor, ct)) throw QuoteOperationException.Forbidden("Retailer cannot access this quote.");
        var view = await store.FindAsync(id, true, ct) ?? throw QuoteOperationException.NotFound("Quote was not found.");
        RequirePending(view.Quote, expectedVersion);
        if (string.IsNullOrWhiteSpace(reason)) throw QuoteOperationException.Validation("Rejection reason is required.");
        var now = timeProvider.GetUtcNow(); view.Quote.Status = SalesOrderQuoteStatuses.Rejected; view.Quote.RejectionReason = reason.Trim(); view.Quote.AcceptedByUserId = actor; view.Quote.AcceptedAt = now; view.Quote.Version++; view.Quote.UpdatedAt = now;
        await releaseService.ReleaseAsync(view.Order.Id, actor, view.Order.OrderCode, ct);
        var fromStatus = view.Order.Status;
        view.Order.Status = OrderStatuses.QuoteRejected;
        view.Order.Version++; view.Order.UpdatedAt = now;
        store.AddHistory(History(view.Order.Id, fromStatus, OrderStatuses.QuoteRejected, actor,
            view.Quote.RejectionReason, now));
        await store.SaveChangesAsync(ct);
        return ToResult(await store.FindAsync(id, false, ct) ?? throw QuoteOperationException.Conflict("Rejected quote could not be reloaded."));
    }

    private static void RequirePending(SalesOrderQuote q, int expected) { if (q.Version != expected) throw QuoteOperationException.Conflict("Quote changed; reload before deciding."); if (q.Status != SalesOrderQuoteStatuses.PendingRetailerAcceptance) throw QuoteOperationException.Conflict("Only a pending quote can be decided."); }
    private static void EnsureRetailer(string role) { if (role != SystemRoles.Retailer) throw QuoteOperationException.Forbidden("Only the retailer can decide a quote."); }
    private static string NormalizePaymentMethod(string value) => value?.Trim() switch
    {
        OrderPaymentMethods.Prepaid => OrderPaymentMethods.Prepaid,
        OrderPaymentMethods.Credit => OrderPaymentMethods.Credit,
        _ => throw QuoteOperationException.Validation(
            "Payment method must be Prepaid (bank transfer) or Credit; COD is not supported.")
    };
    private static OrderStatusHistory History(Guid orderId, string fromStatus, string toStatus,
        Guid actor, string? reason, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), SalesOrderId = orderId, FromStatus = fromStatus, ToStatus = toStatus,
        ChangedByUserId = actor, Reason = reason, ChangedAt = now
    };
    private static SalesOrderQuoteResult ToResult(QuoteView v) => new(v.Quote.Id, v.Order.Id, v.Order.OrderCode, v.Quote.AllocationRunId, v.Quote.RevisionNumber, v.Quote.Status, v.Quote.TotalAmount, v.Quote.IssuedAt, v.Quote.ExpiresAt, v.Quote.AcceptedAt, v.Quote.RejectionReason, v.Quote.PurchaseOrderId, v.PurchaseOrder?.PurchaseOrderCode, v.Quote.Version);
}
