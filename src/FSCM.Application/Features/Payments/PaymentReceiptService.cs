using System.Text.Json;
using FSCM.Application.Features.Orders;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Payments;

public sealed record ReceiptAllocationData(Guid PaymentId, decimal Amount);
public sealed record ConfirmPaymentReceiptData(Guid RetailerId, decimal ReceivedAmount, string? Reference, string? Note, IReadOnlyCollection<ReceiptAllocationData> Allocations);
public sealed record PaymentReceiptResult(Guid Id, string ReceiptCode, PaymentReceiptStatus Status, decimal ReceivedAmount, decimal UnappliedCreditAmount, DateTimeOffset? ConfirmedAt, DateTimeOffset? ReversedAt, IReadOnlyCollection<ReceiptAllocationData> Allocations);
public sealed record ReceiptPaymentView(Payment Payment, SalesOrder Order);

// F64 / F5 step 4: auto-allocation proposal when the retailer does not name invoices.
public sealed record ProposeReceiptAllocationData(Guid RetailerId, decimal ReceivedAmount);

/// <summary>One outstanding receivable with the fields the proposal orders by.</summary>
public sealed record OutstandingReceivableView(
    Guid PaymentId, Guid SalesOrderId, string OrderCode,
    decimal OutstandingAmount, DateOnly? DueDate, DateTimeOffset CreatedAt, bool IsOverdue);

/// <summary>One proposed receipt line: how much of the receipt to apply to this receivable.</summary>
public sealed record ProposedReceiptAllocation(
    Guid PaymentId, Guid SalesOrderId, string OrderCode,
    decimal OutstandingAmount, decimal ProposedAmount, DateOnly? DueDate, bool IsOverdue);

public sealed record ReceiptAllocationProposalResult(
    Guid RetailerId, decimal ReceivedAmount, decimal AllocatedAmount, decimal UnappliedCreditAmount,
    IReadOnlyCollection<ProposedReceiptAllocation> Allocations);

