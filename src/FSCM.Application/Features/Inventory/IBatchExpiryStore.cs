using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Inventory;

/// <summary>A batch tracked for the nightly shelf-life sweep, joined with its product threshold.</summary>
public sealed record BatchExpiryCandidate(Batch Batch, int NearDateThresholdHours);

/// <summary>Persistence contract for the nightly date-tracker job (BE Sprint 1).</summary>
public interface IBatchExpiryStore
{
    /// <summary>
    /// Loads batches that still hold stock and whose persisted expiry status may be stale,
    /// i.e. anything not already marked Expired. Tracked for update.
    /// </summary>
    Task<IReadOnlyCollection<BatchExpiryCandidate>> ListBatchesForExpirySweepAsync(CancellationToken cancellationToken);

    void AddSweepRun(ExpirySweepRun run);

    void AddOutboxMessage(OutboxMessage message);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
