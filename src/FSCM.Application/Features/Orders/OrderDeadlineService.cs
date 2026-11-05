using FSCM.Application.Features.Notifications;
using FSCM.Application.Features.Payments;
using FSCM.Application.Features.Quotes;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Orders;

public sealed record OrderDeadlineView(
    SalesOrder Order,
    SalesOrderQuote Quote,
    Payment? Payment);

public sealed record OrderDeadlineSweepResult(
    int ExpiredQuotes,
    int ExpiredPrepaidPayments,
    int PaymentReminders);

public interface IOrderDeadlineStore
{
    Task<IReadOnlyCollection<Guid>> ListExpiredQuoteOrderIdsAsync(
        DateTimeOffset now, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<Guid>> ListExpiredPrepaidOrderIdsAsync(
        DateTimeOffset acceptedBefore, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<Guid>> ListPrepaidReminderOrderIdsAsync(
        DateTimeOffset acceptedAfter, DateTimeOffset acceptedBefore,
        CancellationToken cancellationToken);
    Task<OrderDeadlineView?> LoadForUpdateAsync(Guid orderId, CancellationToken cancellationToken);
    void AddHistory(OrderStatusHistory history);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken);
    void ClearTracking();
}

/// <summary>
/// Closes unattended quotations and unpaid prepaid orders. Both branches release the exact
/// reservation before changing the order status, so expired demand cannot block later SOs.
/// </summary>
public sealed class OrderDeadlineService(
    IOrderDeadlineStore store,
    OrderReleaseService releaseService,
    OrderNotificationService notifications,
    TimeProvider timeProvider)
{
    public async Task<OrderDeadlineSweepResult> SweepAsync(
        int prepaidPaymentWindowDays, CancellationToken cancellationToken)
    {
        if (prepaidPaymentWindowDays is < 1 or > 30)
            throw new ArgumentOutOfRangeException(nameof(prepaidPaymentWindowDays));

        var now = timeProvider.GetUtcNow();
        var quoteIds = await store.ListExpiredQuoteOrderIdsAsync(now, cancellationToken);
        var paymentCutoff = now.AddDays(-prepaidPaymentWindowDays);
        var paymentIds = await store.ListExpiredPrepaidOrderIdsAsync(paymentCutoff, cancellationToken);
        var reminderIds = await store.ListPrepaidReminderOrderIdsAsync(
            now.AddDays(-(prepaidPaymentWindowDays - 1)),
            now.AddDays(-(prepaidPaymentWindowDays - 2)), cancellationToken);

        var expiredQuotes = 0;
        foreach (var orderId in quoteIds)
            if (await ExpireQuoteAsync(orderId, now, cancellationToken)) expiredQuotes++;

        var expiredPayments = 0;
        foreach (var orderId in paymentIds)
            if (await ExpirePaymentAsync(orderId, now, cancellationToken)) expiredPayments++;

        var reminders = 0;
        foreach (var orderId in reminderIds.Except(paymentIds))
        {
            var view = await store.LoadForUpdateAsync(orderId, cancellationToken);
            store.ClearTracking();
            if (view is null || view.Order.Status != OrderStatuses.PaymentPending) continue;
            await notifications.PublishRetailerAsync(orderId, "PrepaidPaymentReminder",
                "Sắp hết hạn thanh toán",
                $"Đơn {view.Order.OrderCode} sẽ bị hủy và trả tồn nếu chưa thanh toán đủ trước hạn.",
                view.Payment?.Id, $"/retailer/orders/{orderId}", cancellationToken);
            reminders++;
        }

        return new(expiredQuotes, expiredPayments, reminders);
    }

    private async Task<bool> ExpireQuoteAsync(
        Guid orderId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var changed = false;
        string? orderCode = null;
        await store.ExecuteInTransactionAsync(async ct =>
        {
            var view = await store.LoadForUpdateAsync(orderId, ct);
            if (view is null || view.Order.Status != OrderStatuses.Quoted ||
                view.Quote.Status != SalesOrderQuoteStatuses.Issued || view.Quote.ExpiresAt > now)
                return;

            var actor = view.Order.CreatedByUserId;
            await releaseService.ReleaseAsync(orderId, actor, view.Order.OrderCode, ct);
            view.Quote.Status = SalesOrderQuoteStatuses.Expired;
            view.Quote.Version++;
            view.Quote.UpdatedAt = now;
            ChangeOrder(view.Order, OrderStatuses.QuoteExpired, actor,
                "Báo giá hết hạn mà Retailer chưa phản hồi.", now);
            await store.SaveChangesAsync(ct);
            orderCode = view.Order.OrderCode;
            changed = true;
        }, cancellationToken);
        store.ClearTracking();
        if (changed)
        {
            await notifications.PublishRetailerAsync(orderId, "QuoteExpired",
                "Báo giá đã hết hạn",
                $"Báo giá đơn {orderCode} đã hết hạn; lượng hàng giữ trước đã được trả về tồn khả dụng.",
                orderId, $"/retailer/orders/{orderId}", cancellationToken);
        }
        return changed;
    }

    private async Task<bool> ExpirePaymentAsync(
        Guid orderId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var changed = false;
        string? orderCode = null;
        await store.ExecuteInTransactionAsync(async ct =>
        {
            var view = await store.LoadForUpdateAsync(orderId, ct);
            if (view?.Payment is null || view.Order.Status != OrderStatuses.PaymentPending ||
                view.Order.PaymentMethod != OrderPaymentMethods.Prepaid ||
                view.Payment.Status != PaymentStatuses.Unpaid)
                return;

            var actor = view.Quote.AcceptedByUserId ?? view.Order.CreatedByUserId;
            await releaseService.ReleaseAsync(orderId, actor, view.Order.OrderCode, ct);
            view.Payment.Status = PaymentStatuses.Cancelled;
            view.Payment.AdjustedAmount = 0;
            view.Payment.Note = "Hủy tự động do quá hạn thanh toán trả trước";
            view.Payment.Version++;
            view.Payment.UpdatedAt = now;
            ChangeOrder(view.Order, OrderStatuses.Withdrawn, actor,
                "Quá hạn thanh toán trả trước; hệ thống tự hủy và trả tồn.", now);
            await store.SaveChangesAsync(ct);
            orderCode = view.Order.OrderCode;
            changed = true;
        }, cancellationToken);
        store.ClearTracking();
        if (changed)
        {
            await notifications.PublishRetailerAsync(orderId, "PrepaidPaymentExpired",
                "Đơn đã bị hủy do quá hạn thanh toán",
                $"Đơn {orderCode} chưa được thanh toán đúng hạn và lượng hàng giữ trước đã được trả về tồn khả dụng.",
                orderId, $"/retailer/orders/{orderId}", cancellationToken);
        }
        return changed;
    }

    private void ChangeOrder(SalesOrder order, string status, Guid actor,
        string reason, DateTimeOffset now)
    {
        var from = order.Status;
        order.Status = status;
        order.Version++;
        order.UpdatedAt = now;
        store.AddHistory(new OrderStatusHistory
        {
            Id = Guid.NewGuid(), SalesOrderId = order.Id,
            FromStatus = from, ToStatus = status,
            ChangedByUserId = actor, Reason = reason, ChangedAt = now
        });
    }
}
