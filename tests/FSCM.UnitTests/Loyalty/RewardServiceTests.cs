using FSCM.Application.Features.Loyalty;
using FSCM.Domain.Entities;
using FSCM.Application.Features.Storage;
using FSCM.UnitTests.Storage;

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

    [Fact]
    public async Task Upload_stores_stable_reference_and_preserves_reward_business_fields()
    {
        var store = new FakeStore();
        var storage = new FakeImageStorage();
        var service = new RewardService(store, new FixedTimeProvider(Now), storage.ToUploadService());
        var created = await service.CreateAsync(new(store.Program.Id, "Test gift", "Description",
            "https://old.example/image.png", "Gift", 100, null, null, 5), CancellationToken.None);
        var upload = FakeImageFactory.Png();
        using var content = upload.Content;

        var result = await service.SetImageAsync(created.Id, upload, CancellationToken.None);
        var stored = Assert.Single(store.Rewards);
        Assert.StartsWith("reward/", stored.ImageUrl!);
        Assert.Equal(stored.ImageUrl, result.ImageReference);
        Assert.Equal(storage.GetReadUrl(ImageCategory.LoyaltyRewardImage, stored.ImageUrl!), result.ImageUrl);
        Assert.DoesNotContain("?sig=", stored.ImageUrl!);
        Assert.Equal(100, result.RequiredPoints);
        Assert.Equal(5, result.AvailableQuantity);
        Assert.False(result.IsActive);
        Assert.Equal("Description", result.Description);
        Assert.Empty(storage.Deleted); // External legacy URLs are never deleted from blob storage.
        Assert.Equal(result.ImageUrl, (await service.GetAsync(created.Id, CancellationToken.None)).ImageUrl);
    }

    [Fact]
    public async Task Replace_deletes_old_blob_only_after_successful_save_and_rolls_back_failed_save()
    {
        var store = new FakeStore();
        var storage = new FakeImageStorage();
        var service = new RewardService(store, new FixedTimeProvider(Now), storage.ToUploadService());
        var reward = await service.CreateAsync(new(store.Program.Id, "Test gift", null, null,
            "Gift", 100, null, null, null), CancellationToken.None);
        var first = FakeImageFactory.Png();
        using var firstContent = first.Content;
        var original = await service.SetImageAsync(reward.Id, first, CancellationToken.None);

        store.FailSave = true;
        var failed = FakeImageFactory.Png();
        using var failedContent = failed.Content;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetImageAsync(reward.Id, failed, CancellationToken.None));
        Assert.Equal(original.ImageReference, Assert.Single(store.Rewards).ImageUrl);
        Assert.True(storage.Blobs.ContainsKey(original.ImageReference!));
        Assert.DoesNotContain(original.ImageReference!, storage.Deleted);
        Assert.Single(storage.Blobs);

        store.FailSave = false;
        var next = FakeImageFactory.Jpeg();
        using var nextContent = next.Content;
        var replacement = await service.SetImageAsync(reward.Id, next, CancellationToken.None);
        Assert.Contains(original.ImageReference!, storage.Deleted);
        Assert.NotEqual(original.ImageReference, replacement.ImageReference);
        Assert.Single(storage.Blobs);
    }

    [Fact]
    public async Task Upload_rejects_invalid_content_without_changing_existing_image()
    {
        var store = new FakeStore();
        var storage = new FakeImageStorage();
        var service = new RewardService(store, new FixedTimeProvider(Now), storage.ToUploadService());
        var reward = await service.CreateAsync(new(store.Program.Id, "Test gift", null, "https://legacy/image.png",
            "Gift", 100, null, null, null), CancellationToken.None);
        var invalid = FakeImageFactory.Fake("image/png");
        using var content = invalid.Content;
        await Assert.ThrowsAsync<ImageStorageException>(() => service.SetImageAsync(reward.Id, invalid, CancellationToken.None));
        Assert.Equal("https://legacy/image.png", Assert.Single(store.Rewards).ImageUrl);
        Assert.Empty(storage.Blobs);
        Assert.Empty(storage.Deleted);
    }

    private sealed class FakeStore : IRewardStore
    {
        public LoyaltyProgram Program { get; } = new() { Id = Guid.NewGuid(), Name = "LP", Code = "LP-001" };
        public List<LoyaltyReward> Rewards { get; } = [];
        public bool FailSave { get; set; }
        public Task<IReadOnlyCollection<LoyaltyReward>> ListAsync(Guid? programId, bool includeInactive, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<LoyaltyReward>>(Rewards.Where(x => (!programId.HasValue || x.LoyaltyProgramId == programId) && (includeInactive || x.IsActive)).ToArray());
        public Task<LoyaltyReward?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Rewards.SingleOrDefault(x => x.Id == id));
        public Task<LoyaltyProgram?> FindProgramAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<LoyaltyProgram?>(id == Program.Id ? Program : null);
        public Task<string> NextCodeAsync(CancellationToken cancellationToken) => Task.FromResult($"QUA-{Rewards.Count + 1:000}");
        public void Add(LoyaltyReward reward) => Rewards.Add(reward);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => FailSave
            ? Task.FromException(new InvalidOperationException("Database save failed")) : Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
