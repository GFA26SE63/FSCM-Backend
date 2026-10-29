using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FSCM.Application.Features.Payments;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Refunds;

public sealed class RefundService(IRefundStore store, IPaymentStore paymentStore, TimeProvider timeProvider)
{
    public async Task<RefundResult> CreateAsync(CreateRefundData data, string operationKey,
        Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        EnsureOperator(actorRole);
        operationKey = Key(operationKey);
        ValidateAmount(data.Amount);
        var recipient = Required(data.Recipient, 200, "Recipient");
        var reason = Required(data.Reason, 500, "Reason");
        var hash = Hash(data.SalesOrderId, data.Amount, recipient, reason);
        var prior = await store.FindByOperationKeyAsync(operationKey, cancellationToken);
        if (prior is not null) return Replay(prior, hash, prior.Refund.CreateRequestHash);

        Guid id = default;
        await paymentStore.ExecuteInTransactionAsync(async ct =>
        {
            var ledger = await paymentStore.FindByOrderAsync(data.SalesOrderId, true, ct)
                ?? throw RefundOperationException.NotFound("Payment ledger was not found.");
            var totals = await store.TotalsAsync(ledger.Payment.Id, ct);
            var refundDue = Math.Max(0, ledger.Payment.ConfirmedAmount - ledger.Payment.AdjustedAmount - totals.Refunded);
            if (data.Amount + totals.Pending > refundDue)
                throw RefundOperationException.Conflict($"Refund exceeds available refund obligation of {Math.Max(0, refundDue - totals.Pending):0.##} VND.");
            var now = timeProvider.GetUtcNow();
            var refund = new ManualRefund
            {
                Id = Guid.NewGuid(),
                PaymentId = ledger.Payment.Id,
                SalesOrderId = data.SalesOrderId,
                Amount = data.Amount,
                Recipient = recipient,
                Reason = reason,
                Status = RefundStatuses.PendingRefund,
                CreateOperationKey = operationKey,
                CreateRequestHash = hash,
                CreatedByUserId = actorUserId,
                CreatedAt = now,
                UpdatedAt = now
            };
            ledger.Payment.Version++;
            ledger.Payment.UpdatedAt = now;
            store.Add(refund);
            Audit(refund, actorUserId, "RefundRequested", now);
            await store.SaveChangesAsync(ct);
            id = refund.Id;
        }, cancellationToken);
        paymentStore.ClearTracking();
        return ToResult(await store.FindAsync(id, false, cancellationToken)
            ?? throw RefundOperationException.Conflict("Refund was committed but could not be reloaded."));
    }

    public async Task<RefundResult> SubmitEvidenceAsync(Guid id, SubmitRefundEvidenceData data,
        string operationKey, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        EnsureOperator(actorRole);
        operationKey = Key(operationKey);
        var transaction = Required(data.TransactionReference, 100, "Transaction reference");
        var evidence = Required(data.EvidenceReference, 500, "Evidence reference");
        if (data.TransferredAt > timeProvider.GetUtcNow())
            throw RefundOperationException.Validation("Transfer date cannot be in the future.");
        var hash = Hash(id, data.ExpectedVersion, data.TransferredAt, transaction, evidence);
        var prior = await store.FindByOperationKeyAsync(operationKey, cancellationToken);
        if (prior is not null) return Replay(prior, hash, prior.Refund.EvidenceRequestHash);
        await MutateAsync(id, data.ExpectedVersion, actorUserId, ct =>
        {
            if (ct.Status != RefundStatuses.PendingRefund)
                throw RefundOperationException.Conflict("Only a pending refund can receive transfer evidence.");
            ct.Status = RefundStatuses.AwaitingVerification;
            ct.TransferredAt = data.TransferredAt;
            ct.TransactionReference = transaction;
            ct.EvidenceReference = evidence;
            ct.SubmittedByUserId = actorUserId;
            ct.SubmittedAt = timeProvider.GetUtcNow();
            ct.EvidenceOperationKey = operationKey;
            ct.EvidenceRequestHash = hash;
        }, "RefundEvidenceSubmitted", cancellationToken);
        return await Reload(id, cancellationToken);
    }

    public async Task<RefundResult> VerifyAsync(Guid id, VerifyRefundData data, string operationKey,
        Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        if (actorRole != SystemRoles.Admin)
            throw RefundOperationException.Forbidden("Only Admin can verify a completed refund transfer.");
        operationKey = Key(operationKey);
        var note = Optional(data.Note, 500);
        var hash = Hash(id, data.ExpectedVersion, note);
        var prior = await store.FindByOperationKeyAsync(operationKey, cancellationToken);
        if (prior is not null) return Replay(prior, hash, prior.Refund.VerificationRequestHash);
        await MutateAsync(id, data.ExpectedVersion, actorUserId, refund =>
        {
            if (refund.Status != RefundStatuses.AwaitingVerification)
                throw RefundOperationException.Conflict("Only a refund awaiting verification can be verified.");
            if (refund.SubmittedByUserId == actorUserId)
                throw RefundOperationException.Forbidden("The evidence submitter cannot verify the same refund.");
            refund.Status = RefundStatuses.Refunded;
            refund.VerifiedByUserId = actorUserId;
            refund.VerifiedAt = timeProvider.GetUtcNow();
            refund.VerificationNote = note;
            refund.VerificationOperationKey = operationKey;
            refund.VerificationRequestHash = hash;
        }, "RefundVerified", cancellationToken);
        return await Reload(id, cancellationToken);
    }

