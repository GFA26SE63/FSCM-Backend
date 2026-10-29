using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.InventoryLocks;
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
        var store = new FakeStore([Candidate(batch, physical: 10, reserved: 0, locked: 0)]);
        var service = new InventoryExpirySweepService(store, new FixedTimeProvider(Now));

        var result = await service.RunAsync(default);

        Assert.Equal(BatchExpiryStatuses.Expired, batch.ExpiryStatus);
        Assert.Equal(1, result.BatchesExpired);
        Assert.Equal(1, result.StatusesUpdated);
        var message = Assert.Single(store.Outbox);
        Assert.Equal(InventoryOutboxEventTypes.BatchExpired, message.EventType);
        Assert.Equal(OutboxAggregateTypes.Batch, message.AggregateType);
        Assert.Equal(batch.Id, message.Id);
        Assert.Equal(batch.Id, message.AggregateId);
        Assert.Equal(OutboxMessageStatuses.Pending, message.Status);
        Assert.Equal(ExpirySweepStatuses.Completed, Assert.Single(store.Runs).Status);
        Assert.True(store.Saved);
    }

    [Fact]
    public async Task Sweep_locks_expired_unreserved_stock_without_resolution()
    {
        var batch = NewBatch(new DateOnly(2026, 10, 12), BatchExpiryStatuses.Valid);
        var store = new FakeStore([Candidate(batch, physical: 10, reserved: 0, locked: 0)]);
        var service = new InventoryExpirySweepService(store, new FixedTimeProvider(Now));

        await service.RunAsync(default);

        var inventoryLock = Assert.Single(store.Locks);
        Assert.Equal(InventoryLockStatuses.Active, inventoryLock.Status);
        Assert.Equal(InventoryLockReasons.Expired, inventoryLock.Reason);
        Assert.Equal(10, inventoryLock.LockedQuantity);
        Assert.False(inventoryLock.RequiresResolution);
        Assert.Null(inventoryLock.LockedByUserId);
    }

    [Fact]
    public async Task Sweep_locks_expired_reserved_stock_and_flags_resolution()
    {
        // 10 on-hand, 6 reserved -> locking the full 10 overlaps reserved -> needs resolution.
        var batch = NewBatch(new DateOnly(2026, 10, 12), BatchExpiryStatuses.Valid);
        var store = new FakeStore([Candidate(batch, physical: 10, reserved: 6, locked: 0)]);
        var service = new InventoryExpirySweepService(store, new FixedTimeProvider(Now));

        await service.RunAsync(default);

        var inventoryLock = Assert.Single(store.Locks);
        Assert.Equal(10, inventoryLock.LockedQuantity);
        Assert.True(inventoryLock.RequiresResolution);
    }

    [Fact]
    public async Task Sweep_marks_near_expiry_without_emitting_event_or_lock()
    {
        var batch = NewBatch(new DateOnly(2026, 10, 14), BatchExpiryStatuses.Valid);
        var store = new FakeStore([Candidate(batch, physical: 10, reserved: 0, locked: 0)]);
        var service = new InventoryExpirySweepService(store, new FixedTimeProvider(Now));

        var result = await service.RunAsync(default);

        Assert.Equal(BatchExpiryStatuses.NearExpiry, batch.ExpiryStatus);
        Assert.Equal(0, result.BatchesExpired);
        Assert.Empty(store.Outbox);
        Assert.Empty(store.Locks);
    }

    [Fact]
    public async Task Sweep_records_completed_run_without_duplicate_event_when_nothing_changes()
    {
        var batch = NewBatch(new DateOnly(2027, 1, 1), BatchExpiryStatuses.Valid);
        var store = new FakeStore([Candidate(batch, physical: 10, reserved: 0, locked: 0)]);
        var service = new InventoryExpirySweepService(store, new FixedTimeProvider(Now));

        var result = await service.RunAsync(default);

        Assert.Equal(0, result.StatusesUpdated);
        Assert.True(store.Saved);
        var run = Assert.Single(store.Runs);
        Assert.Equal(ExpirySweepStatuses.Completed, run.Status);
        Assert.Equal(1, run.BatchesScanned);
        Assert.Empty(store.Outbox);
        Assert.Empty(store.Locks);
    }

    private static BatchExpiryCandidate Candidate(Batch batch, decimal physical, decimal reserved, decimal locked) =>
        new(batch, NearDateThresholdHours: 48, physical, reserved, locked);

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
        public List<ExpirySweepRun> Runs { get; } = [];
        public List<InventoryLock> Locks { get; } = [];
        public bool Saved { get; private set; }

        public Task<IReadOnlyCollection<BatchExpiryCandidate>> ListBatchesForExpirySweepAsync(
            CancellationToken cancellationToken) => Task.FromResult(candidates);

        public void AddOutboxMessage(OutboxMessage message) => Outbox.Add(message);
        public void AddSweepRun(ExpirySweepRun run) => Runs.Add(run);
        public void AddInventoryLock(InventoryLock inventoryLock) => Locks.Add(inventoryLock);

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
