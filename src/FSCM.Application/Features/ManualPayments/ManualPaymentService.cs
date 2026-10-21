using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.ManualPayments;

/// <summary>
/// Records and confirms manual payments against sales orders. Supports cash-on-delivery (COD)
/// and prepaid (transfer/cash) collection, tracked independently of the order lifecycle and of
/// the PayOS receivable on the PAYMENT table (BE Sprint 1 - "Thanh toán COD / trả trước").
/// </summary>
public sealed class ManualPaymentService(IManualPaymentStore store, TimeProvider timeProvider)
{
    public async Task<ManualPaymentResult> RecordAsync(
        Guid actorUserId, string actorRole, RecordManualPaymentData data, CancellationToken cancellationToken)
    {
        if (data.SalesOrderId == Guid.Empty)
            throw ManualPaymentOperationException.Validation("Sales order identifier is required.");
        var method = NormalizeMethod(data.Method);
        if (data.Amount <= 0 || decimal.Round(data.Amount, 2) != data.Amount)
            throw ManualPaymentOperationException.Validation("Payment amount must be a positive value with at most two decimals.");
        var note = CleanNote(data.Note);

        var order = await store.FindOrderAsync(data.SalesOrderId, cancellationToken)
            ?? throw ManualPaymentOperationException.NotFound("Sales order was not found.");
        if (!await store.CanAccessOrderAsync(actorUserId, actorRole, order, cancellationToken))
            throw ManualPaymentOperationException.Forbidden("You cannot record payments for this order.");

        var alreadyConfirmed = await store.SumConfirmedAmountAsync(order.Id, cancellationToken);
        var willConfirm = data.MarkPaid ? data.Amount : 0m;
        if (order.FinalAmount > 0 && alreadyConfirmed + willConfirm > order.FinalAmount)
            throw ManualPaymentOperationException.Conflict("Confirmed payments would exceed the order total amount.");

        var now = timeProvider.GetUtcNow();
        var payment = new ManualPayment
        {
            Id = Guid.NewGuid(),
            SalesOrderId = order.Id,
            Method = method,
            Status = data.MarkPaid ? ManualPaymentStatuses.Paid : ManualPaymentStatuses.Pending,
            Amount = data.Amount,
            ConfirmedByUserId = data.MarkPaid ? actorUserId : null,
            ConfirmedAt = data.MarkPaid ? now : null,
            Note = note,
            CreatedAt = now,
            UpdatedAt = now
        };
        store.Add(payment);
        await store.SaveChangesAsync(cancellationToken);
        return ToResult((await store.FindAsync(payment.Id, cancellationToken))!);
    }

    public async Task<ManualPaymentResult> ConfirmAsync(
        Guid paymentId, Guid actorUserId, string actorRole, string? note, CancellationToken cancellationToken)
    {
        var payment = await store.FindForUpdateAsync(paymentId, cancellationToken)
            ?? throw ManualPaymentOperationException.NotFound("Payment was not found.");
        var order = await store.FindOrderAsync(payment.SalesOrderId, cancellationToken)
            ?? throw ManualPaymentOperationException.NotFound("Sales order was not found.");
        if (!await store.CanAccessOrderAsync(actorUserId, actorRole, order, cancellationToken))
            throw ManualPaymentOperationException.Forbidden("You cannot confirm payments for this order.");

        if (payment.Status == ManualPaymentStatuses.Paid)
            return ToResult((await store.FindAsync(payment.Id, cancellationToken))!);
        if (payment.Status != ManualPaymentStatuses.Pending)
            throw ManualPaymentOperationException.Conflict("Only a pending payment can be confirmed.");

        var alreadyConfirmed = await store.SumConfirmedAmountAsync(order.Id, cancellationToken);
        if (order.FinalAmount > 0 && alreadyConfirmed + payment.Amount > order.FinalAmount)
            throw ManualPaymentOperationException.Conflict("Confirmed payments would exceed the order total amount.");

        var now = timeProvider.GetUtcNow();
        payment.Status = ManualPaymentStatuses.Paid;
        payment.ConfirmedByUserId = actorUserId;
        payment.ConfirmedAt = now;
        if (!string.IsNullOrWhiteSpace(note)) payment.Note = CleanNote(note);
        payment.UpdatedAt = now;
        await store.SaveChangesAsync(cancellationToken);
        return ToResult((await store.FindAsync(payment.Id, cancellationToken))!);
    }

