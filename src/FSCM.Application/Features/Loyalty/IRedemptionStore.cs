namespace FSCM.Application.Features.Loyalty;

public interface IRedemptionStore
{
    Task<RedemptionView> CreateAsync(Guid userAccountId, CreateRedemptionData data,
        DateTimeOffset now, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<RedemptionView>> ListForUserAsync(Guid userAccountId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<RedemptionView>> ListForAdminAsync(CancellationToken cancellationToken);
}
