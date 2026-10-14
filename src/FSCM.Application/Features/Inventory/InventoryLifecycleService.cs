using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Inventory;

public static class StockHoldStatuses
{
    public const string Active = "Active";
    public const string Converted = "Converted";
    public const string Released = "Released";
}

public static class ReservationStatuses
{
    public const string Active = "Active";
    public const string Released = "Released";
    public const string Consumed = "Consumed";
}

public static class AllocationInventoryStatuses
{
    public const string Held = "Held";
    public const string Reserved = "Reserved";
    public const string Released = "Released";
    public const string Issued = "Issued";
}

public sealed record InventoryLifecycleResult(
    Guid AllocationId,
    Guid BatchId,
    Guid WarehouseId,
    decimal Quantity,
    string Status,
    decimal OnHandQuantity,
    decimal HeldQuantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    int Version);

public sealed class InventoryLifecycleService(IInventoryLifecycleStore store, TimeProvider timeProvider)
{
    public async Task<InventoryLifecycleResult> HoldAsync(
        Guid allocationId,
        Guid actorUserId,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        var view = await GetAsync(allocationId, cancellationToken);
        if (view.Reservation is not null || view.Hold?.Status == StockHoldStatuses.Active)
        {
            return ToResult(view);
        }

        if (view.Hold is not null)
        {
            throw InventoryOperationException.Conflict("Released or converted inventory cannot be held again.");
        }

        EnsureExpectedVersion(view.Balance, expectedVersion);
        EnsureAllocationMatchesBatch(view);
        if (view.Allocation.AllocatedQuantity <= 0 || decimal.Truncate(view.Allocation.AllocatedQuantity) != view.Allocation.AllocatedQuantity)
        {
            throw InventoryOperationException.Conflict("Allocated quantity must be a positive whole number of cartons.");
        }

        if (view.Balance.AvailableQuantity < view.Allocation.AllocatedQuantity)
        {
            throw InventoryOperationException.Conflict("Available inventory is insufficient for the allocation.");
        }

        var now = timeProvider.GetUtcNow();
        view.Balance.HeldQuantity += view.Allocation.AllocatedQuantity;
        UpdateBalance(view.Balance, now);
        view.Allocation.Status = AllocationInventoryStatuses.Held;
        view.Hold = new StockHold
        {
            Id = Guid.NewGuid(),
            AllocationId = view.Allocation.Id,
            BatchId = view.Batch.Id,
            Quantity = view.Allocation.AllocatedQuantity,
            Status = StockHoldStatuses.Active,
            HeldAt = now
        };
        store.AddHold(view.Hold);
        AddHistory(view, actorUserId, InventoryTransactionTypes.OrderHold,
            0, view.Allocation.AllocatedQuantity, 0, "Giữ tồn kho cho đơn hàng", now);
        await store.SaveChangesAsync(cancellationToken);
        return ToResult(view);
    }

