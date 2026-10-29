namespace FSCM.Application.Features.Payments;

public sealed class RetailerReceivableService(
    IRetailerReceivableStore store,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public async Task<RetailerReceivablePage> ListAsync(
        Guid retailerUserId,
        RetailerReceivableFilter filter,
        CancellationToken cancellationToken)
    {
        Validate(filter);
        var today = Today();
        var query = (await store.ListAsync(retailerUserId, cancellationToken))
            .Select(view => ToResult(view, today));

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var status = NormalizeStatus(filter.Status);
            query = query.Where(item => item.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(filter.OrderCode))
        {
            var orderCode = filter.OrderCode.Trim();
            query = query.Where(item => item.OrderCode.Contains(orderCode, StringComparison.OrdinalIgnoreCase));
        }
        if (filter.DueFrom.HasValue)
            query = query.Where(item => item.DueDate >= filter.DueFrom.Value);
        if (filter.DueTo.HasValue)
            query = query.Where(item => item.DueDate <= filter.DueTo.Value);
        if (filter.Overdue.HasValue)
            query = query.Where(item => item.IsOverdue == filter.Overdue.Value);

        var ordered = query.OrderBy(item => item.DueDate ?? DateOnly.MaxValue)
            .ThenBy(item => item.OrderCode, StringComparer.Ordinal)
            .ToArray();
        var totalPages = ordered.Length == 0 ? 0 : (int)Math.Ceiling(ordered.Length / (double)filter.PageSize);
        var items = ordered.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToArray();
        return new(items, filter.Page, filter.PageSize, ordered.Length, totalPages);
    }

    public async Task<RetailerReceivableResult> GetByOrderAsync(
        Guid retailerUserId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var view = await store.FindByOrderAsync(retailerUserId, orderId, cancellationToken)
            ?? throw PaymentOperationException.NotFound("Receivable was not found for this retailer.");
        return ToResult(view, Today());
    }

    private DateOnly Today() =>
        DateOnly.FromDateTime(timeProvider.GetUtcNow().ToOffset(VietnamOffset).DateTime);

    private static RetailerReceivableResult ToResult(RetailerReceivableView view, DateOnly today)
    {
        var payment = view.Payment.Payment;
        var pendingDisputed = view.Disputes.Where(item => item.Status != "Resolved")
            .Sum(item => item.DisputedAmount);
        var outstanding = Math.Max(0, payment.AdjustedAmount - payment.ConfirmedAmount);
        var collectible = Math.Max(0, outstanding - pendingDisputed);
        var overdue = outstanding > 0 && payment.DueDate.HasValue && today > payment.DueDate.Value;
        var daysOverdue = overdue ? today.DayNumber - payment.DueDate!.Value.DayNumber : 0;
        var effectiveStatus = overdue ? PaymentStatuses.Overdue : payment.Status;
        var refundDue = Math.Max(0,
            payment.ConfirmedAmount - payment.AdjustedAmount - view.Payment.RefundedAmount);

        return new(payment.Id, view.Payment.Order.Id, view.Payment.Order.OrderCode,
            payment.Method, effectiveStatus, view.Payment.Order.SubtotalAmount, payment.Amount,
            Math.Max(0, payment.Amount - payment.AdjustedAmount), payment.AdjustedAmount,
            payment.ConfirmedAmount, outstanding, pendingDisputed, collectible,
            payment.DueDate, overdue, daysOverdue, view.Payment.PendingRefundAmount,
            view.Payment.RefundedAmount, refundDue, view.Collections, view.Disputes,
            view.Refunds, payment.UpdatedAt);
    }

    private static void Validate(RetailerReceivableFilter filter)
    {
        if (filter.Page < 1)
            throw PaymentOperationException.Validation("Page must be at least 1.");
        if (filter.PageSize is < 1 or > 100)
            throw PaymentOperationException.Validation("Page size must be between 1 and 100.");
        if (filter.DueFrom.HasValue && filter.DueTo.HasValue && filter.DueFrom > filter.DueTo)
            throw PaymentOperationException.Validation("Due-from date cannot be after due-to date.");
        if (!string.IsNullOrWhiteSpace(filter.OrderCode) && filter.OrderCode.Trim().Length > 40)
            throw PaymentOperationException.Validation("Order code filter cannot exceed 40 characters.");
        if (!string.IsNullOrWhiteSpace(filter.Status)) NormalizeStatus(filter.Status);
    }

    private static string NormalizeStatus(string value) => value.Trim() switch
    {
        PaymentStatuses.Unpaid => PaymentStatuses.Unpaid,
        PaymentStatuses.Paid => PaymentStatuses.Paid,
        PaymentStatuses.Overdue => PaymentStatuses.Overdue,
        _ => throw PaymentOperationException.Validation("Receivable status filter is invalid.")
    };
}
