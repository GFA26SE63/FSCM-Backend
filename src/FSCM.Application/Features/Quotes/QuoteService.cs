using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Payments;
using FSCM.Application.Features.Notifications;
using FSCM.Application.Features.AllocationRuns;

namespace FSCM.Application.Features.Quotes;

public sealed class QuoteService(
    IQuoteStore store,
    IPaymentPlanService paymentPlanService,
    PurchaseOrderGateService purchaseOrderGateService,
    OrderReleaseService releaseService,
    TimeProvider timeProvider,
    RetailerCreditPolicy creditPolicy,
    OrderNotificationService? notifications = null,
    AllocationDeliveryGuard? deliveryGuard = null)
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
        if (role != SystemRoles.Admin && !await store.CanRetailerAccessAsync(id, actor, ct))
            throw QuoteOperationException.Forbidden("Retailer cannot access this quote.");
        var view = await store.FindAsync(id, true, ct) ?? throw QuoteOperationException.NotFound("Quote was not found.");
        RequirePending(view.Quote, expectedVersion);
        RequireOrderAwaitingDecision(view.Order);
        paymentMethod = NormalizePaymentMethod(paymentMethod);
        if (paymentMethod == OrderPaymentMethods.Credit)
        {
            try { await creditPolicy.EnsureEligibleAsync(view.Retailer, view.Order.FinalAmount, ct); }
            catch (PaymentOperationException exception) { throw QuoteOperationException.Conflict(exception.Message); }
        }
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
        if (deliveryGuard is not null) await deliveryGuard.ValidateAsync(view.Order.Id, ct);
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
        if (notifications is not null)
        {
            var isCredit = paymentMethod == OrderPaymentMethods.Credit;
            await notifications.PublishRetailerAsync(view.Order.Id,
                isCredit ? "PurchaseOrderIssued" : "PrepaidPaymentRequired",
                isCredit ? "Đơn mua hàng đã được phát hành" : "Đơn đang chờ thanh toán",
                isCredit
                    ? $"Báo giá đơn {view.Order.OrderCode} đã được chấp nhận theo công nợ và chuyển thành PO."
                    : $"Hãy thanh toán đơn {view.Order.OrderCode} trong thời hạn quy định để hệ thống phát hành PO.",
                view.Quote.Id, $"/retailer/orders/{view.Order.Id}", ct);
        }
        return ToResult(await store.FindAsync(id, false, ct) ?? throw QuoteOperationException.Conflict("Accepted quote could not be reloaded."));
    }

    public async Task<SalesOrderQuoteResult> RejectAsync(Guid id, int expectedVersion, string reason, Guid actor, string role, CancellationToken ct)
    {
        EnsureRetailer(role);
        if (role != SystemRoles.Admin && !await store.CanRetailerAccessAsync(id, actor, ct))
            throw QuoteOperationException.Forbidden("Retailer cannot access this quote.");
        var view = await store.FindAsync(id, true, ct) ?? throw QuoteOperationException.NotFound("Quote was not found.");
        RequirePending(view.Quote, expectedVersion);
        RequireOrderAwaitingDecision(view.Order);
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
    private static void RequireOrderAwaitingDecision(SalesOrder order)
    {
        if (order.Status != OrderStatuses.Quoted)
            throw QuoteOperationException.Conflict("Order is no longer awaiting a quote decision.");
    }
    private static void EnsureRetailer(string role)
    {
        if (role is not (SystemRoles.Admin or SystemRoles.Retailer))
            throw QuoteOperationException.Forbidden("Only the retailer or Admin can decide a quote.");
    }
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