public interface IPaymentReceiptStore
{
    Task<PaymentReceipt?> FindReceiptByOperationKeyAsync(string operationKey, bool forUpdate, CancellationToken cancellationToken);
    Task<PaymentReceipt?> FindReceiptAsync(Guid receiptId, bool forUpdate, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<PaymentReceiptAllocation>> ListAllocationsAsync(Guid receiptId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ReceiptPaymentView>> FindPaymentsForUpdateAsync(IReadOnlyCollection<Guid> paymentIds, CancellationToken cancellationToken);
    Task<decimal> ConfirmedAllocationTotalAsync(Guid paymentId, Guid? excludedReceiptId, CancellationToken cancellationToken);

    /// <summary>
    /// F5 step 4: a retailer's receivables that still owe money, for the auto-allocation proposal.
    /// Outstanding = AdjustedAmount − already-confirmed collections; only rows with a positive
    /// balance are returned.
    /// </summary>
    Task<IReadOnlyCollection<OutstandingReceivableView>> ListOutstandingReceivablesByRetailerAsync(
        Guid retailerId, DateOnly today, CancellationToken cancellationToken);
    void AddReceipt(PaymentReceipt receipt);
    void AddAllocations(IEnumerable<PaymentReceiptAllocation> allocations);
    void AddAuditLog(AuditLog auditLog);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
    void ClearTracking();
}

/// <summary>Applies one verified receipt to selected receivables atomically and derives their status.</summary>
public sealed class PaymentReceiptService(
    IPaymentReceiptStore store,
    TimeProvider timeProvider,
    OrderCompletionService? completionService = null)
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public async Task<PaymentReceiptResult> ConfirmAsync(ConfirmPaymentReceiptData data, string operationKey,
        Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        if (actorRole is not (SystemRoles.Admin or SystemRoles.Operator))
            throw PaymentOperationException.Forbidden("Only Admin or Operator can confirm a receipt.");
        operationKey = NormalizeKey(operationKey);
        Validate(data);
        var replay = await store.FindReceiptByOperationKeyAsync(operationKey, false, cancellationToken);
        if (replay is not null)
        {
            var replayResult = await ToResultAsync(replay, cancellationToken);
            if (replay.RetailerId != data.RetailerId || replay.ReceivedAmount != data.ReceivedAmount ||
                !replayResult.Allocations.OrderBy(x => x.PaymentId).SequenceEqual(data.Allocations.OrderBy(x => x.PaymentId)))
                throw PaymentOperationException.Conflict("Idempotency key was already used for a different receipt.");
            return replayResult;
        }

        Guid[] affectedOrderIds = [];
        await store.ExecuteInTransactionAsync(async token =>
        {
            if (await store.FindReceiptByOperationKeyAsync(operationKey, true, token) is not null) return;
            var ids = data.Allocations.Select(item => item.PaymentId).ToArray();
            var payments = await store.FindPaymentsForUpdateAsync(ids, token);
            if (payments.Count != ids.Length || payments.Any(item => item.Order.RetailerId != data.RetailerId))
                throw PaymentOperationException.Validation("Every selected payment must belong to the receipt retailer.");
            affectedOrderIds = payments.Select(item => item.Order.Id).Distinct().ToArray();
            foreach (var allocation in data.Allocations)
            {
                var payment = payments.Single(item => item.Payment.Id == allocation.PaymentId).Payment;
                var existing = await store.ConfirmedAllocationTotalAsync(payment.Id, null, token);
                if (allocation.Amount > payment.AdjustedAmount - existing)
                    throw PaymentOperationException.Conflict("Receipt allocation exceeds the outstanding balance of a selected order.");
            }
            var now = timeProvider.GetUtcNow();
            var allocated = data.Allocations.Sum(item => item.Amount);
            var receipt = new PaymentReceipt
            {
                Id = Guid.NewGuid(), ReceiptCode = $"PT-{now:yyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                RetailerId = data.RetailerId, ReceivedAmount = data.ReceivedAmount,
                UnappliedCreditAmount = data.ReceivedAmount - allocated, Status = PaymentReceiptStatus.Confirmed,
                Reference = Trim(data.Reference), Note = Trim(data.Note), ConfirmedByUserId = actorUserId,
                ConfirmedAt = now, OperationKey = operationKey, Version = 1, CreatedAt = now, UpdatedAt = now
            };
            store.AddReceipt(receipt);
            store.AddAllocations(data.Allocations.Select(item => new PaymentReceiptAllocation
            { Id = Guid.NewGuid(), PaymentReceiptId = receipt.Id, PaymentId = item.PaymentId, Amount = item.Amount, CreatedAt = now }));
            foreach (var view in payments)
            {
                var total = await store.ConfirmedAllocationTotalAsync(view.Payment.Id, null, token) + data.Allocations.Where(x => x.PaymentId == view.Payment.Id).Sum(x => x.Amount);
                ApplyDerivedStatus(view.Payment, total, now);
            }
            store.AddAuditLog(new AuditLog { Id = Guid.NewGuid(), UserAccountId = actorUserId, EntityName = nameof(PaymentReceipt), EntityId = receipt.Id, Action = "Confirmed", NewValues = JsonSerializer.Serialize(new { receipt.ReceivedAmount, receipt.UnappliedCreditAmount }), Reason = receipt.Note, CreatedAt = now });
            await store.SaveChangesAsync(token);
        }, cancellationToken);
        store.ClearTracking();
        if (completionService is not null)
        {
            foreach (var orderId in affectedOrderIds)
                await completionService.EvaluateAsync(orderId, actorUserId, cancellationToken);
        }
        return await ToResultAsync((await store.FindReceiptByOperationKeyAsync(operationKey, false, cancellationToken))!, cancellationToken);
    }

    /// <summary>
    /// F5 step 4: proposes how to spread a received amount across a retailer's outstanding
    /// receivables when they do not name invoices — overdue first, then earliest due date, then
    /// earliest created, then order code. Read-only: the user still confirms via ConfirmAsync. Any
    /// surplus beyond the total outstanding is reported as unapplied credit.
    /// </summary>
    public async Task<ReceiptAllocationProposalResult> ProposeAllocationAsync(
        ProposeReceiptAllocationData data, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        if (actorRole is not (SystemRoles.Admin or SystemRoles.Operator))
            throw PaymentOperationException.Forbidden("Only Admin or Operator can propose a receipt allocation.");
        if (data.RetailerId == Guid.Empty)
            throw PaymentOperationException.Validation("Retailer is required.");
        if (data.ReceivedAmount <= 0 || decimal.Round(data.ReceivedAmount, 2) != data.ReceivedAmount)
            throw PaymentOperationException.Validation(
                "Received amount must be positive with at most two decimal places.");

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().ToOffset(VietnamOffset).DateTime);
        var outstanding = await store.ListOutstandingReceivablesByRetailerAsync(
            data.RetailerId, today, cancellationToken);

        var ordered = outstanding
            .OrderByDescending(item => item.IsOverdue)
            .ThenBy(item => item.DueDate ?? DateOnly.MaxValue)
            .ThenBy(item => item.CreatedAt)
            .ThenBy(item => item.OrderCode, StringComparer.Ordinal);

        var proposals = new List<ProposedReceiptAllocation>();
        var remaining = data.ReceivedAmount;
        foreach (var receivable in ordered)
        {
            if (remaining <= 0) break;
            var apply = Math.Min(remaining, receivable.OutstandingAmount);
            if (apply <= 0) continue;
            proposals.Add(new ProposedReceiptAllocation(
                receivable.PaymentId, receivable.SalesOrderId, receivable.OrderCode,
                receivable.OutstandingAmount, apply, receivable.DueDate, receivable.IsOverdue));
            remaining -= apply;
        }

        var allocated = proposals.Sum(item => item.ProposedAmount);
        return new ReceiptAllocationProposalResult(
            data.RetailerId, data.ReceivedAmount, allocated, data.ReceivedAmount - allocated, proposals);
    }

