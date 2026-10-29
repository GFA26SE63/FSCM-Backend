using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Inventory;

/// <summary>
/// A batch tracked for the nightly shelf-life sweep, joined with its product threshold and the
/// stock figures needed to lock expired stock out of the issuable pool (F42 → F100).
/// </summary>
public sealed record BatchExpiryCandidate(
    Batch Batch, int NearDateThresholdHours,
    decimal PhysicalQuantity, decimal ReservedQuantity, decimal ActiveLockedQuantity);

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

    /// <summary>Adds an F100 inventory lock created by the nightly job for newly-expired stock.</summary>
    void AddInventoryLock(InventoryLock inventoryLock);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
