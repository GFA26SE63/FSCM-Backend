using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Inventory;

public static class ReservationStatuses
{
    public const string Active = "Active";
    public const string Released = "Released";
    public const string Consumed = "Consumed";
    public const string Expired = "Expired";
}

public static class AllocationInventoryStatuses
{
    public const string Tentative = "Tentative";
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
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    int Version);

public sealed class InventoryLifecycleService(IInventoryLifecycleStore store, TimeProvider timeProvider) : IInventoryLifecycleService
{
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

        EnsureExpectedVersion(view.Balance, expectedVersion);
        EnsureAllocationMatchesBatch(view);
        var quantity = view.Allocation.AllocatedQuantity;
        EnsurePositiveWholeQuantity(quantity);
        if (view.Balance.AvailableQuantity < quantity)
        {
            throw InventoryOperationException.Conflict("Available inventory is insufficient for the allocation.");
        }

        var now = timeProvider.GetUtcNow();
        view.Balance.ReservedQuantity += quantity;
        UpdateBalance(view.Balance, now);
        view.Allocation.Status = AllocationInventoryStatuses.Reserved;
        view.Reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            AllocationId = view.Allocation.Id,
            BatchId = view.Batch.Id,
            Quantity = quantity,
            Status = ReservationStatuses.Active,
            ReservedAt = now
        };
        store.AddReservation(view.Reservation);
        AddHistory(view, actorUserId, InventoryTransactionTypes.OrderReserve,
            0, quantity, "Đặt trước tồn kho khi duyệt kết quả phân bổ", now);
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
        if (view.Reservation is null)
        {
            view.Allocation.Status = AllocationInventoryStatuses.Released;
            return ToResult(view);
        }

        if (view.Reservation.Status == ReservationStatuses.Released)
        {
            return ToResult(view);
        }

        if (view.Reservation.Status == ReservationStatuses.Consumed)
        {
            throw InventoryOperationException.Conflict("Issued inventory cannot be released.");
        }

        EnsureExpectedVersion(view.Balance, expectedVersion);
        if (view.Balance.ReservedQuantity < view.Reservation.Quantity)
        {
            throw InventoryOperationException.Conflict("Reserved inventory is inconsistent with the allocation.");
        }

        var now = timeProvider.GetUtcNow();
        var quantity = view.Reservation.Quantity;
        view.Balance.ReservedQuantity -= quantity;
        UpdateBalance(view.Balance, now);
        view.Reservation.Status = ReservationStatuses.Released;
        view.Reservation.ReleasedAt = now;
        view.Allocation.Status = AllocationInventoryStatuses.Released;
        AddHistory(view, actorUserId, InventoryTransactionTypes.OrderRelease,
            0, -quantity, "Hoàn trả tồn kho đã đặt trước", now);
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
        if (view.Balance.PhysicalQuantity < quantity ||
            view.Balance.ReservedQuantity < quantity ||
            view.Batch.Quantity < quantity)
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
            -quantity, -quantity, "Xuất kho theo shipment", now);
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

    private static void EnsurePositiveWholeQuantity(decimal quantity)
    {
        if (quantity <= 0 || decimal.Truncate(quantity) != quantity)
        {
            throw InventoryOperationException.Conflict("Allocated quantity must be a positive whole number of cartons.");
        }
    }

    private static void UpdateBalance(InventoryBalance balance, DateTimeOffset now)
    {
        balance.AvailableQuantity = balance.PhysicalQuantity - balance.ReservedQuantity;
        if (balance.PhysicalQuantity < 0 || balance.ReservedQuantity < 0 || balance.AvailableQuantity < 0)
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
            SourceType = InventoryTransactionSourceTypes.Allocation,
            SourceId = view.Allocation.Id,
            ReferenceCode = view.Allocation.Id.ToString("N"),
            PhysicalQuantityDelta = physicalDelta,
            ReservedQuantityDelta = reservedDelta,
            PhysicalQuantityAfter = view.Balance.PhysicalQuantity,
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
            view.Balance.ReservedQuantity,
            view.Balance.AvailableQuantity,
            view.Balance.Version);
}
