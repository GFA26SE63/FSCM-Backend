using FSCM.Application.Features.MobileSnapshots;
using FSCM.Application.Features.Orders;

namespace FSCM.UnitTests.MobileSnapshots;

public sealed class MobileSnapshotServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Full_snapshot_has_version_checksum_and_offline_disclaimers()
    {
        var retailer = new MobileSnapshotRetailer(Guid.NewGuid(), "10000001", "Cửa hàng An Khang",
            "Quận 1", "Active", 14, Now.AddMinutes(-1));
        var store = new FakeStore(new([retailer], [], [], [], [], [], [], null));
        var service = new MobileSnapshotService(store, new FixedTimeProvider(Now));

        var result = await service.GetAsync(Guid.NewGuid(), default);

        Assert.Equal("Full", result.DeltaMode);
        Assert.StartsWith("f29-v1-", result.Version);
        Assert.Equal(64, result.Checksum.Length);
        Assert.Contains("tham khảo", result.StockDisclaimer);
        Assert.Contains("sau khi phân bổ", result.PriceDisclaimer);
    }

    [Fact]
    public async Task Same_content_has_stable_version_for_http_cache_revalidation()
    {
        var store = new FakeStore(new([], [], [], [], [], [], [], null));
        var service = new MobileSnapshotService(store, new FixedTimeProvider(Now));

        var first = await service.GetAsync(Guid.NewGuid(), default);
        var second = await service.GetAsync(Guid.NewGuid(), default);

        Assert.Equal("Full", first.DeltaMode);
        Assert.Equal(first.Version, second.Version);
        Assert.Equal(first.Checksum, second.Checksum);
        Assert.Equal(2, store.EffectiveAt.Count);
        Assert.All(store.EffectiveAt, value => Assert.Equal(Now, value));
    }

    [Fact]
    public async Task Scoped_content_change_produces_a_new_version()
    {
        var retailer = new MobileSnapshotRetailer(Guid.NewGuid(), "10000001", "Cửa hàng An Khang",
            "Quận 1", "Active", 14, Now.AddMinutes(-1));
        var withRetailer = new MobileSnapshotService(
            new FakeStore(new([retailer], [], [], [], [], [], [], null)), new FixedTimeProvider(Now));
        var withoutRetailer = new MobileSnapshotService(
            new FakeStore(new([], [], [], [], [], [], [], null)), new FixedTimeProvider(Now));

        var beforeScopeRemoval = await withRetailer.GetAsync(Guid.NewGuid(), default);
        var afterScopeRemoval = await withoutRetailer.GetAsync(Guid.NewGuid(), default);

        Assert.NotEqual(beforeScopeRemoval.Version, afterScopeRemoval.Version);
        Assert.NotEqual(beforeScopeRemoval.Checksum, afterScopeRemoval.Checksum);
    }

    private sealed class FakeStore(MobileSnapshotData data) : IMobileSnapshotStore
    {
        public List<DateTimeOffset> EffectiveAt { get; } = [];
        public Task<MobileSnapshotData> LoadAsync(Guid salesUserId, DateTimeOffset effectiveAt,
            CancellationToken cancellationToken)
        {
            EffectiveAt.Add(effectiveAt);
            return Task.FromResult(data);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
