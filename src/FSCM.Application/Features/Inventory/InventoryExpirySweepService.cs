using System.Text.Json;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Inventory;

public sealed record BatchExpiredPayload(
    Guid BatchId, string BatchCode, Guid WarehouseId, Guid ProductId,
    DateOnly ExpirationDate, decimal Quantity, DateTimeOffset DetectedAt);

public sealed record ExpirySweepResult(int BatchesScanned, int StatusesUpdated, int BatchesExpired);

/// <summary>
/// Recomputes and persists batch shelf-life status (Valid / NearExpiry / Expired) and emits a
/// <c>BatchExpired</c> outbox message when a batch crosses its expiration date. Invoked by the
/// nightly date-tracker job (BE Sprint 1 - runs 01:30 Asia/Ho_Chi_Minh) and is safe to run on demand.
/// </summary>
public sealed class InventoryExpirySweepService(IBatchExpiryStore store, TimeProvider timeProvider)
{
    private static readonly TimeZoneInfo VietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    public async Task<ExpirySweepResult> RunAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var businessDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, VietnamTimeZone).DateTime);
        var run = new ExpirySweepRun
        {
            Id = Guid.NewGuid(),
            BusinessDate = businessDate,
            Status = ExpirySweepStatuses.Running,
            StartedAt = now
        };
        store.AddSweepRun(run);
        await store.SaveChangesAsync(cancellationToken);

        try
        {
            var candidates = await store.ListBatchesForExpirySweepAsync(cancellationToken);
            var updated = 0;
            var expired = 0;
            foreach (var candidate in candidates)
            {
                var batch = candidate.Batch;
                var newStatus = CalculateExpiryStatus(batch.ExpirationDate, candidate.NearDateThresholdHours, businessDate);
                if (newStatus == batch.ExpiryStatus) continue;

                var wasExpired = batch.ExpiryStatus == BatchExpiryStatuses.Expired;
                batch.ExpiryStatus = newStatus;
                batch.Version++;
                batch.UpdatedAt = now;
                updated++;

                if (newStatus == BatchExpiryStatuses.Expired && !wasExpired)
                {
                    expired++;
                    store.AddOutboxMessage(BuildBatchExpiredMessage(batch, now));
                }
            }

            run.Status = ExpirySweepStatuses.Completed;
            run.BatchesScanned = candidates.Count;
            run.StatusesUpdated = updated;
            run.BatchesExpired = expired;
            run.CompletedAt = timeProvider.GetUtcNow();
            await store.SaveChangesAsync(cancellationToken);
            return new ExpirySweepResult(candidates.Count, updated, expired);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            run.Status = ExpirySweepStatuses.Failed;
            run.Error = exception.Message.Length <= 2_000 ? exception.Message : exception.Message[..2_000];
            run.CompletedAt = timeProvider.GetUtcNow();
            await store.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    private static OutboxMessage BuildBatchExpiredMessage(Batch batch, DateTimeOffset now)
    {
        var payload = new BatchExpiredPayload(
            batch.Id, batch.BatchCode, batch.WarehouseId, batch.ProductId,
            batch.ExpirationDate, batch.Quantity, now);
        return new OutboxMessage
        {
            // A batch can cross the expiration boundary only once. Reusing the aggregate ID as the
            // message ID gives the outbox a database-enforced idempotency key across job replicas.
            Id = batch.Id,
            EventType = InventoryOutboxEventTypes.BatchExpired,
            AggregateType = OutboxAggregateTypes.Batch,
            AggregateId = batch.Id,
            Payload = JsonSerializer.Serialize(payload),
            Status = OutboxMessageStatuses.Pending,
            RetryCount = 0,
            CreatedAt = now
        };
    }

    private static string CalculateExpiryStatus(DateOnly expirationDate, int nearDateThresholdHours, DateOnly today)
    {
        if (expirationDate < today) return BatchExpiryStatuses.Expired;
        var nearDate = today.AddDays((int)Math.Ceiling(nearDateThresholdHours / 24m));
        return expirationDate <= nearDate ? BatchExpiryStatuses.NearExpiry : BatchExpiryStatuses.Valid;
    }
}
