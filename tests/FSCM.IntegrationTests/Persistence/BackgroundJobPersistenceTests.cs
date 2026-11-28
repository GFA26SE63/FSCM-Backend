using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.Orders;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using FSCM.Persistence.Inventory;
using FSCM.Persistence.Orders;
using Microsoft.EntityFrameworkCore;

namespace FSCM.IntegrationTests.Persistence;

public sealed class BackgroundJobPersistenceTests
{
    [ConfiguredDatabaseFact]
    public async Task Sweep_candidates_recognize_already_processed_expiry_events()
    {
        await using var db = new AppDbContextFactory().CreateDbContext([]);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var batch = await db.BatchRecords.FirstAsync(b => db.InventoryBalanceRecords.Any(i => i.BatchId == b.Id));
        batch.ExpiryStatus = BatchExpiryStatuses.Valid;
        var existing = await db.OutboxMessageRecords.FirstOrDefaultAsync(m =>
            m.AggregateId == batch.Id && m.EventType == InventoryOutboxEventTypes.BatchExpired &&
            m.AggregateType == OutboxAggregateTypes.Batch);
        if (existing is null)
        {
            db.OutboxMessageRecords.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(), AggregateId = batch.Id, AggregateType = OutboxAggregateTypes.Batch,
                EventType = InventoryOutboxEventTypes.BatchExpired, Payload = "{}",
                Status = OutboxMessageStatuses.Processed, CreatedAt = DateTimeOffset.UtcNow
            });
        }
        else existing.Status = OutboxMessageStatuses.Processed;
        await db.SaveChangesAsync();

        var candidates = await new InventoryStore(db).ListBatchesForExpirySweepAsync(default);

        Assert.True(Assert.Single(candidates, c => c.Batch.Id == batch.Id).HasExpiryEvent);
        await transaction.RollbackAsync();
    }

    [ConfiguredDatabaseFact]
    public async Task Automated_completion_persists_null_actor_and_repeated_completion_is_noop()
    {
        await using var db = new AppDbContextFactory().CreateDbContext([]);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var order = await db.SalesOrderRecords.FirstAsync();
        order.Status = OrderStatuses.Delivered;
        await db.SaveChangesAsync();
        var before = await db.OrderStatusHistoryRecords.CountAsync(h => h.SalesOrderId == order.Id);
        var now = DateTimeOffset.UtcNow;
        var store = new OrderCompletionStore(db);

        await store.CompleteAsync(order.Id, null, now, default);
        await store.CompleteAsync(order.Id, null, now, default);

        var history = await db.OrderStatusHistoryRecords.SingleAsync(h =>
            h.SalesOrderId == order.Id && h.ChangedAt == now && h.ToStatus == OrderStatuses.Completed);
        Assert.Null(history.ChangedByUserId);
        Assert.Equal(before + 1, await db.OrderStatusHistoryRecords.CountAsync(h => h.SalesOrderId == order.Id));
        Assert.Equal(OrderStatuses.Completed, order.Status);
        await transaction.RollbackAsync();
    }

    [ConfiguredDatabaseFact]
    public async Task Failure_record_discards_pending_outbox_and_batch_changes()
    {
        await using var db = new AppDbContextFactory().CreateDbContext([]);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var store = new InventoryStore(db);
        var run = new ExpirySweepRun
        {
            Id = Guid.NewGuid(), BusinessDate = DateOnly.FromDateTime(DateTime.UtcNow),
            StartedAt = DateTimeOffset.UtcNow, Status = ExpirySweepStatuses.Running
        };
        store.AddSweepRun(run);
        await store.SaveChangesAsync(default);
        var batch = await db.BatchRecords.FirstAsync();
        var originalStatus = batch.ExpiryStatus;
        batch.ExpiryStatus = "MustNotPersist";
        var messageId = Guid.NewGuid();
        store.AddOutboxMessage(new OutboxMessage
        {
            Id = messageId, AggregateId = batch.Id, AggregateType = OutboxAggregateTypes.Batch,
            EventType = InventoryOutboxEventTypes.BatchExpired, Payload = "{}",
            Status = OutboxMessageStatuses.Pending, CreatedAt = DateTimeOffset.UtcNow
        });
        run.Status = ExpirySweepStatuses.Failed;
        run.Error = "Simulated failed business save";
        run.CompletedAt = DateTimeOffset.UtcNow;

        await store.SaveFailedSweepAsync(run, default);
        db.ChangeTracker.Clear();

        Assert.False(await db.OutboxMessageRecords.AnyAsync(m => m.Id == messageId));
        Assert.Equal(originalStatus, (await db.BatchRecords.SingleAsync(b => b.Id == batch.Id)).ExpiryStatus);
        Assert.Equal(ExpirySweepStatuses.Failed, (await db.ExpirySweepRunRecords.SingleAsync(r => r.Id == run.Id)).Status);
        await transaction.RollbackAsync();
    }
}
