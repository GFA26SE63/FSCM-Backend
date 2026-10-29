using System.Text.Json;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Inventory;

public sealed record InventoryOutboxDispatchResult(int MessagesRead, int MessagesProcessed, int MessagesFailed);

public sealed class InventoryOutboxDispatcher(IInventoryOutboxStore store, TimeProvider timeProvider)
{
    private const int MaximumRetryCount = 5;

    public async Task<InventoryOutboxDispatchResult> DispatchAsync(
        int batchSize, CancellationToken cancellationToken)
    {
        if (batchSize is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be between 1 and 500.");

        var messages = await store.ListPendingBatchExpiredMessagesAsync(batchSize, cancellationToken);
        var processed = 0;
        var failed = 0;
        foreach (var message in messages)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<BatchExpiredPayload>(message.Payload)
                    ?? throw new JsonException("BatchExpired payload is empty.");
                if (payload.BatchId == Guid.Empty || payload.WarehouseId == Guid.Empty ||
                    string.IsNullOrWhiteSpace(payload.BatchCode))
                    throw new JsonException("BatchExpired payload is invalid.");
                var recipients = await store.ListExpiryNotificationRecipientsAsync(
                    payload.WarehouseId, cancellationToken);
                foreach (var userId in recipients)
                {
                    if (await store.NotificationExistsAsync(userId, message.Id, cancellationToken)) continue;
                    store.AddNotification(new Notification
                    {
                        Id = Guid.NewGuid(),
                        UserAccountId = userId,
                        OutboxMessageId = message.Id,
                        RelatedEntityType = "Batch",
                        RelatedEntityId = payload.BatchId,
                        DeepLink = $"/inventory/batches/{payload.BatchId}",
                        Type = InventoryNotificationTypes.BatchExpired,
                        Title = "Lô hàng đã hết hạn",
                        Message = $"Lô {payload.BatchCode} đã hết hạn ngày {payload.ExpirationDate:dd/MM/yyyy}.",
                        IsRead = false,
                        CreatedAt = timeProvider.GetUtcNow()
                    });
                }

                message.Status = OutboxMessageStatuses.Processed;
                message.ProcessedAt = timeProvider.GetUtcNow();
                message.LastError = null;
                await store.SaveChangesAsync(cancellationToken);
                processed++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                message.RetryCount++;
                message.Status = message.RetryCount >= MaximumRetryCount
                    ? OutboxMessageStatuses.Failed
                    : OutboxMessageStatuses.Pending;
                message.LastError = Truncate(exception.Message, 2_000);
                await store.SaveChangesAsync(cancellationToken);
                failed++;
            }
        }

        return new InventoryOutboxDispatchResult(messages.Count, processed, failed);
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}
