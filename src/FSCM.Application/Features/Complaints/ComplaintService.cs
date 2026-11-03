using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Payments;
using FSCM.Application.Features.Loyalty;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Complaints;

public sealed class ComplaintService(
    IComplaintStore store,
    OrderCompletionService completion,
    TimeProvider timeProvider,
    PaymentService? payments = null,
    LoyaltyAccrualService? loyalty = null)
{
    public Task<ReceiptDiscrepancy> CreateAsync(Guid actor, CreateComplaintData data, CancellationToken ct) =>
        store.CreateAsync(actor, data, timeProvider.GetUtcNow(), ct);
    public Task<IReadOnlyCollection<ComplaintResult>> ListAsync(Guid actor, string role, CancellationToken ct) =>
        store.ListAsync(actor, role, ct);
    public async Task<ComplaintResult> ResolveAsync(
        Guid id, Guid actor, ResolveComplaintData data, CancellationToken ct)
    {
        var resolved = await store.ResolveAsync(id, actor, data, timeProvider.GetUtcNow(), ct);
        if (data.Approved && !resolved.FinancialAdjustmentApplied &&
            resolved.AdjustmentAmount > 0 && payments is not null)
        {
            var payment = await payments.GetByOrderAsync(
                resolved.SalesOrderId, actor, SystemRoles.Operator, ct);
            var reason = $"Khiếu nại {resolved.Code} được chấp nhận: {data.Result.Trim()}";
            var adjusted = await payments.AdjustReceivableAsync(
                resolved.SalesOrderId,
                new AdjustReceivableData(
                    resolved.AdjustmentAmount,
                    reason,
                    payment.Version),
                $"complaint-adjust:{resolved.Id:N}",
                actor,
                SystemRoles.Operator,
                ct);
            if (loyalty is not null && adjusted.AdjustedAmount != payment.AdjustedAmount)
            {
                await loyalty.AdjustAsync(
                    resolved.SalesOrderId, adjusted.AdjustedAmount, reason, actor, ct);
            }
            await store.MarkFinancialAdjustmentAppliedAsync(resolved.Id, ct);
        }
        // An order held back only by this complaint can now earn its points, and nothing else is
        // going to ask again on its behalf.
        await completion.EvaluateAsync(resolved.SalesOrderId, actor, ct);
        return resolved;
    }
}
