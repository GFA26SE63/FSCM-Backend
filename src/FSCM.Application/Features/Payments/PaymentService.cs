using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FSCM.Application.Features.Orders;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Payments;

public sealed class PaymentService(IPaymentStore store, TimeProvider timeProvider)
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
        var requestHash = Hash(orderId, collectionMethod, NormalizeNote(data.Note));

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
            if (view.Payment.Status == PaymentStatuses.Paid)
                throw PaymentOperationException.Conflict("Payment was already collected in full.");
            if (view.Payment.Version != data.ExpectedVersion)
                throw PaymentOperationException.Conflict(
                    $"Payment version is {view.Payment.Version}; refresh and retry.");
            if (actorRole == SystemRoles.Sales && view.Order.CreatedByUserId != actorUserId)
                throw PaymentOperationException.Forbidden(
                    "Sales can only record cash collection for their own order.");

            var now = timeProvider.GetUtcNow();
            var oldValues = new
            {
                view.Payment.Status,
                view.Payment.PaidDate,
                view.Payment.CollectionMethod,
                view.Payment.ConfirmedByUserId,
                view.Payment.ConfirmedAt,
                view.Payment.Version
            };
            view.Payment.Status = PaymentStatuses.Paid;
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
            view.Payment.Amount, view.Order.CreditTermDays, view.Payment.DeliveredDate,
            view.Payment.DueDate, daysOverdue, view.Payment.PaidDate,
            view.Payment.CollectionMethod, view.Payment.ConfirmedByUserId,
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

    private static string Hash(Guid orderId, string method, string? note)
    {
        var canonical = $"{orderId:N}|{method}|{note}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
