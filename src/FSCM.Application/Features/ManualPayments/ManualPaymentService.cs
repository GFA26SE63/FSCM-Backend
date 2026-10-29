using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Payments;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.ManualPayments;

/// <summary>
/// Records evidence-based COD or prepaid collections while updating the same locked payment
/// ledger used by PayOS. Confirmed collections can therefore never exceed the final approved amount.
/// </summary>
public sealed class ManualPaymentService(
    IManualPaymentStore store,
    IPaymentStore paymentStore,
    OrderCompletionService completion,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public async Task<ManualPaymentResult> RecordAsync(
        Guid actorUserId,
        string actorRole,
        RecordManualPaymentData data,
        string operationKey,
        CancellationToken cancellationToken)
    {
        EnsureWriteRole(actorRole);
        if (data.SalesOrderId == Guid.Empty)
            throw ManualPaymentOperationException.Validation("Sales order identifier is required.");
        operationKey = NormalizeOperationKey(operationKey);
        var method = NormalizeMethod(data.Method);
        ValidateAmount(data.Amount);
        var note = CleanNote(data.Note);
        var requestHash = Hash(data.SalesOrderId, method, data.Amount, data.MarkPaid, note);

        var prior = await store.FindByOperationKeyAsync(operationKey, cancellationToken);
        if (prior is not null)
        {
            if (prior.Payment.RequestHash != requestHash)
                throw ManualPaymentOperationException.Conflict(
                    "Idempotency key was already used for a different manual payment request.");
            var priorOrder = await store.FindOrderAsync(prior.Payment.SalesOrderId, cancellationToken)
                ?? throw ManualPaymentOperationException.NotFound("Sales order was not found.");
            if (!await store.CanAccessOrderAsync(actorUserId, actorRole, priorOrder, cancellationToken))
                throw ManualPaymentOperationException.Forbidden("You cannot record payments for this order.");
            return ToResult(prior);
        }

        Guid paymentId = default;
        await paymentStore.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var order = await store.FindOrderAsync(data.SalesOrderId, transactionCancellationToken)
                ?? throw ManualPaymentOperationException.NotFound("Sales order was not found.");
            if (!await store.CanAccessOrderAsync(
                    actorUserId, actorRole, order, transactionCancellationToken))
                throw ManualPaymentOperationException.Forbidden("You cannot record payments for this order.");
            var ledger = await paymentStore.FindByOrderAsync(
                    order.Id, true, transactionCancellationToken)
                ?? throw ManualPaymentOperationException.Conflict(
                    "Final allocation approval must create the payment ledger before collection.");

            ValidateMethodMatchesOrder(method, ledger.Payment.Method);
            ValidateCollectionTiming(method, ledger.Payment);
            ValidateFullOutstandingAmount(data.Amount, ledger.Payment);
            if (data.MarkPaid) EnsureNoActivePayOsCheckout(ledger.Payment);

            var now = timeProvider.GetUtcNow();
            var payment = new ManualPayment
            {
                Id = Guid.NewGuid(),
                SalesOrderId = order.Id,
                Method = method,
                Status = data.MarkPaid ? ManualPaymentStatuses.Paid : ManualPaymentStatuses.Pending,
                Amount = data.Amount,
                OperationKey = operationKey,
                RequestHash = requestHash,
                ConfirmedByUserId = data.MarkPaid ? actorUserId : null,
                ConfirmedAt = data.MarkPaid ? now : null,
                Note = note,
                Version = 0,
                CreatedAt = now,
                UpdatedAt = now
            };
            if (data.MarkPaid) ApplyLedgerConfirmation(ledger.Payment, payment, actorUserId, now);
            store.Add(payment);
            AddAudit(payment, ledger.Payment, actorUserId,
                data.MarkPaid ? "ManualPaymentRecordedAndConfirmed" : "ManualPaymentRecorded", now);
            await paymentStore.SaveChangesAsync(transactionCancellationToken);
            paymentId = payment.Id;
        }, cancellationToken);

        paymentStore.ClearTracking();
        return ToResult(await store.FindAsync(paymentId, cancellationToken)
            ?? throw ManualPaymentOperationException.Conflict(
                "Manual payment was committed but could not be reloaded."));
    }

    public async Task<ManualPaymentResult> ConfirmAsync(
        Guid paymentId,
        Guid actorUserId,
        string actorRole,
        ChangeManualPaymentData data,
        CancellationToken cancellationToken)
    {
        // Set inside the transaction, read after it: this payment may be the one that settles the
        // order, and that is when its points become possible.
        Guid? settledOrderId = null;
        EnsureWriteRole(actorRole);
        await paymentStore.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var payment = await store.FindForUpdateAsync(paymentId, transactionCancellationToken)
                ?? throw ManualPaymentOperationException.NotFound("Payment was not found.");
            var order = await store.FindOrderAsync(payment.SalesOrderId, transactionCancellationToken)
                ?? throw ManualPaymentOperationException.NotFound("Sales order was not found.");
            if (!await store.CanAccessOrderAsync(
                    actorUserId, actorRole, order, transactionCancellationToken))
                throw ManualPaymentOperationException.Forbidden("You cannot confirm payments for this order.");
            if (payment.Status == ManualPaymentStatuses.Paid) return;
            if (payment.Status != ManualPaymentStatuses.Pending)
                throw ManualPaymentOperationException.Conflict("Only a pending payment can be confirmed.");
            EnsureVersion(payment, data.ExpectedVersion);

            var ledger = await paymentStore.FindByOrderAsync(
                    order.Id, true, transactionCancellationToken)
                ?? throw ManualPaymentOperationException.Conflict("Payment ledger was not found.");
            ValidateMethodMatchesOrder(payment.Method, ledger.Payment.Method);
            ValidateCollectionTiming(payment.Method, ledger.Payment);
            ValidateFullOutstandingAmount(payment.Amount, ledger.Payment);
            EnsureNoActivePayOsCheckout(ledger.Payment);

            var now = timeProvider.GetUtcNow();
            payment.Status = ManualPaymentStatuses.Paid;
            payment.ConfirmedByUserId = actorUserId;
            payment.ConfirmedAt = now;
            if (!string.IsNullOrWhiteSpace(data.Note)) payment.Note = CleanNote(data.Note);
            payment.Version++;
            payment.UpdatedAt = now;
            ApplyLedgerConfirmation(ledger.Payment, payment, actorUserId, now);
            if (ledger.Payment.Status == PaymentStatuses.Paid) settledOrderId = order.Id;
            AddAudit(payment, ledger.Payment, actorUserId, "ManualPaymentConfirmed", now);
            await paymentStore.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);

        paymentStore.ClearTracking();

        // Collecting the money can be the last condition an order was waiting on - a credit order
        // is delivered long before it is paid - so the question is asked again here. Outside the
        // transaction, and idempotent, so it cannot undo a payment that was recorded.
        if (settledOrderId is { } paidOrderId)
        {
            await completion.EvaluateAsync(paidOrderId, actorUserId, cancellationToken);
        }

        return ToResult(await store.FindAsync(paymentId, cancellationToken)
            ?? throw ManualPaymentOperationException.Conflict(
                "Manual payment was committed but could not be reloaded."));
    }

    public async Task<ManualPaymentResult> CancelAsync(
        Guid paymentId,
        Guid actorUserId,
        string actorRole,
        ChangeManualPaymentData data,
        CancellationToken cancellationToken)
    {
        EnsureWriteRole(actorRole);
        await paymentStore.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var payment = await store.FindForUpdateAsync(paymentId, transactionCancellationToken)
                ?? throw ManualPaymentOperationException.NotFound("Payment was not found.");
            var order = await store.FindOrderAsync(payment.SalesOrderId, transactionCancellationToken)
                ?? throw ManualPaymentOperationException.NotFound("Sales order was not found.");
            if (!await store.CanAccessOrderAsync(
                    actorUserId, actorRole, order, transactionCancellationToken))
                throw ManualPaymentOperationException.Forbidden("You cannot cancel payments for this order.");
            if (payment.Status == ManualPaymentStatuses.Cancelled) return;
            if (payment.Status != ManualPaymentStatuses.Pending)
                throw ManualPaymentOperationException.Conflict("Only a pending payment can be cancelled.");
            EnsureVersion(payment, data.ExpectedVersion);

            var now = timeProvider.GetUtcNow();
            payment.Status = ManualPaymentStatuses.Cancelled;
            payment.Note = CleanNote(data.Note);
            payment.Version++;
            payment.UpdatedAt = now;
            var ledger = await paymentStore.FindByOrderAsync(
                    order.Id, true, transactionCancellationToken)
                ?? throw ManualPaymentOperationException.Conflict("Payment ledger was not found.");
            AddAudit(payment, ledger.Payment, actorUserId, "ManualPaymentCancelled", now);
            await paymentStore.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);

        paymentStore.ClearTracking();
        return ToResult(await store.FindAsync(paymentId, cancellationToken)
            ?? throw ManualPaymentOperationException.Conflict(
                "Manual payment was committed but could not be reloaded."));
    }

    public async Task<IReadOnlyCollection<ManualPaymentResult>> ListByOrderAsync(
        Guid salesOrderId, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        EnsureReadRole(actorRole);
        var order = await store.FindOrderAsync(salesOrderId, cancellationToken)
            ?? throw ManualPaymentOperationException.NotFound("Sales order was not found.");
        if (!await store.CanAccessOrderAsync(actorUserId, actorRole, order, cancellationToken))
            throw ManualPaymentOperationException.Forbidden("You cannot view payments for this order.");
        return (await store.ListByOrderAsync(salesOrderId, cancellationToken)).Select(ToResult).ToArray();
    }

    public async Task<ManualPaymentResult> GetAsync(
        Guid paymentId, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        EnsureReadRole(actorRole);
        var view = await store.FindAsync(paymentId, cancellationToken)
            ?? throw ManualPaymentOperationException.NotFound("Payment was not found.");
        var order = await store.FindOrderAsync(view.Payment.SalesOrderId, cancellationToken)
            ?? throw ManualPaymentOperationException.NotFound("Sales order was not found.");
        if (!await store.CanAccessOrderAsync(actorUserId, actorRole, order, cancellationToken))
            throw ManualPaymentOperationException.Forbidden("You cannot view this payment.");
        return ToResult(view);
    }

    private static void ApplyLedgerConfirmation(
        Payment ledger, ManualPayment payment, Guid actorUserId, DateTimeOffset now)
    {
        if (ledger.ConfirmedAmount + payment.Amount > ledger.AdjustedAmount)
            throw ManualPaymentOperationException.Conflict(
                "Confirmed payments would exceed the final approved amount.");
        ledger.ConfirmedAmount += payment.Amount;
        ledger.Status = ledger.ConfirmedAmount == ledger.AdjustedAmount
            ? PaymentStatuses.Paid
            : PaymentStatuses.Unpaid;
        ledger.PaidDate = ledger.Status == PaymentStatuses.Paid
            ? DateOnly.FromDateTime(now.ToOffset(VietnamOffset).DateTime)
            : null;
        ledger.CollectionMethod = payment.Method == ManualPaymentMethods.PrepaidTransfer
            ? PaymentCollectionMethods.BankTransfer
            : PaymentCollectionMethods.Cash;
        ledger.ConfirmedByUserId = actorUserId;
        ledger.ConfirmedAt = now;
        ledger.Note = payment.Note;
        ledger.Version++;
        ledger.UpdatedAt = now;
    }

    private void AddAudit(
        ManualPayment payment, Payment ledger, Guid actorUserId, string action, DateTimeOffset now) =>
        store.AddAuditLog(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserAccountId = actorUserId,
            EntityName = nameof(ManualPayment),
            EntityId = payment.Id,
            Action = action,
            NewValues = JsonSerializer.Serialize(new
            {
                payment.SalesOrderId,
                payment.Method,
                payment.Status,
                payment.Amount,
                payment.OperationKey,
                payment.ConfirmedAt,
                payment.Version,
                finalApprovedAmount = ledger.Amount,
                totalConfirmedAmount = ledger.ConfirmedAmount,
                adjustedAmount = ledger.AdjustedAmount,
                outstandingAmount = Math.Max(0, ledger.AdjustedAmount - ledger.ConfirmedAmount)
            }),
            Reason = payment.Note,
            CreatedAt = now
        });

    private static void ValidateMethodMatchesOrder(string method, string orderMethod)
    {
        var valid = orderMethod switch
        {
            OrderPaymentMethods.Cod => method == ManualPaymentMethods.CashOnDelivery,
            OrderPaymentMethods.Prepaid => method is ManualPaymentMethods.PrepaidTransfer or ManualPaymentMethods.PrepaidCash,
            _ => false
        };
        if (!valid)
            throw ManualPaymentOperationException.Conflict(
                "Manual payment method does not match the final approved order payment method.");
    }

    private static void ValidateCollectionTiming(string method, Payment ledger)
    {
        if (ledger.FinalApprovedAt == default || string.IsNullOrWhiteSpace(ledger.FinalApprovalReference))
            throw ManualPaymentOperationException.Conflict(
                "Collection requires a final allocation approval snapshot.");
        if (method == ManualPaymentMethods.CashOnDelivery && !ledger.DeliveredDate.HasValue)
            throw ManualPaymentOperationException.Conflict(
                "COD can only be recorded or confirmed after complete delivery.");
    }

    private static void ValidateFullOutstandingAmount(decimal amount, Payment ledger)
    {
        var outstanding = Math.Max(0, ledger.AdjustedAmount - ledger.ConfirmedAmount);
        if (amount != outstanding)
            throw ManualPaymentOperationException.Conflict(
                $"Only full payment is supported; outstanding amount is {outstanding:0.##} VND.");
    }

    private static void EnsureNoActivePayOsCheckout(Payment ledger)
    {
        if (ledger.PayOsPaymentLinkId is not null && ledger.PayOsLinkStatus is not ("CANCELLED" or "EXPIRED"))
            throw ManualPaymentOperationException.Conflict(
                "An active PayOS checkout exists; reconcile or expire it before manual confirmation.");
    }

    private static void EnsureWriteRole(string role)
    {
        if (role is not (SystemRoles.Admin or SystemRoles.Operator))
            throw ManualPaymentOperationException.Forbidden(
                "Only Admin or Operator can record, confirm, or cancel manual payments.");
    }

    private static void EnsureReadRole(string role)
    {
        if (role is not (SystemRoles.Admin or SystemRoles.Operator or SystemRoles.SalesManager or
            SystemRoles.Sales or SystemRoles.Retailer))
            throw ManualPaymentOperationException.Forbidden("Role cannot view manual payments.");
    }

    private static void EnsureVersion(ManualPayment payment, int expectedVersion)
    {
        if (payment.Version != expectedVersion)
            throw ManualPaymentOperationException.Conflict(
                $"Manual payment version is {payment.Version}; refresh and retry.");
    }

    private static string NormalizeOperationKey(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        if (value.Length is < 1 or > 80)
            throw ManualPaymentOperationException.Validation(
                "Idempotency-Key is required and cannot exceed 80 characters.");
        return value;
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

    private static void ValidateAmount(decimal amount)
    {
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
            throw ManualPaymentOperationException.Validation(
                "Payment amount must be positive and have at most two decimal places.");
    }

    private static string? CleanNote(string? value)
    {
        value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (value?.Length > 500)
            throw ManualPaymentOperationException.Validation("Note cannot exceed 500 characters.");
        return value;
    }

    private static string Hash(
        Guid orderId, string method, decimal amount, bool markPaid, string? note)
    {
        var canonical = $"{orderId:N}|{method}|{amount:0.00}|{markPaid}|{note}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static ManualPaymentResult ToResult(ManualPaymentView view) => new(
        view.Payment.Id, view.Payment.SalesOrderId, view.OrderCode, view.Payment.Method,
        view.Payment.Status, view.Payment.Amount, view.Payment.ConfirmedByUserId,
        view.Payment.ConfirmedAt, view.Payment.Note, view.Payment.Version,
        view.FinalApprovedAmount, view.AdjustedAmount, view.TotalConfirmedAmount,
        Math.Max(0, view.AdjustedAmount - view.TotalConfirmedAmount),
        view.Payment.CreatedAt, view.Payment.UpdatedAt);
}
