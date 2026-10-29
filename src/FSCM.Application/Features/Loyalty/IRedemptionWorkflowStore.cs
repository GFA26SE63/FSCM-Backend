namespace FSCM.Application.Features.Loyalty;

public interface IRedemptionWorkflowStore
{
    Task<RedemptionView> TransitionAsync(Guid redemptionId, string targetStatus,
        Guid actorUserId, DateTimeOffset now, CancellationToken cancellationToken);
}
