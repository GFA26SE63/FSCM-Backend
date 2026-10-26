using FSCM.Application.Features.Inventory;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.AllocationRuns;

/// <summary>One allocation of an approved plan, with the stock it is about to hold.</summary>
public sealed record ReservableAllocation(
    AllocationRunAllocation Allocation,
    Batch Batch,
    InventoryBalance Balance,
    bool AlreadyReserved);

public sealed record ReservationOutcome(
    int ReservedCount,
    int AlreadyHeldCount,
    decimal ReservedQuantity,
    int Attempts);

/// <summary>
/// Holds the stock an approved plan promised. The plan was built against a snapshot taken at the
/// cutoff, and hours can pass before an Admin decides, so every line is checked again against live
/// stock before anything is held.
///
/// The whole plan is held or none of it is. A plan that reserved half its orders would leave the
/// rest short with no record of why, and the orders it did reserve would have been chosen by a
/// priority order that no longer applies.
///
/// Holding stock is not issuing it: OnHand does not move here. Reserved rises, Available falls, and
/// the goods only leave when a warehouse confirms dispatch (F96).
/// </summary>
public sealed class AllocationReservationService(IAllocationRunStore store, TimeProvider timeProvider)
{
    public const int DefaultMaxAttempts = 3;

    /// <summary>
    /// Reserves every allocation of a plan version. Re-running is safe: an allocation that already
    /// holds stock is counted and skipped rather than held twice.
    /// </summary>
    public async Task<ReservationOutcome> ReserveAsync(
        Guid planVersionId,
        Guid actorUserId,
        string referenceCode,
        int maxAttempts = DefaultMaxAttempts,
        CancellationToken cancellationToken = default)
    {
        if (maxAttempts < 1)
        {
            throw AllocationRunOperationException.Validation("At least one attempt is required.");
        }

        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                return await ReserveOnceAsync(
                    planVersionId, actorUserId, referenceCode, attempt, cancellationToken);
            }
            catch (AllocationRunOperationException exception)
                when (exception.Code == "conflict" && attempt < maxAttempts)
            {
                // Someone moved stock between the check and the write. Read it again rather than
                // forcing the hold through on numbers we know are stale.
                store.ClearTracking();
            }
        }
    }

    private async Task<ReservationOutcome> ReserveOnceAsync(
        Guid planVersionId,
        Guid actorUserId,
        string referenceCode,
        int attempt,
        CancellationToken cancellationToken)
    {
        var reserved = 0;
        var alreadyHeld = 0;
        var quantity = 0m;

        await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            // A run where every order came up short allocates nothing. There is simply nothing to
            // hold, which Admin still has to acknowledge, so this is not an error.
            var rows = await store.ListReservableAsync(planVersionId, transactionCancellationToken);
            if (rows.Count == 0) return;

            var now = timeProvider.GetUtcNow();
            foreach (var row in rows)
            {
                if (row.AlreadyReserved)
                {
                    alreadyHeld++;
                    continue;
                }

                Revalidate(row);
                var balance = row.Balance;
                balance.ReservedQuantity += row.Allocation.Quantity;
                balance.AvailableQuantity = balance.PhysicalQuantity - balance.ReservedQuantity;
                balance.Version++;
                balance.UpdatedAt = now;

                store.AddReservation(new Reservation
                {
                    Id = Guid.NewGuid(),
                    AllocationRunAllocationId = row.Allocation.Id,
                    BatchId = row.Allocation.BatchId,
                    Quantity = row.Allocation.Quantity,
                    Status = ReservationStatuses.Active,
                    ReservedAt = now
                });

                store.AddInventoryTransaction(new InventoryTransaction
                {
                    Id = Guid.NewGuid(),
                    BatchId = row.Batch.Id,
                    WarehouseId = row.Batch.WarehouseId,
                    ProductId = row.Batch.ProductId,
                    CreatedByUserId = actorUserId,
                    TransactionType = InventoryTransactionTypes.OrderReserve,
                    SourceType = InventoryTransactionSourceTypes.Allocation,
                    SourceId = row.Allocation.Id,
                    ReferenceCode = referenceCode,
                    // Holding stock does not move it: only the reserved side changes.
                    PhysicalQuantityDelta = 0m,
                    ReservedQuantityDelta = row.Allocation.Quantity,
                    PhysicalQuantityAfter = balance.PhysicalQuantity,
                    ReservedQuantityAfter = balance.ReservedQuantity,
                    OccurredAt = now
                });

                reserved++;
                quantity += row.Allocation.Quantity;
            }

            await store.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);

        store.ClearTracking();
        return new ReservationOutcome(reserved, alreadyHeld, quantity, attempt);
    }

    /// <summary>
    /// Re-checks one allocation against live stock. A plan built at 12:00 can be decided at 14:00,
    /// and in between a batch can expire, be locked, or be drawn down by something else.
    /// </summary>
    private static void Revalidate(ReservableAllocation row)
    {
        if (row.Batch.ExpiryStatus == BatchExpiryStatuses.Expired)
        {
            throw AllocationRunOperationException.Conflict(
                $"Batch {row.Batch.BatchCode} expired after the plan was built.");
        }

        if (row.Balance.AvailableQuantity < row.Allocation.Quantity)
        {
            throw AllocationRunOperationException.Conflict(
                $"Batch {row.Batch.BatchCode} has {row.Balance.AvailableQuantity:0.###} available " +
                $"but the plan needs {row.Allocation.Quantity:0.###}.");
        }
    }
}
