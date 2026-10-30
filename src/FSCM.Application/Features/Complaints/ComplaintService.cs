using FSCM.Application.Features.Orders;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Complaints;

public sealed class ComplaintService(
    IComplaintStore store, OrderCompletionService completion, TimeProvider timeProvider)
{
    public Task<ReceiptDiscrepancy> CreateAsync(Guid actor, CreateComplaintData data, CancellationToken ct) =>
        store.CreateAsync(actor, data, timeProvider.GetUtcNow(), ct);
    public Task<IReadOnlyCollection<ComplaintResult>> ListAsync(Guid actor, string role, CancellationToken ct) =>
        store.ListAsync(actor, role, ct);
    public async Task<ComplaintResult> ResolveAsync(
        Guid id, Guid actor, ResolveComplaintData data, CancellationToken ct)
    {
        var resolved = await store.ResolveAsync(id, actor, data, timeProvider.GetUtcNow(), ct);
        // An order held back only by this complaint can now earn its points, and nothing else is
        // going to ask again on its behalf.
        await completion.EvaluateAsync(resolved.SalesOrderId, actor, ct);
        return resolved;
    }
}
