using FSCM.Application.Features.Inventory;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Inventory;

public sealed class InventoryExpirySweepServiceTests
{
    // 2026-10-13 05:00 UTC == 2026-10-13 12:00 Asia/Ho_Chi_Minh, so the business date is 2026-10-13.
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sweep_marks_past_dated_batch_expired_and_emits_outbox_event()
    {
        var batch = NewBatch(new DateOnly(2026, 10, 12), BatchExpiryStatuses.Valid);
        var store = new FakeStore([new BatchExpiryCandidate(batch, NearDateThresholdHours: 48)]);
        var service = new InventoryExpirySweepService(store, new FixedTimeProvider(Now));

        var result = await service.RunAsync(default);

        Assert.Equal(BatchExpiryStatuses.Expired, batch.ExpiryStatus);
        Assert.Equal(1, result.BatchesExpired);
        Assert.Equal(1, result.StatusesUpdated);
        var message = Assert.Single(store.Outbox);
        Assert.Equal(InventoryOutboxEventTypes.BatchExpired, message.EventType);
        Assert.Equal(OutboxAggregateTypes.Batch, message.AggregateType);
        Assert.Equal(batch.Id, message.AggregateId);
        Assert.Equal(OutboxMessageStatuses.Pending, message.Status);
        Assert.True(store.Saved);
    }

    [Fact]
    public async Task Sweep_marks_near_expiry_without_emitting_event()
    {
        // Expires in 1 day with a 48h threshold -> NearExpiry.
        var batch = NewBatch(new DateOnly(2026, 10, 14), BatchExpiryStatuses.Valid);
        var store = new FakeStore([new BatchExpiryCandidate(batch, NearDateThresholdHours: 48)]);
        var service = new InventoryExpirySweepService(store, new FixedTimeProvider(Now));

        var result = await service.RunAsync(default);

        Assert.Equal(BatchExpiryStatuses.NearExpiry, batch.ExpiryStatus);
        Assert.Equal(0, result.BatchesExpired);
        Assert.Empty(store.Outbox);
    }

    [Fact]
    public async Task Sweep_does_not_persist_or_duplicate_when_nothing_changes()
    {
        var batch = NewBatch(new DateOnly(2027, 1, 1), BatchExpiryStatuses.Valid);
        var store = new FakeStore([new BatchExpiryCandidate(batch, NearDateThresholdHours: 48)]);
        var service = new InventoryExpirySweepService(store, new FixedTimeProvider(Now));

        var result = await service.RunAsync(default);

        Assert.Equal(0, result.StatusesUpdated);
        Assert.False(store.Saved);
        Assert.Empty(store.Outbox);
    }

    private static Batch NewBatch(DateOnly expiration, string status) => new()
    {
        Id = Guid.NewGuid(),
        WarehouseId = Guid.NewGuid(),
        ProductId = Guid.NewGuid(),
        BatchCode = "WH-HCM-01-SKU001-" + expiration.ToString("ddMMyyyy"),
        Quantity = 10,
        ExpirationDate = expiration,
        ExpiryStatus = status,
        StockStatus = BatchStockStatuses.InStock,
        Version = 1
    };

    private sealed class FakeStore(IReadOnlyCollection<BatchExpiryCandidate> candidates) : IBatchExpiryStore
    {
        public List<OutboxMessage> Outbox { get; } = [];
        public bool Saved { get; private set; }

        public Task<IReadOnlyCollection<BatchExpiryCandidate>> ListBatchesForExpirySweepAsync(
            CancellationToken cancellationToken) => Task.FromResult(candidates);

        public void AddOutboxMessage(OutboxMessage message) => Outbox.Add(message);

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            Saved = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