    public async Task<ManualPaymentResult> CancelAsync(
        Guid paymentId, Guid actorUserId, string actorRole, string? reason, CancellationToken cancellationToken)
    {
        var payment = await store.FindForUpdateAsync(paymentId, cancellationToken)
            ?? throw ManualPaymentOperationException.NotFound("Payment was not found.");
        var order = await store.FindOrderAsync(payment.SalesOrderId, cancellationToken)
            ?? throw ManualPaymentOperationException.NotFound("Sales order was not found.");
        if (!await store.CanAccessOrderAsync(actorUserId, actorRole, order, cancellationToken))
            throw ManualPaymentOperationException.Forbidden("You cannot cancel payments for this order.");

        if (payment.Status == ManualPaymentStatuses.Cancelled)
            return ToResult((await store.FindAsync(payment.Id, cancellationToken))!);
        if (payment.Status != ManualPaymentStatuses.Pending)
            throw ManualPaymentOperationException.Conflict("Only a pending payment can be cancelled.");

        var now = timeProvider.GetUtcNow();
        payment.Status = ManualPaymentStatuses.Cancelled;
        payment.Note = CleanNote(reason);
        payment.UpdatedAt = now;
        await store.SaveChangesAsync(cancellationToken);
        return ToResult((await store.FindAsync(payment.Id, cancellationToken))!);
    }

    public async Task<IReadOnlyCollection<ManualPaymentResult>> ListByOrderAsync(
        Guid salesOrderId, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        var order = await store.FindOrderAsync(salesOrderId, cancellationToken)
            ?? throw ManualPaymentOperationException.NotFound("Sales order was not found.");
        if (!await store.CanAccessOrderAsync(actorUserId, actorRole, order, cancellationToken))
            throw ManualPaymentOperationException.Forbidden("You cannot view payments for this order.");
        return (await store.ListByOrderAsync(salesOrderId, cancellationToken)).Select(ToResult).ToArray();
    }

    public async Task<ManualPaymentResult> GetAsync(
        Guid paymentId, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        var view = await store.FindAsync(paymentId, cancellationToken)
            ?? throw ManualPaymentOperationException.NotFound("Payment was not found.");
        var order = await store.FindOrderAsync(view.Payment.SalesOrderId, cancellationToken)
            ?? throw ManualPaymentOperationException.NotFound("Sales order was not found.");
        if (!await store.CanAccessOrderAsync(actorUserId, actorRole, order, cancellationToken))
            throw ManualPaymentOperationException.Forbidden("You cannot view this payment.");
        return ToResult(view);
    }

    private static string NormalizeMethod(string method)
    {
        if (string.IsNullOrWhiteSpace(method))
            throw ManualPaymentOperationException.Validation("Payment method is required.");
        var value = method.Trim();
        return value.ToLowerInvariant() switch
        {
            "cod" or "cashondelivery" => ManualPaymentMethods.CashOnDelivery,
            "prepaidtransfer" or "transfer" => ManualPaymentMethods.PrepaidTransfer,
            "prepaidcash" or "cash" => ManualPaymentMethods.PrepaidCash,
            _ when ManualPaymentMethods.All.Contains(value) => value,
            _ => throw ManualPaymentOperationException.Validation(
                "Payment method must be COD, PrepaidTransfer, or PrepaidCash.")
        };
    }

    private static string? CleanNote(string? value)
    {
        value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (value?.Length > 500) throw ManualPaymentOperationException.Validation("Note cannot exceed 500 characters.");
        return value;
    }

    private static ManualPaymentResult ToResult(ManualPaymentView view) => new(
        view.Payment.Id, view.Payment.SalesOrderId, view.OrderCode, view.Payment.Method,
        view.Payment.Status, view.Payment.Amount, view.Payment.ConfirmedByUserId,
        view.Payment.ConfirmedAt, view.Payment.Note, view.Payment.CreatedAt, view.Payment.UpdatedAt);
}
