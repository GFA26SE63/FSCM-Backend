using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Complaints;

public interface IComplaintStore
{
    Task<ReceiptDiscrepancy> CreateAsync(Guid actorUserId, CreateComplaintData data, DateTimeOffset now, CancellationToken ct);
    Task<IReadOnlyCollection<ComplaintResult>> ListAsync(Guid actorUserId, string role, CancellationToken ct);
    Task<ComplaintResult> ResolveAsync(Guid id, Guid actorUserId, ResolveComplaintData data, DateTimeOffset now, CancellationToken ct);
}
