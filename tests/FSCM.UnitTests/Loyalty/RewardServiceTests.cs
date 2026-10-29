using FSCM.Application.Features.Loyalty;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Loyalty;

public sealed class RewardServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_reward_starts_inactive_and_generates_code()
    {
        var store = new FakeStore();
        var result = await new RewardService(store, new FixedTimeProvider(Now)).CreateAsync(
            new(store.Program.Id, "  Thùng quà  ", " Mô tả ", "https://img", "Gift", 100, null, null, 5), CancellationToken.None);

        Assert.Equal("QUA-001", result.Code);
        Assert.Equal("Thùng quà", result.Name);
        Assert.False(result.IsActive);
    }

    [Fact]
    public async Task Out_of_stock_reward_cannot_be_activated()
    {
        var store = new FakeStore();
        var reward = new LoyaltyReward { Id = Guid.NewGuid(), LoyaltyProgramId = store.Program.Id,
            Code = "QUA-001", Name = "Hết quà", RewardType = "Gift", RequiredPoints = 10,
            AvailableQuantity = 0, CreatedAt = Now, UpdatedAt = Now };
        store.Rewards.Add(reward);

        var error = await Assert.ThrowsAsync<LoyaltyOperationException>(() =>
            new RewardService(store, new FixedTimeProvider(Now)).ActivateAsync(reward.Id, CancellationToken.None));

        Assert.Equal("conflict", error.Code);
    }

    private sealed class FakeStore : IRewardStore
    {
        public LoyaltyProgram Program { get; } = new() { Id = Guid.NewGuid(), Name = "LP", Code = "LP-001" };
        public List<LoyaltyReward> Rewards { get; } = [];
        public Task<IReadOnlyCollection<LoyaltyReward>> ListAsync(Guid? programId, bool includeInactive, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<LoyaltyReward>>(Rewards.Where(x => (!programId.HasValue || x.LoyaltyProgramId == programId) && (includeInactive || x.IsActive)).ToArray());
        public Task<LoyaltyReward?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Rewards.SingleOrDefault(x => x.Id == id));
        public Task<LoyaltyProgram?> FindProgramAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<LoyaltyProgram?>(id == Program.Id ? Program : null);
        public Task<string> NextCodeAsync(CancellationToken cancellationToken) => Task.FromResult($"QUA-{Rewards.Count + 1:000}");
        public void Add(LoyaltyReward reward) => Rewards.Add(reward);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
