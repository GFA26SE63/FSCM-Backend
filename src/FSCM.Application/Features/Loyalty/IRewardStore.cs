using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Loyalty;

public interface IRewardStore
{
    Task<IReadOnlyCollection<LoyaltyReward>> ListAsync(Guid? programId, bool includeInactive, CancellationToken cancellationToken);
    Task<LoyaltyReward?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<LoyaltyProgram?> FindProgramAsync(Guid id, CancellationToken cancellationToken);
    Task<string> NextCodeAsync(CancellationToken cancellationToken);
    void Add(LoyaltyReward reward);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
