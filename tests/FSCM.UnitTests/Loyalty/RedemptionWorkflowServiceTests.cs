using FSCM.Application.Features.Loyalty;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Loyalty;

public sealed class RedemptionWorkflowServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 8, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Approve", "Approved")]
    [InlineData("Reject", "Rejected")]
    [InlineData("Deliver", "Delivered")]
    [InlineData("Complete", "Completed")]
    public async Task Workflow_commands_forward_the_exact_actor_status_and_timestamp(
        string command,
        string expectedStatus)
    {
        var store = new RecordingStore();
        var service = new RedemptionWorkflowService(store, new FixedTimeProvider(Now));
        var actor = Guid.NewGuid();

        var result = command switch
        {
            "Approve" => await service.ApproveAsync(store.Redemption.Id, actor, default),
            "Reject" => await service.RejectAsync(store.Redemption.Id, actor, default),
            "Deliver" => await service.DeliverAsync(store.Redemption.Id, actor, default),
            "Complete" => await service.CompleteAsync(store.Redemption.Id, actor, default),
            _ => throw new InvalidOperationException(command),
        };

        Assert.Equal(store.Redemption.Id, store.LastRedemptionId);
        Assert.Equal(expectedStatus, store.LastTargetStatus);
        Assert.Equal(actor, store.LastActorId);
        Assert.Equal(Now, store.LastTimestamp);
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(store.Redemption.ClientRequestId, result.ClientRequestId);
        Assert.Equal(store.Reward.Id, result.RewardId);
        Assert.Equal(store.BalanceAfter, result.BalanceAfter);
    }

    [Fact]
    public async Task Approved_delivered_completed_sequence_preserves_identity_and_finalizes_once()
    {
        var store = new StatefulStore();
        var service = new RedemptionWorkflowService(store, new FixedTimeProvider(Now));
        var actor = Guid.NewGuid();

        var approved = await service.ApproveAsync(store.Redemption.Id, actor, default);
        var delivered = await service.DeliverAsync(store.Redemption.Id, actor, default);
        var completed = await service.CompleteAsync(store.Redemption.Id, actor, default);

        Assert.Equal(new[] { "Approved", "Delivered", "Completed" }, store.Transitions);
        Assert.All(new[] { approved, delivered, completed }, result =>
        {
            Assert.Equal(store.Redemption.Id, result.Id);
            Assert.Equal(store.Redemption.RetailerId, result.RetailerId);
            Assert.Equal(store.Redemption.LoyaltyRewardId, result.RewardId);
            Assert.Equal(store.Redemption.RedeemedPoints, result.RedeemedPoints);
        });
        Assert.Equal("Completed", completed.Status);

        var replay = await Assert.ThrowsAsync<LoyaltyOperationException>(() =>
            service.CompleteAsync(store.Redemption.Id, actor, default));
        Assert.Equal("conflict", replay.Code);
        Assert.Equal(new[] { "Approved", "Delivered", "Completed" }, store.Transitions);
    }

    private sealed class RecordingStore : IRedemptionWorkflowStore
    {
        public RewardRedemption Redemption { get; } = NewRedemption();
        public LoyaltyReward Reward { get; }
        public int BalanceAfter => 275;
        public Guid LastRedemptionId { get; private set; }
        public string LastTargetStatus { get; private set; } = string.Empty;
        public Guid LastActorId { get; private set; }
        public DateTimeOffset LastTimestamp { get; private set; }

        public RecordingStore()
        {
            Reward = NewReward(Redemption.LoyaltyRewardId);
        }

        public Task<RedemptionView> TransitionAsync(
            Guid redemptionId,
            string targetStatus,
            Guid actorUserId,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            LastRedemptionId = redemptionId;
            LastTargetStatus = targetStatus;
            LastActorId = actorUserId;
            LastTimestamp = now;
            Redemption.Status = targetStatus;
            Redemption.UpdatedAt = now;
            return Task.FromResult(View(Redemption, Reward, BalanceAfter));
        }
    }

    private sealed class StatefulStore : IRedemptionWorkflowStore
    {
        public RewardRedemption Redemption { get; } = NewRedemption();
        public LoyaltyReward Reward { get; }
        public List<string> Transitions { get; } = [];

        public StatefulStore()
        {
            Reward = NewReward(Redemption.LoyaltyRewardId);
        }

        public Task<RedemptionView> TransitionAsync(
            Guid redemptionId,
            string targetStatus,
            Guid actorUserId,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            Assert.Equal(Redemption.Id, redemptionId);
            var expected = Redemption.Status switch
            {
                "Pending" => "Approved",
                "Approved" => "Delivered",
                "Delivered" => "Completed",
                _ => string.Empty,
            };
            if (targetStatus != expected)
            {
                throw new LoyaltyOperationException("conflict", "Invalid or repeated redemption transition.");
            }

            Redemption.Status = targetStatus;
            Redemption.UpdatedAt = now;
            if (targetStatus == "Approved")
            {
                Redemption.ApprovedByUserId = actorUserId;
                Redemption.ApprovedAt = now;
            }
            else if (targetStatus == "Delivered")
            {
                Redemption.DeliveredByUserId = actorUserId;
                Redemption.DeliveredAt = now;
            }
            else if (targetStatus == "Completed")
            {
                Redemption.FulfilledAt = now;
            }

            Transitions.Add(targetStatus);
            return Task.FromResult(View(Redemption, Reward, 275));
        }
    }

    private static RewardRedemption NewRedemption() => new()
    {
        Id = Guid.NewGuid(),
        RetailerId = Guid.NewGuid(),
        LoyaltyRewardId = Guid.NewGuid(),
        ClientRequestId = "report5-redemption-001",
        RedeemedPoints = 300,
        Quantity = 2,
        RequiredPointsSnapshot = 150,
        Status = "Pending",
        RedeemedByUserId = Guid.NewGuid(),
        RedeemedAt = Now,
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    private static LoyaltyReward NewReward(Guid id) => new()
    {
        Id = id,
        LoyaltyProgramId = Guid.NewGuid(),
        Code = "GIFT-001",
        Name = "Quà kiểm thử",
        RewardType = "Gift",
        RequiredPoints = 150,
        AvailableQuantity = 10,
        IsActive = true,
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    private static RedemptionView View(
        RewardRedemption redemption,
        LoyaltyReward reward,
        int balanceAfter) => new(
            redemption,
            reward,
            balanceAfter,
            "RET-0001",
            "Nhà bán lẻ kiểm thử",
            redemption.RedeemedPoints,
            Guid.NewGuid(),
            [Guid.NewGuid()]);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
