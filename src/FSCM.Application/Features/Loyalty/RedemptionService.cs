namespace FSCM.Application.Features.Loyalty;

public sealed class RedemptionService(IRedemptionStore store, TimeProvider timeProvider)
{
    public async Task<RedemptionResult> CreateAsync(Guid userAccountId, CreateRedemptionData data,
        CancellationToken cancellationToken) => ToResult(await store.CreateAsync(
            userAccountId, data, timeProvider.GetUtcNow(), cancellationToken));

    public async Task<IReadOnlyCollection<RedemptionResult>> ListAsync(
        Guid userAccountId, CancellationToken cancellationToken) =>
        (await store.ListForUserAsync(userAccountId, cancellationToken)).Select(ToResult).ToArray();

    public async Task<IReadOnlyCollection<RedemptionResult>> ListForAdminAsync(
        CancellationToken cancellationToken) =>
        (await store.ListForAdminAsync(cancellationToken)).Select(ToResult).ToArray();

    private static RedemptionResult ToResult(RedemptionView view) => new(
        view.Redemption.Id, view.Redemption.RetailerId, view.Redemption.LoyaltyRewardId,
        view.Reward.Code, view.Reward.Name, view.Redemption.RedeemedPoints / view.Reward.RequiredPoints,
        view.Redemption.RedeemedPoints, view.Redemption.Status, view.BalanceAfter,
        view.Redemption.ClientRequestId, view.Redemption.CreatedAt, view.Redemption.UpdatedAt,
        view.RetailerCode, view.RetailerName);
}
