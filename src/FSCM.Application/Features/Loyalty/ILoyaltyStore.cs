using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Loyalty;

public interface ILoyaltyStore
{
    Task<IReadOnlyCollection<LoyaltyProgram>> ListProgramsAsync(
        bool includeInactive, CancellationToken cancellationToken);
    Task<LoyaltyProgram?> FindProgramAsync(Guid id, CancellationToken cancellationToken);
    Task<string> NextProgramCodeAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<Guid>> ListProgramRetailerIdsAsync(Guid programId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<Guid>> FindExistingRetailerIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
    void AddProgram(LoyaltyProgram program);
    void AddProgramRetailers(IEnumerable<LoyaltyProgramRetailer> links);
    void RemoveProgramRetailers(IEnumerable<Guid> retailerIds, Guid programId);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