    public async Task<PaymentReceiptResult> ReverseAsync(Guid receiptId, string reason, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        if (actorRole is not (SystemRoles.Admin or SystemRoles.Operator)) throw PaymentOperationException.Forbidden("Only Admin or Operator can reverse a receipt.");
        if (string.IsNullOrWhiteSpace(reason)) throw PaymentOperationException.Validation("A reversal reason is required.");
        await store.ExecuteInTransactionAsync(async token =>
        {
            var receipt = await store.FindReceiptAsync(receiptId, true, token) ?? throw PaymentOperationException.NotFound("Payment receipt was not found.");
            if (receipt.Status == PaymentReceiptStatus.Reversed) return;
            var allocations = await store.ListAllocationsAsync(receiptId, token);
            var payments = await store.FindPaymentsForUpdateAsync(allocations.Select(x => x.PaymentId).Distinct().ToArray(), token);
            var now = timeProvider.GetUtcNow(); receipt.Status = PaymentReceiptStatus.Reversed; receipt.ReversedByUserId = actorUserId; receipt.ReversedAt = now; receipt.ReversalReason = reason.Trim(); receipt.Version++; receipt.UpdatedAt = now;
            foreach (var view in payments)
                ApplyDerivedStatus(view.Payment,
                    await store.ConfirmedAllocationTotalAsync(view.Payment.Id, receipt.Id, token), now);
            store.AddAuditLog(new AuditLog { Id = Guid.NewGuid(), UserAccountId = actorUserId, EntityName = nameof(PaymentReceipt), EntityId = receipt.Id, Action = "Reversed", Reason = receipt.ReversalReason, CreatedAt = now });
            await store.SaveChangesAsync(token);
        }, cancellationToken);
        store.ClearTracking(); return await ToResultAsync((await store.FindReceiptAsync(receiptId, false, cancellationToken))!, cancellationToken);
    }

    private async Task<PaymentReceiptResult> ToResultAsync(PaymentReceipt receipt, CancellationToken token) => new(receipt.Id, receipt.ReceiptCode, receipt.Status, receipt.ReceivedAmount, receipt.UnappliedCreditAmount, receipt.ConfirmedAt, receipt.ReversedAt, (await store.ListAllocationsAsync(receipt.Id, token)).Select(x => new ReceiptAllocationData(x.PaymentId, x.Amount)).ToArray());
    private static void ApplyDerivedStatus(Payment payment, decimal paid, DateTimeOffset now) { payment.ConfirmedAmount = paid; payment.Status = paid >= payment.AdjustedAmount ? PaymentStatuses.Paid : paid > 0 ? PaymentStatuses.PartiallyPaid : payment.DueDate < DateOnly.FromDateTime(now.UtcDateTime) ? PaymentStatuses.Overdue : PaymentStatuses.Unpaid; payment.PaidDate = payment.Status == PaymentStatuses.Paid ? DateOnly.FromDateTime(now.UtcDateTime) : null; payment.Version++; payment.UpdatedAt = now; }
    private static void Validate(ConfirmPaymentReceiptData data) { if (data.RetailerId == Guid.Empty || data.ReceivedAmount <= 0 || data.Allocations.Count == 0 || data.Allocations.GroupBy(x => x.PaymentId).Any(x => x.Count() > 1) || data.Allocations.Any(x => x.PaymentId == Guid.Empty || x.Amount <= 0) || data.Allocations.Sum(x => x.Amount) > data.ReceivedAmount) throw PaymentOperationException.Validation("Receipt amount and allocations are invalid."); }
    private static string NormalizeKey(string key) => string.IsNullOrWhiteSpace(key) || key.Trim().Length > 80 ? throw PaymentOperationException.Validation("Idempotency-Key is required and must not exceed 80 characters.") : key.Trim();
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
