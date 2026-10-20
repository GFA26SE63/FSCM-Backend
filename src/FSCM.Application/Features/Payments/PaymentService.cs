using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Payments;

/// <summary>
/// Records and confirms payments against sales orders. Payment method and status are tracked
/// independently of the order lifecycle, supporting prepaid transfer, prepaid cash and
/// cash-on-delivery (COD) flows (BE Sprint 1 - "Thanh toán COD / trả trước").
/// </summary>
public sealed class PaymentService(IPaymentStore store, TimeProvider timeProvider)
{
    public async Task<PaymentResult> RecordAsync(
        Guid actorUserId, string actorRole, RecordPaymentData data, CancellationToken cancellationToken)
    {
        if (data.SalesOrderId == Guid.Empty)
            throw PaymentOperationException.Validation("Sales order identifier is required.");
        var method = NormalizeMethod(data.Method);
        if (data.Amount <= 0 || decimal.Round(data.Amount, 2) != data.Amount)
            throw PaymentOperationException.Validation("Payment amount must be a positive value with at most two decimals.");
        var note = CleanNote(data.Note);

        var order = await store.FindOrderAsync(data.SalesOrderId, cancellationToken)
            ?? throw PaymentOperationException.NotFound("Sales order was not found.");
        if (!await store.CanAccessOrderAsync(actorUserId, actorRole, order, cancellationToken))
            throw PaymentOperationException.Forbidden("You cannot record payments for this order.");

        // Prevent over-collection: confirmed payments must not exceed the order final amount.
        var alreadyConfirmed = await store.SumConfirmedAmountAsync(order.Id, cancellationToken);
        var willConfirm = data.MarkPaid ? data.Amount : 0m;
        if (order.FinalAmount > 0 && alreadyConfirmed + willConfirm > order.FinalAmount)
            throw PaymentOperationException.Conflict(
                "Confirmed payments would exceed the order total amount.");

        var now = timeProvider.GetUtcNow();
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            SalesOrderId = order.Id,
            Method = method,
            Status = data.MarkPaid ? PaymentStatuses.Paid : PaymentStatuses.Pending,
            Amount = data.Amount,
            ConfirmedByUserId = data.MarkPaid ? actorUserId : null,
            ConfirmedAt = data.MarkPaid ? now : null,
            Note = note,
            CreatedAt = now,
            UpdatedAt = now
        };
        store.AddPayment(payment);
        await store.SaveChangesAsync(cancellationToken);
        return ToResult((await store.FindPaymentAsync(payment.Id, cancellationToken))!);
    }

    public async Task<PaymentResult> ConfirmAsync(
        Guid paymentId, Guid actorUserId, string actorRole, string? note, CancellationToken cancellationToken)
    {
        var payment = await store.FindPaymentForUpdateAsync(paymentId, cancellationToken)
            ?? throw PaymentOperationException.NotFound("Payment was not found.");
        var order = await store.FindOrderAsync(payment.SalesOrderId, cancellationToken)
            ?? throw PaymentOperationException.NotFound("Sales order was not found.");
        if (!await store.CanAccessOrderAsync(actorUserId, actorRole, order, cancellationToken))
            throw PaymentOperationException.Forbidden("You cannot confirm payments for this order.");

        if (payment.Status == PaymentStatuses.Paid)
            return ToResult((await store.FindPaymentAsync(payment.Id, cancellationToken))!);
        if (payment.Status != PaymentStatuses.Pending)
            throw PaymentOperationException.Conflict("Only a pending payment can be confirmed.");

        var alreadyConfirmed = await store.SumConfirmedAmountAsync(order.Id, cancellationToken);
        if (order.FinalAmount > 0 && alreadyConfirmed + payment.Amount > order.FinalAmount)
            throw PaymentOperationException.Conflict("Confirmed payments would exceed the order total amount.");

        var now = timeProvider.GetUtcNow();
        payment.Status = PaymentStatuses.Paid;
        payment.ConfirmedByUserId = actorUserId;
        payment.ConfirmedAt = now;
        if (!string.IsNullOrWhiteSpace(note)) payment.Note = CleanNote(note);
        payment.UpdatedAt = now;
        await store.SaveChangesAsync(cancellationToken);
        return ToResult((await store.FindPaymentAsync(payment.Id, cancellationToken))!);
    }

    public async Task<PaymentResult> CancelAsync(
        Guid paymentId, Guid actorUserId, string actorRole, string? reason, CancellationToken cancellationToken)
    {
        var payment = await store.FindPaymentForUpdateAsync(paymentId, cancellationToken)
            ?? throw PaymentOperationException.NotFound("Payment was not found.");
        var order = await store.FindOrderAsync(payment.SalesOrderId, cancellationToken)
            ?? throw PaymentOperationException.NotFound("Sales order was not found.");
        if (!await store.CanAccessOrderAsync(actorUserId, actorRole, order, cancellationToken))
            throw PaymentOperationException.Forbidden("You cannot cancel payments for this order.");

        if (payment.Status == PaymentStatuses.Cancelled)
            return ToResult((await store.FindPaymentAsync(payment.Id, cancellationToken))!);
        if (payment.Status != PaymentStatuses.Pending)
            throw PaymentOperationException.Conflict("Only a pending payment can be cancelled.");

        var now = timeProvider.GetUtcNow();
        payment.Status = PaymentStatuses.Cancelled;
        payment.Note = CleanNote(reason);
        payment.UpdatedAt = now;
        await store.SaveChangesAsync(cancellationToken);
        return ToResult((await store.FindPaymentAsync(payment.Id, cancellationToken))!);
    }

    public async Task<IReadOnlyCollection<PaymentResult>> ListByOrderAsync(
        Guid salesOrderId, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        var order = await store.FindOrderAsync(salesOrderId, cancellationToken)
            ?? throw PaymentOperationException.NotFound("Sales order was not found.");
        if (!await store.CanAccessOrderAsync(actorUserId, actorRole, order, cancellationToken))
            throw PaymentOperationException.Forbidden("You cannot view payments for this order.");
        return (await store.ListByOrderAsync(salesOrderId, cancellationToken)).Select(ToResult).ToArray();
    }

    public async Task<PaymentResult> GetAsync(
        Guid paymentId, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        var view = await store.FindPaymentAsync(paymentId, cancellationToken)
            ?? throw PaymentOperationException.NotFound("Payment was not found.");
        var order = await store.FindOrderAsync(view.Payment.SalesOrderId, cancellationToken)
            ?? throw PaymentOperationException.NotFound("Sales order was not found.");
        if (!await store.CanAccessOrderAsync(actorUserId, actorRole, order, cancellationToken))
            throw PaymentOperationException.Forbidden("You cannot view this payment.");
        return ToResult(view);
    }

    private static string NormalizeMethod(string method)
    {
        if (string.IsNullOrWhiteSpace(method))
            throw PaymentOperationException.Validation("Payment method is required.");
        var value = method.Trim();
        return value.ToLowerInvariant() switch
        {
            "cod" or "cashondelivery" => PaymentMethods.CashOnDelivery,
            "prepaidtransfer" or "transfer" => PaymentMethods.PrepaidTransfer,
            "prepaidcash" or "cash" => PaymentMethods.PrepaidCash,
            _ when PaymentMethods.All.Contains(value) => value,
            _ => throw PaymentOperationException.Validation(
                "Payment method must be COD, PrepaidTransfer, or PrepaidCash.")
        };
    }

    private static string? CleanNote(string? value)
    {
        value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (value?.Length > 500) throw PaymentOperationException.Validation("Note cannot exceed 500 characters.");
        return value;
    }

    private static PaymentResult ToResult(PaymentView view) => new(
        view.Payment.Id, view.Payment.SalesOrderId, view.OrderCode, view.Payment.Method,
        view.Payment.Status, view.Payment.Amount, view.Payment.ConfirmedByUserId,
        view.Payment.ConfirmedAt, view.Payment.Note, view.Payment.CreatedAt, view.Payment.UpdatedAt);
}
