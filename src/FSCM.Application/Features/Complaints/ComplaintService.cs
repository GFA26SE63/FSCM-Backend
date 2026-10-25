using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Complaints;

public sealed class ComplaintService(IComplaintStore store, TimeProvider timeProvider)
{
    public Task<ReceiptDiscrepancy> CreateAsync(Guid actor, CreateComplaintData data, CancellationToken ct) =>
        store.CreateAsync(actor, data, timeProvider.GetUtcNow(), ct);
    public Task<IReadOnlyCollection<ComplaintResult>> ListAsync(Guid actor, string role, CancellationToken ct) =>
        store.ListAsync(actor, role, ct);
    public Task<ComplaintResult> ResolveAsync(Guid id, Guid actor, ResolveComplaintData data, CancellationToken ct) =>
        store.ResolveAsync(id, actor, data, timeProvider.GetUtcNow(), ct);
}
