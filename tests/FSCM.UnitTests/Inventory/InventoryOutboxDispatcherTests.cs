using System.Text.Json;
using FSCM.Application.Features.Inventory;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Inventory;

public sealed class InventoryOutboxDispatcherTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Batch_expired_message_creates_Vietnamese_notifications_and_is_processed_once()
    {
        var warehouseId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var recipients = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var message = NewMessage(new BatchExpiredPayload(
            batchId, "WH-TA-SKU001-20102026", warehouseId, Guid.NewGuid(),
            new DateOnly(2026, 10, 20), 25, Now));
        var store = new FakeStore(message, recipients);
        var dispatcher = new InventoryOutboxDispatcher(store, new FixedTimeProvider(Now));

        var first = await dispatcher.DispatchAsync(50, default);
        var second = await dispatcher.DispatchAsync(50, default);

        Assert.Equal(1, first.MessagesProcessed);
        Assert.Equal(0, second.MessagesRead);
        Assert.Equal(OutboxMessageStatuses.Processed, message.Status);
        Assert.Equal(2, store.Notifications.Count);
        Assert.All(store.Notifications, notification =>
        {
            Assert.Equal(message.Id, notification.OutboxMessageId);
            Assert.Equal(InventoryNotificationTypes.BatchExpired, notification.Type);
            Assert.Contains("đã hết hạn", notification.Message);
        });
    }

    [Fact]
    public async Task Invalid_payload_is_retried_and_keeps_error_for_monitoring()
    {
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = InventoryOutboxEventTypes.BatchExpired,
            AggregateType = OutboxAggregateTypes.Batch,
            AggregateId = Guid.NewGuid(),
            Payload = "{}",
            Status = OutboxMessageStatuses.Pending,
            CreatedAt = Now
        };
        var store = new FakeStore(message, []);
        var dispatcher = new InventoryOutboxDispatcher(store, new FixedTimeProvider(Now));

        var result = await dispatcher.DispatchAsync(50, default);

        Assert.Equal(1, result.MessagesFailed);
        Assert.Equal(1, message.RetryCount);
        Assert.Equal(OutboxMessageStatuses.Pending, message.Status);
        Assert.False(string.IsNullOrWhiteSpace(message.LastError));
    }

    [Fact]
    public async Task Invalid_payload_moves_to_failed_after_the_retry_limit()
    {
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = InventoryOutboxEventTypes.BatchExpired,
            AggregateType = OutboxAggregateTypes.Batch,
            AggregateId = Guid.NewGuid(),
            Payload = "{}",
            Status = OutboxMessageStatuses.Pending,
            CreatedAt = Now
        };
        var dispatcher = new InventoryOutboxDispatcher(
            new FakeStore(message, []), new FixedTimeProvider(Now));

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await dispatcher.DispatchAsync(50, default);
        }

        Assert.Equal(5, message.RetryCount);
        Assert.Equal(OutboxMessageStatuses.Failed, message.Status);
    }

    private static OutboxMessage NewMessage(BatchExpiredPayload payload) => new()
    {
        Id = Guid.NewGuid(),
        EventType = InventoryOutboxEventTypes.BatchExpired,
        AggregateType = OutboxAggregateTypes.Batch,
        AggregateId = payload.BatchId,
        Payload = JsonSerializer.Serialize(payload),
        Status = OutboxMessageStatuses.Pending,
        CreatedAt = Now
    };

    private sealed class FakeStore(OutboxMessage message, IReadOnlyCollection<Guid> recipients)
        : IInventoryOutboxStore
    {
        public List<Notification> Notifications { get; } = [];

        public Task<IReadOnlyCollection<OutboxMessage>> ListPendingBatchExpiredMessagesAsync(
            int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<OutboxMessage>>(
                message.Status == OutboxMessageStatuses.Pending ? [message] : []);

        public Task<IReadOnlyCollection<Guid>> ListExpiryNotificationRecipientsAsync(
            Guid warehouseId, CancellationToken cancellationToken) => Task.FromResult(recipients);

        public Task<bool> NotificationExistsAsync(
            Guid userAccountId, Guid outboxMessageId, CancellationToken cancellationToken) =>
            Task.FromResult(Notifications.Any(item =>
                item.UserAccountId == userAccountId && item.OutboxMessageId == outboxMessageId));

        public void AddNotification(Notification notification) => Notifications.Add(notification);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
