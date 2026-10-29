using FSCM.Application.Features.Loyalty;

namespace FSCM.Application.Features.Orders;

/// <summary>What is still keeping an order from being settled, in the order the checks run.</summary>
public enum OrderCompletionBlock
{
    None,
    NotFound,
    NotDelivered,
    NotFullyPaid,
    ComplaintOpen,
    AlreadyCompleted,
}

public sealed record OrderCompletionResult(
    Guid SalesOrderId, bool Completed, OrderCompletionBlock Block, string Explanation);

/// <summary>
/// CH-14: an order is Completed when every part has been received, the money is in, and nothing
/// about it is in dispute.
///
/// The three conditions live here and only here. They were previously spread between the shipment
/// receipt, the payment ledger and the loyalty accrual, each checking its own slice, which is how
/// two of them can quietly drift apart - the loyalty rule and the order status disagreeing about
/// whether an order is finished is the kind of thing nobody notices until the points are wrong.
/// Points now simply trust the status.
///
/// Which condition lands last depends on the order: a credit order is delivered weeks before it is
/// paid, a prepaid one is paid before it ships, and a complaint can hold either. So this is
/// evaluated rather than triggered, and the three events that can change the answer all ask it.
/// </summary>
public sealed class OrderCompletionService(
    IOrderCompletionStore store,
    LoyaltyAccrualService accrual,
    TimeProvider timeProvider)
{
    public async Task<OrderCompletionResult> EvaluateAsync(
        Guid salesOrderId, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var view = await store.FindAsync(salesOrderId, cancellationToken);
        if (view is null)
        {
            return new(salesOrderId, false, OrderCompletionBlock.NotFound, "Không tìm thấy đơn.");
        }

        if (view.Status == OrderStatuses.Completed)
        {
            // Already settled. Say so rather than re-running: these callers fire on every receipt
            // and every payment, and a second completion would be a second lot of points.
            return new(salesOrderId, false, OrderCompletionBlock.AlreadyCompleted,
                "Đơn đã hoàn tất từ trước.");
        }

        if (view.Status != OrderStatuses.Delivered)
        {
            return await HoldAsync(salesOrderId, actorUserId, OrderCompletionBlock.NotDelivered,
                "Đơn chưa nhận đủ mọi phần giao.", cancellationToken);
        }

        if (!view.PaymentSettled)
        {
            return await HoldAsync(salesOrderId, actorUserId, OrderCompletionBlock.NotFullyPaid,
                "Đơn chưa thanh toán đủ.", cancellationToken);
        }

        if (view.HasOpenComplaint)
        {
            return await HoldAsync(salesOrderId, actorUserId, OrderCompletionBlock.ComplaintOpen,
                "Đơn còn khiếu nại đang xử lý.", cancellationToken);
        }

        await store.CompleteAsync(
            salesOrderId, actorUserId, timeProvider.GetUtcNow(), cancellationToken);

        // Only now, and only once: the status is committed before the points are counted, so the
        // accrual reads a settled order rather than being told one is coming.
        await accrual.EvaluateAsync(salesOrderId, actorUserId, cancellationToken);

        return new(salesOrderId, true, OrderCompletionBlock.None, "Đơn đã hoàn tất.");
    }

    /// <summary>
    /// Not settled yet. The accrual is still asked, because it is what records "Chờ cộng" on the
    /// card and names what the order is waiting for - a retailer looking at their points deserves
    /// a reason, not a blank.
    /// </summary>
    private async Task<OrderCompletionResult> HoldAsync(
        Guid salesOrderId, Guid? actorUserId, OrderCompletionBlock block, string why,
        CancellationToken cancellationToken)
    {
        await accrual.EvaluateAsync(salesOrderId, actorUserId, cancellationToken);
        return new(salesOrderId, false, block, why);
    }
}