    public async Task<InventoryLifecycleResult> ReserveAsync(
        Guid allocationId,
        Guid actorUserId,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        var view = await GetAsync(allocationId, cancellationToken);
        if (view.Reservation is not null)
        {
            return ToResult(view);
        }

        if (view.Hold?.Status != StockHoldStatuses.Active)
        {
            throw InventoryOperationException.Conflict("An active stock hold is required before reservation.");
        }

        EnsureExpectedVersion(view.Balance, expectedVersion);
        if (view.Balance.HeldQuantity < view.Hold.Quantity)
        {
            throw InventoryOperationException.Conflict("Held inventory is inconsistent with the allocation.");
        }

        var now = timeProvider.GetUtcNow();
        view.Balance.HeldQuantity -= view.Hold.Quantity;
        view.Balance.ReservedQuantity += view.Hold.Quantity;
        UpdateBalance(view.Balance, now);
        view.Hold.Status = StockHoldStatuses.Converted;
        view.Allocation.Status = AllocationInventoryStatuses.Reserved;
        view.Reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            AllocationId = view.Allocation.Id,
            BatchId = view.Batch.Id,
            Quantity = view.Hold.Quantity,
            Status = ReservationStatuses.Active,
            ReservedAt = now
        };
        store.AddReservation(view.Reservation);
        AddHistory(view, actorUserId, InventoryTransactionTypes.OrderReserve,
            0, -view.Hold.Quantity, view.Hold.Quantity, "Chuyển tồn kho giữ sang đặt trước", now);
        await store.SaveChangesAsync(cancellationToken);
        return ToResult(view);
    }

    public async Task<InventoryLifecycleResult> ReleaseAsync(
        Guid allocationId,
        Guid actorUserId,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        var view = await GetAsync(allocationId, cancellationToken);
        if (view.Reservation?.Status == ReservationStatuses.Released ||
            view.Reservation is null && view.Hold?.Status == StockHoldStatuses.Released)
        {
            return ToResult(view);
        }

        if (view.Reservation?.Status == ReservationStatuses.Consumed)
        {
            throw InventoryOperationException.Conflict("Issued inventory cannot be released.");
        }

        EnsureExpectedVersion(view.Balance, expectedVersion);
        var now = timeProvider.GetUtcNow();
        decimal heldDelta;
        decimal reservedDelta;
        if (view.Reservation?.Status == ReservationStatuses.Active)
        {
            if (view.Balance.ReservedQuantity < view.Reservation.Quantity)
            {
                throw InventoryOperationException.Conflict("Reserved inventory is inconsistent with the allocation.");
            }

            heldDelta = 0;
            reservedDelta = -view.Reservation.Quantity;
            view.Balance.ReservedQuantity -= view.Reservation.Quantity;
            view.Reservation.Status = ReservationStatuses.Released;
            view.Reservation.ReleasedAt = now;
        }
        else if (view.Hold?.Status == StockHoldStatuses.Active)
        {
            if (view.Balance.HeldQuantity < view.Hold.Quantity)
            {
                throw InventoryOperationException.Conflict("Held inventory is inconsistent with the allocation.");
            }

            heldDelta = -view.Hold.Quantity;
            reservedDelta = 0;
            view.Balance.HeldQuantity -= view.Hold.Quantity;
            view.Hold.Status = StockHoldStatuses.Released;
            view.Hold.ReleasedAt = now;
        }
        else
        {
            throw InventoryOperationException.Conflict("No active hold or reservation exists for the allocation.");
        }

        UpdateBalance(view.Balance, now);
        view.Allocation.Status = AllocationInventoryStatuses.Released;
        AddHistory(view, actorUserId, InventoryTransactionTypes.OrderRelease,
            0, heldDelta, reservedDelta, "Hoàn trả tồn kho của đơn hàng", now);
        await store.SaveChangesAsync(cancellationToken);
        return ToResult(view);
    }

    public async Task<InventoryLifecycleResult> IssueAsync(
        Guid allocationId,
        Guid actorUserId,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        var view = await GetAsync(allocationId, cancellationToken);
        if (view.Reservation?.Status == ReservationStatuses.Consumed)
        {
            return ToResult(view);
        }

        if (view.Reservation?.Status != ReservationStatuses.Active)
        {
            throw InventoryOperationException.Conflict("An active reservation is required before issue.");
        }

        EnsureExpectedVersion(view.Balance, expectedVersion);
        var quantity = view.Reservation.Quantity;
        if (view.Balance.PhysicalQuantity < quantity || view.Balance.ReservedQuantity < quantity || view.Batch.Quantity < quantity)
        {
            throw InventoryOperationException.Conflict("Reserved inventory is insufficient for issue.");
        }

        var now = timeProvider.GetUtcNow();
        view.Balance.PhysicalQuantity -= quantity;
        view.Balance.ReservedQuantity -= quantity;
        UpdateBalance(view.Balance, now);
        view.Batch.Quantity -= quantity;
        view.Batch.StockStatus = view.Batch.Quantity == 0 ? BatchStockStatuses.Depleted : BatchStockStatuses.InStock;
        view.Batch.Version++;
        view.Batch.UpdatedAt = now;
        view.Reservation.Status = ReservationStatuses.Consumed;
        view.Reservation.ConsumedAt = now;
        view.Allocation.Status = AllocationInventoryStatuses.Issued;
        AddHistory(view, actorUserId, InventoryTransactionTypes.OrderIssue,
            -quantity, 0, -quantity, "Xuất kho hoàn tất picking", now);
        await store.SaveChangesAsync(cancellationToken);
        return ToResult(view);
    }

    private async Task<InventoryLifecycleView> GetAsync(Guid allocationId, CancellationToken cancellationToken)
    {
        if (allocationId == Guid.Empty)
        {
            throw InventoryOperationException.Validation("Allocation identifier is required.");
        }

        return await store.FindForUpdateAsync(allocationId, cancellationToken)
            ?? throw InventoryOperationException.NotFound("Allocation inventory was not found.");
    }

    private static void EnsureExpectedVersion(InventoryBalance balance, int expectedVersion)
    {
        if (balance.Version != expectedVersion)
        {
            throw InventoryOperationException.Conflict(
                $"Inventory version is {balance.Version}; refresh and retry with the current version.");
        }
    }

    private static void EnsureAllocationMatchesBatch(InventoryLifecycleView view)
    {
        if (view.Allocation.BatchId != view.Batch.Id || view.Allocation.WarehouseId != view.Batch.WarehouseId)
        {
            throw InventoryOperationException.Conflict("Allocation batch and warehouse are inconsistent.");
        }
    }

    private static void UpdateBalance(InventoryBalance balance, DateTimeOffset now)
    {
        balance.AvailableQuantity = balance.PhysicalQuantity - balance.HeldQuantity - balance.ReservedQuantity;
        if (balance.PhysicalQuantity < 0 || balance.HeldQuantity < 0 ||
            balance.ReservedQuantity < 0 || balance.AvailableQuantity < 0)
        {
            throw InventoryOperationException.Conflict("Inventory quantities cannot become negative.");
        }

        balance.Version++;
        balance.UpdatedAt = now;
    }

    private void AddHistory(
        InventoryLifecycleView view,
        Guid actorUserId,
        string transactionType,
        decimal physicalDelta,
        decimal heldDelta,
        decimal reservedDelta,
        string note,
        DateTimeOffset now) =>
        store.AddTransaction(new InventoryTransaction
        {
            Id = Guid.NewGuid(),
            BatchId = view.Batch.Id,
            WarehouseId = view.Batch.WarehouseId,
            ProductId = view.Batch.ProductId,
            CreatedByUserId = actorUserId,
            TransactionType = transactionType,
            ReferenceCode = view.Allocation.Id.ToString("N"),
            PhysicalQuantityDelta = physicalDelta,
            HeldQuantityDelta = heldDelta,
            ReservedQuantityDelta = reservedDelta,
            PhysicalQuantityAfter = view.Balance.PhysicalQuantity,
            HeldQuantityAfter = view.Balance.HeldQuantity,
            ReservedQuantityAfter = view.Balance.ReservedQuantity,
            AvailableQuantityAfter = view.Balance.AvailableQuantity,
            Note = note,
            OccurredAt = now
        });

    private static InventoryLifecycleResult ToResult(InventoryLifecycleView view) =>
        new(
            view.Allocation.Id,
            view.Batch.Id,
            view.Batch.WarehouseId,
            view.Allocation.AllocatedQuantity,
            view.Allocation.Status,
            view.Balance.PhysicalQuantity,
            view.Balance.HeldQuantity,
            view.Balance.ReservedQuantity,
            view.Balance.AvailableQuantity,
            view.Balance.Version);
}
