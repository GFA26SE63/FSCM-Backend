namespace FSCM.Application.Features.Loyalty;

public interface ILoyaltyCardStore
{
    Task<LoyaltyCardData> GetAsync(Guid actorUserId, string actorRole,
        Guid? requestedRetailerId, CancellationToken cancellationToken);
    Task<Guid> FindRetailerIdByCodeAsync(string retailerCode, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<LoyaltyCardData>> ListAsync(Guid actorUserId, string actorRole, CancellationToken cancellationToken);
}
