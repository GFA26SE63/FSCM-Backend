namespace FSCM.Application.Features.Loyalty;

public sealed class RedemptionWorkflowService(IRedemptionWorkflowStore store, TimeProvider timeProvider)
{
    public Task<RedemptionResult> ApproveAsync(Guid id, Guid actor, CancellationToken ct) => Transition(id, "Approved", actor, ct);
    public Task<RedemptionResult> RejectAsync(Guid id, Guid actor, CancellationToken ct) => Transition(id, "Rejected", actor, ct);
    public Task<RedemptionResult> DeliverAsync(Guid id, Guid actor, CancellationToken ct) => Transition(id, "Delivered", actor, ct);
    public Task<RedemptionResult> CompleteAsync(Guid id, Guid actor, CancellationToken ct) => Transition(id, "Completed", actor, ct);

    private async Task<RedemptionResult> Transition(Guid id, string status, Guid actor, CancellationToken ct) =>
        ToResult(await store.TransitionAsync(id, status, actor, timeProvider.GetUtcNow(), ct));

    private static RedemptionResult ToResult(RedemptionView view) => new(
        view.Redemption.Id, view.Redemption.RetailerId, view.Redemption.LoyaltyRewardId,
        view.Reward.Code, view.Reward.Name, view.Redemption.RedeemedPoints / view.Reward.RequiredPoints,
        view.Redemption.RedeemedPoints, view.Redemption.Status, view.BalanceAfter,
        view.Redemption.ClientRequestId, view.Redemption.CreatedAt, view.Redemption.UpdatedAt);
}
