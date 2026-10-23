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
        var store = new FakeStore(new([retailer], [], [], [], [], [], [], null, retailer.UpdatedAt));
        var service = new MobileSnapshotService(store, new FixedTimeProvider(Now));

        var result = await service.GetAsync(Guid.NewGuid(), null, default);

        Assert.Equal("Full", result.DeltaMode);
        Assert.False(result.NotModified);
        Assert.NotEmpty(result.Version);
        Assert.Equal(64, result.Checksum.Length);
        Assert.Contains("tham khảo", result.StockDisclaimer);
        Assert.Contains("sau khi phân bổ", result.PriceDisclaimer);
    }

    [Fact]
    public async Task Delta_without_changes_is_marked_not_modified()
    {
        var store = new FakeStore(new([], [], [], [], [], [], [], null, Now));
        var service = new MobileSnapshotService(store, new FixedTimeProvider(Now));

        var result = await service.GetAsync(Guid.NewGuid(), Now.UtcTicks.ToString(), default);

        Assert.Equal("Delta", result.DeltaMode);
        Assert.True(result.NotModified);
        Assert.Single(store.RequestedChangesAfter);
        Assert.Null(store.RequestedChangesAfter[0]);
    }

    [Theory]
    [InlineData("not-a-version")]
    [InlineData("-1")]
    public async Task Invalid_since_version_is_rejected(string version)
    {
        var service = new MobileSnapshotService(new FakeStore(new([], [], [], [], [], [], [], null, Now)),
            new FixedTimeProvider(Now));

        var exception = await Assert.ThrowsAsync<OrderOperationException>(() =>
            service.GetAsync(Guid.NewGuid(), version, default));

        Assert.Equal("validation_error", exception.Code);
    }

    private sealed class FakeStore(MobileSnapshotData data) : IMobileSnapshotStore
    {
        public List<DateTimeOffset?> RequestedChangesAfter { get; } = [];
        public Task<MobileSnapshotData> LoadAsync(Guid salesUserId, DateTimeOffset? changedAfter,
            CancellationToken cancellationToken)
        {
            RequestedChangesAfter.Add(changedAfter);
            return Task.FromResult(data);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