    public async Task<RefundResult> GetAsync(Guid id, Guid actorUserId, string actorRole, CancellationToken ct)
    {
        EnsureRead(actorRole);
        var view = await store.FindAsync(id, false, ct) ?? throw RefundOperationException.NotFound("Refund was not found.");
        if (!await paymentStore.CanViewAsync(new(view.Payment, view.Order, view.Retailer), actorUserId, actorRole, ct))
            throw RefundOperationException.Forbidden("You cannot view this refund.");
        return ToResult(view);
    }

    public async Task<IReadOnlyCollection<RefundResult>> ListAsync(Guid? orderId, string? status,
        Guid actorUserId, string actorRole, CancellationToken ct)
    {
        EnsureRead(actorRole);
        if (status is not null && !RefundStatuses.All.Contains(status))
            throw RefundOperationException.Validation("Refund status filter is invalid.");
        return (await store.ListAsync(orderId, status, actorUserId, actorRole, ct)).Select(ToResult).ToArray();
    }

    private async Task MutateAsync(Guid id, int version, Guid actor, Action<ManualRefund> change, string action, CancellationToken ct)
    {
        await paymentStore.ExecuteInTransactionAsync(async tx =>
        {
            var view = await store.FindAsync(id, true, tx) ?? throw RefundOperationException.NotFound("Refund was not found.");
            if (view.Refund.Version != version)
                throw RefundOperationException.Conflict($"Refund version is {view.Refund.Version}; refresh and retry.");
            change(view.Refund);
            var now = timeProvider.GetUtcNow();
            view.Refund.Version++;
            view.Refund.UpdatedAt = now;
            Audit(view.Refund, actor, action, now);
            await store.SaveChangesAsync(tx);
        }, ct);
        paymentStore.ClearTracking();
    }

    private async Task<RefundResult> Reload(Guid id, CancellationToken ct) => ToResult(
        await store.FindAsync(id, false, ct) ?? throw RefundOperationException.Conflict("Refund could not be reloaded."));

    private void Audit(ManualRefund refund, Guid actor, string action, DateTimeOffset now) => store.AddAuditLog(new AuditLog
    {
        Id = Guid.NewGuid(),
        UserAccountId = actor,
        EntityName = nameof(ManualRefund),
        EntityId = refund.Id,
        Action = action,
        NewValues = JsonSerializer.Serialize(new
        {
            refund.Amount,
            refund.Status,
            refund.Recipient,
            refund.TransactionReference,
            refund.EvidenceReference,
            refund.SubmittedByUserId,
            refund.VerifiedByUserId,
            refund.Version
        }),
        Reason = refund.Reason,
        CreatedAt = now
    });

    private static RefundResult Replay(RefundView view, string hash, string? stored)
    {
        if (stored != hash) throw RefundOperationException.Conflict("Idempotency key was used for a different refund request.");
        return ToResult(view);
    }

    private static RefundResult ToResult(RefundView v)
    {
        var due = Math.Max(0, v.Payment.ConfirmedAmount - v.Payment.AdjustedAmount - v.RefundedAmount);
        return new(v.Refund.Id, v.Payment.Id, v.Order.Id, v.Order.OrderCode, v.Refund.Amount,
            v.Refund.Recipient, v.Refund.Reason, v.Refund.Status, v.Refund.TransferredAt,
            v.Refund.TransactionReference, v.Refund.EvidenceReference, v.Refund.CreatedByUserId,
            v.Refund.SubmittedByUserId, v.Refund.SubmittedAt, v.Refund.VerifiedByUserId,
            v.Refund.VerifiedAt, v.Refund.VerificationNote, v.Refund.Version, v.Payment.Amount,
            v.Payment.AdjustedAmount, v.Payment.ConfirmedAmount, v.PendingRefundAmount,
            v.RefundedAmount, due, v.Refund.CreatedAt, v.Refund.UpdatedAt);
    }

    private static void EnsureOperator(string role) { if (role != SystemRoles.Operator) throw RefundOperationException.Forbidden("Only Operator can create refunds and submit transfer evidence."); }
    private static void EnsureRead(string role) { if (role is not (SystemRoles.Admin or SystemRoles.Operator or SystemRoles.SalesManager or SystemRoles.Sales or SystemRoles.Retailer)) throw RefundOperationException.Forbidden("Role cannot view refunds."); }
    private static void ValidateAmount(decimal amount) { if (amount <= 0 || decimal.Round(amount, 2) != amount) throw RefundOperationException.Validation("Refund amount must be positive with at most two decimal places."); }
    private static string Key(string value) => Required(value, 80, "Idempotency-Key");
    private static string Required(string? value, int max, string name) { value = value?.Trim(); if (string.IsNullOrWhiteSpace(value) || value.Length > max) throw RefundOperationException.Validation($"{name} is required and cannot exceed {max} characters."); return value; }
    private static string? Optional(string? value, int max) { value = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); if (value?.Length > max) throw RefundOperationException.Validation($"Value cannot exceed {max} characters."); return value; }
    private static string Hash(params object?[] values) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', values.Select(x => x switch { DateTimeOffset d => d.ToUniversalTime().ToString("O"), decimal n => n.ToString("0.00"), Guid g => g.ToString("N"), _ => x?.ToString() }))))).ToLowerInvariant();
}
