using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FSCM.Application.Features.Orders;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Payments;

public sealed class PaymentService(
    IPaymentStore store,
    IPayOsGateway payOsGateway,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public async Task<IReadOnlyCollection<PaymentResult>> ListAsync(
        Guid actorUserId,
        string actorRole,
        string? status,
        CancellationToken cancellationToken)
    {
        ValidateReadRole(actorRole);
        status = NormalizeStatus(status);
        var today = Today();
        return (await store.ListAsync(actorUserId, actorRole, cancellationToken))
            .Select(item => ToResult(item, today))
            .Where(item => status is null || item.Status == status)
            .OrderBy(item => item.DueDate)
            .ThenBy(item => item.OrderCode, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<PaymentResult> GetByOrderAsync(
        Guid orderId,
        Guid actorUserId,
        string actorRole,
        CancellationToken cancellationToken)
    {
        ValidateReadRole(actorRole);
        var view = await store.FindByOrderAsync(orderId, false, cancellationToken)
            ?? throw PaymentOperationException.NotFound("Payment was not found.");
        if (!await store.CanViewAsync(view, actorUserId, actorRole, cancellationToken))
            throw PaymentOperationException.Forbidden("You cannot view this payment.");
        return ToResult(view, Today());
    }

    public async Task<PaymentResult> CollectAsync(
        Guid orderId,
        CollectPaymentData data,
        string operationKey,
        Guid actorUserId,
        string actorRole,
        CancellationToken cancellationToken)
    {
        operationKey = NormalizeOperationKey(operationKey);
        var collectionMethod = NormalizeCollectionMethod(data.CollectionMethod);
        ValidateCollectionAuthority(actorRole, collectionMethod);
        ValidateNote(data.Note);
        var requestHash = Hash(orderId, collectionMethod, data.ExpectedVersion, NormalizeNote(data.Note));

        var prior = await store.FindByOperationKeyAsync(operationKey, cancellationToken);
        if (prior is not null)
        {
            if (prior.Order.Id != orderId || prior.Payment.RequestHash != requestHash)
                throw PaymentOperationException.Conflict(
                    "Operation key was already used for a different payment request.");
            if (!await store.CanViewAsync(prior, actorUserId, actorRole, cancellationToken))
                throw PaymentOperationException.Forbidden("You cannot collect this payment.");
            return ToResult(prior, Today());
        }

        await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var view = await store.FindByOrderAsync(orderId, true, transactionCancellationToken)
                ?? throw PaymentOperationException.NotFound("Payment was not found.");
            if (view.Payment.Method != OrderPaymentMethods.Credit)
                throw PaymentOperationException.Conflict("Only a credit receivable can be collected manually.");
            if (!view.Payment.DeliveredDate.HasValue)
                throw PaymentOperationException.Conflict("Deferred payment can only be collected after complete receipt.");
            if (view.Payment.Status == PaymentStatuses.Paid)
                throw PaymentOperationException.Conflict("Payment was already collected in full.");
            if (view.Payment.PayOsPaymentLinkId is not null)
                throw PaymentOperationException.Conflict(
                    "Payment has an active payOS checkout and cannot be collected manually.");
            if (view.HasOpenDispute)
                throw PaymentOperationException.Conflict(
                    "Payment cannot be collected while the order has an unresolved dispute.");
            if (view.Payment.Version != data.ExpectedVersion)
                throw PaymentOperationException.Conflict(
                    $"Payment version is {view.Payment.Version}; refresh and retry.");
            if (actorRole == SystemRoles.Sales && view.Order.CreatedByUserId != actorUserId)
                throw PaymentOperationException.Forbidden(
                    "Sales can only record cash collection for their own order.");
            var outstanding = Math.Max(0, view.Payment.AdjustedAmount - view.Payment.ConfirmedAmount);
            if (outstanding <= 0)
                throw PaymentOperationException.Conflict("Payment has no outstanding amount to collect.");

            var now = timeProvider.GetUtcNow();
            var oldValues = new
            {
                view.Payment.Status,
                view.Payment.ConfirmedAmount,
                view.Payment.PaidDate,
                view.Payment.CollectionMethod,
                view.Payment.ConfirmedByUserId,
                view.Payment.ConfirmedAt,
                view.Payment.Version
            };
            view.Payment.Status = PaymentStatuses.Paid;
            view.Payment.ConfirmedAmount = view.Payment.AdjustedAmount;
            view.Payment.PaidDate = DateOnly.FromDateTime(now.ToOffset(VietnamOffset).DateTime);
            view.Payment.CollectionMethod = collectionMethod;
            view.Payment.OperationKey = operationKey;
            view.Payment.RequestHash = requestHash;
            view.Payment.ConfirmedByUserId = actorUserId;
            view.Payment.ConfirmedAt = now;
            view.Payment.Note = NormalizeNote(data.Note);
            view.Payment.Version++;
            view.Payment.UpdatedAt = now;
            store.AddAuditLog(new AuditLog
            {
                Id = Guid.NewGuid(),
                UserAccountId = actorUserId,
                EntityName = nameof(Payment),
                EntityId = view.Payment.Id,
                Action = "Collected",
                OldValues = JsonSerializer.Serialize(oldValues),
                NewValues = JsonSerializer.Serialize(new
                {
                    view.Payment.Status,
                    view.Payment.ConfirmedAmount,
                    view.Payment.PaidDate,
                    view.Payment.CollectionMethod,
                    view.Payment.ConfirmedByUserId,
                    view.Payment.ConfirmedAt,
                    view.Payment.Version
                }),
                Reason = view.Payment.Note,
                CreatedAt = now
            });
            await store.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);

        store.ClearTracking();
        return ToResult(await store.FindByOrderAsync(orderId, false, cancellationToken)
            ?? throw PaymentOperationException.Conflict(
                "Payment was committed but could not be reloaded."), Today());
    }

    /// <summary>
    /// F64: adjusts a credit receivable from an approved claim (positive amount reduces the
    /// collectable total against the final price of the affected goods) or a rejected claim
    /// (negative amount restores a prior reduction). Never recomputes shelf-life discounts; the
    /// change applies to the adjusted collectable base only and keeps disputed money separate.
    /// </summary>
    public async Task<PaymentResult> AdjustReceivableAsync(
        Guid orderId,
        AdjustReceivableData data,
        string operationKey,
        Guid actorUserId,
        string actorRole,
        CancellationToken cancellationToken)
    {
        if (actorRole is not (SystemRoles.Admin or SystemRoles.Operator))
            throw PaymentOperationException.Forbidden("Only Admin or Operator can adjust a receivable.");
        operationKey = NormalizeOperationKey(operationKey);
        var reason = string.IsNullOrWhiteSpace(data.Reason)
            ? throw PaymentOperationException.Validation("Adjustment reason is required.")
            : data.Reason.Trim();
        if (reason.Length > 500)
            throw PaymentOperationException.Validation("Adjustment reason cannot exceed 500 characters.");
        if (data.Amount == 0 || decimal.Round(data.Amount, 2) != data.Amount)
            throw PaymentOperationException.Validation(
                "Adjustment amount must be a non-zero value with at most two decimal places.");
        var requestHash = Hash(orderId, data.Amount, "adjust", reason);

        var prior = await store.FindByOperationKeyAsync(operationKey, cancellationToken);
        if (prior is not null)
        {
            if (prior.Order.Id != orderId || prior.Payment.RequestHash != requestHash)
                throw PaymentOperationException.Conflict(
                    "Operation key was already used for a different adjustment request.");
            if (!await store.CanViewAsync(prior, actorUserId, actorRole, cancellationToken))
                throw PaymentOperationException.Forbidden("You cannot adjust this receivable.");
            return ToResult(prior, Today());
        }

        await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var view = await store.FindByOrderAsync(orderId, true, transactionCancellationToken)
                ?? throw PaymentOperationException.NotFound("Payment was not found.");
            if (view.Payment.Method != OrderPaymentMethods.Credit)
                throw PaymentOperationException.Conflict("Only a credit receivable can be adjusted.");
            if (!view.Payment.DeliveredDate.HasValue)
                throw PaymentOperationException.Conflict(
                    "A receivable can only be adjusted after complete receipt.");
            if (view.Payment.Version != data.ExpectedVersion)
                throw PaymentOperationException.Conflict(
                    $"Payment version is {view.Payment.Version}; refresh and retry.");

            // Positive amount reduces the collectable base (approved claim); negative restores it.
            var adjusted = view.Payment.AdjustedAmount - data.Amount;
            if (adjusted < view.Payment.ConfirmedAmount)
                throw PaymentOperationException.Conflict(
                    "Adjustment cannot reduce the receivable below the amount already collected.");
            if (adjusted > view.Payment.Amount)
                throw PaymentOperationException.Conflict(
                    "Adjustment cannot restore the receivable above the approved final total.");

            var now = timeProvider.GetUtcNow();
            var oldValues = new { view.Payment.AdjustedAmount, view.Payment.Status, view.Payment.Version };
            view.Payment.AdjustedAmount = adjusted;
            // If the remaining collectable is fully covered by collections, the receivable is settled.
            if (view.Payment.Status != PaymentStatuses.Paid &&
                view.Payment.ConfirmedAmount >= adjusted && adjusted >= 0 && view.Payment.ConfirmedAmount > 0)
            {
                view.Payment.Status = PaymentStatuses.Paid;
                view.Payment.PaidDate ??= DateOnly.FromDateTime(now.ToOffset(VietnamOffset).DateTime);
            }
            view.Payment.OperationKey = operationKey;
            view.Payment.RequestHash = requestHash;
            view.Payment.Note = reason;
            view.Payment.Version++;
            view.Payment.UpdatedAt = now;
            store.AddAuditLog(new AuditLog
            {
                Id = Guid.NewGuid(),
                UserAccountId = actorUserId,
                EntityName = nameof(Payment),
                EntityId = view.Payment.Id,
                Action = "ReceivableAdjusted",
                OldValues = JsonSerializer.Serialize(oldValues),
                NewValues = JsonSerializer.Serialize(new
                {
                    view.Payment.AdjustedAmount,
                    view.Payment.Status,
                    view.Payment.Version
                }),
                Reason = reason,
                CreatedAt = now
            });
            await store.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);

        store.ClearTracking();
        return ToResult(await store.FindByOrderAsync(orderId, false, cancellationToken)
            ?? throw PaymentOperationException.Conflict(
                "Payment was committed but could not be reloaded."), Today());
    }

    public async Task<PayOsCheckoutResult> CreatePayOsCheckoutAsync(
        Guid orderId,
        CreatePayOsCheckoutData data,
        Guid actorUserId,
        string actorRole,
        CancellationToken cancellationToken)
    {
        ValidateReadRole(actorRole);
        var view = await store.FindByOrderAsync(orderId, false, cancellationToken)
            ?? throw PaymentOperationException.NotFound("Payment was not found.");
        if (!await store.CanViewAsync(view, actorUserId, actorRole, cancellationToken))
            throw PaymentOperationException.Forbidden("You cannot create a checkout for this payment.");
        if (view.Payment.Method != OrderPaymentMethods.Prepaid)
            throw PaymentOperationException.Conflict("Only a prepaid order can use payOS checkout.");
        if (view.Payment.Status == PaymentStatuses.Paid)
            throw PaymentOperationException.Conflict("Payment was already collected in full.");
        if (view.Payment.PayOsPaymentLinkId is not null) return ToCheckoutResult(view.Payment);
        ValidatePayOsEligible(view, data.ExpectedVersion);

        var orderCode = CreatePayOsOrderCode(view.Payment.Id);
        var link = await payOsGateway.CreatePaymentLinkAsync(new(
            orderCode,
            ToPayOsAmount(view.Payment.Amount - view.Payment.ConfirmedAmount),
            $"FSCM {view.Order.OrderCode}",
            view.Order.RetailerNameSnapshot), cancellationToken);
        if (link.OrderCode != orderCode ||
            link.Amount != ToPayOsAmount(view.Payment.Amount - view.Payment.ConfirmedAmount) ||
            string.IsNullOrWhiteSpace(link.PaymentLinkId) || string.IsNullOrWhiteSpace(link.CheckoutUrl))
            throw PaymentOperationException.Gateway("payOS returned an inconsistent payment link.");

        await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var current = await store.FindByOrderAsync(orderId, true, transactionCancellationToken)
                ?? throw PaymentOperationException.NotFound("Payment was not found.");
            if (current.Payment.PayOsPaymentLinkId is not null) return;
            ValidatePayOsEligible(current, data.ExpectedVersion);

            var now = timeProvider.GetUtcNow();
            current.Payment.PayOsOrderCode = link.OrderCode;
            current.Payment.PayOsPaymentLinkId = link.PaymentLinkId;
            current.Payment.PayOsCheckoutUrl = link.CheckoutUrl;
            current.Payment.PayOsQrCode = link.QrCode;
            current.Payment.PayOsLinkStatus = link.Status;
            current.Payment.PayOsExpiredAt = link.ExpiredAt;
            current.Payment.Version++;
            current.Payment.UpdatedAt = now;
            store.AddAuditLog(new AuditLog
            {
                Id = Guid.NewGuid(),
                UserAccountId = actorUserId,
                EntityName = nameof(Payment),
                EntityId = current.Payment.Id,
                Action = "PayOsCheckoutCreated",
                NewValues = JsonSerializer.Serialize(new
                {
                    current.Payment.PayOsOrderCode,
                    current.Payment.PayOsPaymentLinkId,
                    current.Payment.PayOsLinkStatus,
                    current.Payment.Version
                }),
                Reason = "Tạo link thanh toán payOS cho công nợ",
                CreatedAt = now
            });
            await store.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);

        store.ClearTracking();
        return ToCheckoutResult((await store.FindByOrderAsync(orderId, false, cancellationToken)
            ?? throw PaymentOperationException.Conflict(
                "payOS checkout was committed but could not be reloaded.")).Payment);
    }

    public async Task<PayOsWebhookResult> HandlePayOsWebhookAsync(
        PayOsWebhookEnvelope webhook,
        CancellationToken cancellationToken)
    {
        var verified = await payOsGateway.VerifyWebhookAsync(webhook, cancellationToken);
        if (!verified.Success || verified.Code != "00" || verified.Data.Code != "00")
            return new(false, "Webhook was verified but does not represent a successful payment.", null);

        var existing = await store.FindByPayOsOrderCodeAsync(
            verified.Data.OrderCode, false, cancellationToken);
        if (existing is null)
            return new(false, "Webhook was verified but does not match an FSCM payment.", null);

        PayOsWebhookResult? result = null;
        await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var current = await store.FindByPayOsOrderCodeAsync(
                verified.Data.OrderCode, true, transactionCancellationToken)
                ?? throw PaymentOperationException.NotFound("Payment was not found.");
            ValidatePayOsWebhook(current.Payment, verified.Data);
            if (current.Payment.Status == PaymentStatuses.Paid)
            {
                if (current.Payment.CollectionMethod == PaymentCollectionMethods.BankTransfer &&
                    current.Payment.PayOsPaymentLinkId is not null &&
                    current.Payment.PayOsReference == verified.Data.Reference &&
                    current.Payment.PayOsWebhookHash == HashPayOsWebhook(verified.Data))
                {
                    result = new(true, "Payment webhook was already processed.", current.Payment.Id);
                    return;
                }
                throw PaymentOperationException.Conflict("Payment was already collected by another method.");
            }

            var now = timeProvider.GetUtcNow();
            var paidAt = ParsePayOsTransactionTime(verified.Data.TransactionDateTime, now);
            var webhookHash = HashPayOsWebhook(verified.Data);
            if (current.Payment.ConfirmedAmount + verified.Data.Amount > current.Payment.Amount)
                throw PaymentOperationException.Conflict("payOS payment would exceed the final approved amount.");
            var oldValues = new
            {
                current.Payment.Status,
                current.Payment.ConfirmedAmount,
                current.Payment.CollectionMethod,
                current.Payment.Version
            };
            current.Payment.Status = PaymentStatuses.Paid;
            current.Payment.ConfirmedAmount += verified.Data.Amount;
            current.Payment.PaidDate = DateOnly.FromDateTime(paidAt.ToOffset(VietnamOffset).DateTime);
            current.Payment.CollectionMethod = PaymentCollectionMethods.BankTransfer;
            current.Payment.OperationKey = $"payos:{verified.Data.Reference}";
            current.Payment.RequestHash = webhookHash;
            current.Payment.PayOsLinkStatus = "PAID";
            current.Payment.PayOsReference = verified.Data.Reference;
            current.Payment.PayOsWebhookHash = webhookHash;
            current.Payment.ConfirmedByUserId = null;
            current.Payment.ConfirmedAt = paidAt;
            current.Payment.Note = "payOS xác nhận đã nhận đủ tiền";
            current.Payment.Version++;
            current.Payment.UpdatedAt = now;
            store.AddAuditLog(new AuditLog
            {
                Id = Guid.NewGuid(),
                EntityName = nameof(Payment),
                EntityId = current.Payment.Id,
                Action = "PayOsPaymentConfirmed",
                OldValues = JsonSerializer.Serialize(oldValues),
                NewValues = JsonSerializer.Serialize(new
                {
                    current.Payment.Status,
                    current.Payment.ConfirmedAmount,
                    current.Payment.CollectionMethod,
                    current.Payment.PayOsOrderCode,
                    current.Payment.PayOsPaymentLinkId,
                    current.Payment.PayOsReference,
                    current.Payment.PaidDate,
                    current.Payment.ConfirmedAt,
                    current.Payment.Version
                }),
                Reason = current.Payment.Note,
                CreatedAt = now
            });
            await store.SaveChangesAsync(transactionCancellationToken);
            result = new(true, "Payment was confirmed by payOS.", current.Payment.Id);
        }, cancellationToken);
        return result ?? throw PaymentOperationException.Conflict("Payment webhook could not be processed.");
    }

    private DateOnly Today() =>
        DateOnly.FromDateTime(timeProvider.GetUtcNow().ToOffset(VietnamOffset).DateTime);

    private static PaymentResult ToResult(PaymentView view, DateOnly today)
    {
        var overdue = view.Payment.Status != PaymentStatuses.Paid &&
                      view.Payment.DueDate.HasValue && today > view.Payment.DueDate.Value;
        var effectiveStatus = overdue ? PaymentStatuses.Overdue : view.Payment.Status;
        var daysOverdue = overdue ? today.DayNumber - view.Payment.DueDate!.Value.DayNumber : 0;
        return new(
            view.Payment.Id, view.Order.Id, view.Order.OrderCode, view.Retailer.Id,
            view.Order.RetailerCodeSnapshot, view.Order.RetailerNameSnapshot,
            view.Order.CreatedByUserId, view.Payment.Method, effectiveStatus,
            view.Payment.Amount, view.Payment.AdjustedAmount, view.Payment.ConfirmedAmount,
            Math.Max(0, view.Payment.AdjustedAmount - view.Payment.ConfirmedAmount),
            view.PendingRefundAmount, view.RefundedAmount,
            Math.Max(0, view.Payment.ConfirmedAmount - view.Payment.AdjustedAmount - view.RefundedAmount),
            view.Order.CreditTermDays, view.Payment.FinalApprovedAt,
            view.Payment.FinalApprovalReference, view.Payment.DeliveredDate,
            view.Payment.DueDate, daysOverdue, view.Payment.PaidDate,
            view.Payment.CollectionMethod, view.Payment.PayOsOrderCode,
            view.Payment.PayOsPaymentLinkId, view.Payment.PayOsCheckoutUrl,
            view.Payment.PayOsQrCode, view.Payment.PayOsLinkStatus, view.Payment.PayOsExpiredAt,
            view.Payment.PayOsReference,
            view.Payment.ConfirmedByUserId,
            view.Payment.ConfirmedAt, view.Payment.Note, view.Payment.Version,
            view.Payment.CreatedAt, view.Payment.UpdatedAt);
    }

    private static void ValidateReadRole(string role)
    {
        if (role is not (SystemRoles.Admin or SystemRoles.Operator or SystemRoles.SalesManager or
            SystemRoles.Sales or SystemRoles.Retailer))
            throw PaymentOperationException.Forbidden("Role cannot view payments.");
    }

    private static void ValidateCollectionAuthority(string role, string collectionMethod)
    {
        if (role == SystemRoles.Sales && collectionMethod == PaymentCollectionMethods.Cash) return;
        if (role == SystemRoles.Operator && collectionMethod == PaymentCollectionMethods.BankTransfer) return;
        throw PaymentOperationException.Forbidden(
            "Sales records cash collection; Operator records bank transfer collection.");
    }

    private static string? NormalizeStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value is PaymentStatuses.Unpaid or PaymentStatuses.Paid or PaymentStatuses.Overdue) return value;
        throw PaymentOperationException.Validation("Payment status filter is invalid.");
    }

    private static string NormalizeCollectionMethod(string value) => value?.Trim() switch
    {
        PaymentCollectionMethods.Cash => PaymentCollectionMethods.Cash,
        PaymentCollectionMethods.BankTransfer => PaymentCollectionMethods.BankTransfer,
        _ => throw PaymentOperationException.Validation(
            "Collection method must be Cash or BankTransfer.")
    };

    private static string NormalizeOperationKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw PaymentOperationException.Validation("Idempotency-Key header is required.");
        value = value.Trim();
        if (value.Length > 80)
            throw PaymentOperationException.Validation("Idempotency-Key cannot exceed 80 characters.");
        return value;
    }

    private static void ValidateNote(string? note)
    {
        if (note?.Trim().Length > 500)
            throw PaymentOperationException.Validation("Note cannot exceed 500 characters.");
    }

    private static string? NormalizeNote(string? note) =>
        string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    private static string Hash(Guid orderId, string method, int expectedVersion, string? note)
    {
        var canonical = $"{orderId:N}|{method}|{expectedVersion}|{note}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string Hash(Guid orderId, decimal amount, string method, string? note)
    {
        var canonical = $"{orderId:N}|{amount:0.00}|{method}|{note}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static void ValidatePayOsEligible(PaymentView view, int expectedVersion)
    {
        if (view.Payment.Method != OrderPaymentMethods.Prepaid)
            throw PaymentOperationException.Conflict("Only a prepaid order can use payOS checkout.");
        if (view.Payment.FinalApprovedAt == default || string.IsNullOrWhiteSpace(view.Payment.FinalApprovalReference))
            throw PaymentOperationException.Conflict(
                "PayOS checkout requires a final allocation approval snapshot.");
        if (view.Payment.Status == PaymentStatuses.Paid)
            throw PaymentOperationException.Conflict("Payment was already collected in full.");
        if (view.Payment.ConfirmedAmount != 0)
            throw PaymentOperationException.Conflict(
                "PayOS checkout requires the full final approved amount to remain outstanding.");
        if (view.Payment.Version != expectedVersion)
            throw PaymentOperationException.Conflict(
                $"Payment version is {view.Payment.Version}; refresh and retry.");
    }

    private static PayOsCheckoutResult ToCheckoutResult(Payment payment)
    {
        if (payment.PayOsOrderCode is null || payment.PayOsPaymentLinkId is null ||
            payment.PayOsCheckoutUrl is null || payment.PayOsQrCode is null ||
            payment.PayOsLinkStatus is null)
            throw PaymentOperationException.Conflict("Payment does not have a complete payOS checkout.");
        return new(payment.Id, payment.SalesOrderId, payment.PayOsOrderCode.Value,
            payment.Amount, payment.PayOsPaymentLinkId, payment.PayOsCheckoutUrl,
            payment.PayOsQrCode, payment.PayOsLinkStatus, payment.PayOsExpiredAt, payment.Version);
    }

    private static long CreatePayOsOrderCode(Guid paymentId)
    {
        var value = Convert.ToInt64(paymentId.ToString("N")[..13], 16);
        return value == 0 ? 1 : value;
    }

    private static long ToPayOsAmount(decimal amount)
    {
        if (amount <= 0 || amount != decimal.Truncate(amount) || amount > long.MaxValue)
            throw PaymentOperationException.Validation("payOS requires a positive whole-number VND amount.");
        return decimal.ToInt64(amount);
    }

    private static void ValidatePayOsWebhook(Payment payment, PayOsWebhookPayload data)
    {
        if (payment.PayOsPaymentLinkId != data.PaymentLinkId)
            throw PaymentOperationException.Conflict("payOS payment-link identifier does not match.");
        if (ToPayOsAmount(payment.Amount) != data.Amount)
            throw PaymentOperationException.Conflict("payOS payment amount does not match.");
        if (!string.Equals(data.Currency, "VND", StringComparison.OrdinalIgnoreCase))
            throw PaymentOperationException.Conflict("payOS payment currency must be VND.");
        if (string.IsNullOrWhiteSpace(data.Reference) || data.Reference.Length > 74)
            throw PaymentOperationException.Validation("payOS transaction reference is invalid.");
    }

    private static DateTimeOffset ParsePayOsTransactionTime(string value, DateTimeOffset fallback)
    {
        if (DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var local))
            return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), VietnamOffset);
        return fallback;
    }

    private static string HashPayOsWebhook(PayOsWebhookPayload data)
    {
        var canonical = $"{data.OrderCode}|{data.Amount}|{data.PaymentLinkId}|{data.Reference}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static void ValidateAmount(decimal amount)
    {
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
            throw PaymentOperationException.Validation(
                "Payment amount must be positive and have at most two decimal places.");
    }
}
