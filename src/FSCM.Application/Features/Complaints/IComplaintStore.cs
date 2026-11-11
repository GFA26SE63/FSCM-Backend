using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Complaints;

public interface IComplaintStore
{
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct) => operation(ct);
    Task<IReadOnlyCollection<ComplaintTarget>> ListTargetsAsync(Guid actor, Guid orderId, CancellationToken ct);
    Task<ReceiptDiscrepancy> CreateAsync(Guid actorUserId, CreateComplaintData data, DateTimeOffset now, CancellationToken ct);
    Task<IReadOnlyCollection<ComplaintResult>> ListAsync(Guid actorUserId, string role, CancellationToken ct);
    Task<ComplaintResult> ResolveAsync(Guid id, Guid actorUserId, ResolveComplaintData data, DateTimeOffset now, CancellationToken ct);
    Task MarkFinancialAdjustmentAppliedAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Loads the complaint header for an evidence operation, verifying the retailer owns it. Returns
    /// null when the complaint does not exist or does not belong to <paramref name="actorUserId"/>.
    /// </summary>
    Task<ReceiptDiscrepancy?> FindOwnedComplaintAsync(Guid complaintId, Guid actorUserId, CancellationToken ct);
    Task AddEvidenceAsync(ComplaintEvidence evidence, CancellationToken ct);
    Task<IReadOnlyCollection<ComplaintEvidence>> ListEvidenceAsync(Guid complaintId, CancellationToken ct);
    Task<ComplaintEvidence?> FindEvidenceAsync(Guid complaintId, Guid evidenceId, CancellationToken ct);
}
