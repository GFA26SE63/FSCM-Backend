using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Quotes;
using FSCM.Domain.Allocation;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.AllocationRuns;

public sealed record RealTimeInventoryCandidate(Batch Batch, InventoryBalance Balance, Warehouse Warehouse,
    Product Product, decimal? WarehouseLatitude, decimal? WarehouseLongitude);
public sealed record RealTimeAllocationContext(SalesOrder Order, IReadOnlyCollection<SalesOrderLine> Lines,
    MapLocation? RetailerLocation, IReadOnlyCollection<RealTimeInventoryCandidate> Candidates, int NextAttemptNumber);

public interface IRealTimeAllocationStore
{
    Task<RealTimeAllocationContext?> LoadForUpdateAsync(Guid orderId, CancellationToken cancellationToken);
    void AddRun(AllocationRun run);
    void AddAllocation(Allocation allocation);
    void AddReservation(Reservation reservation);
    void AddTransaction(InventoryTransaction transaction);
    void AddQuote(SalesOrderQuote quote);
    void AddHistory(OrderStatusHistory history);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
    void ClearTracking();
}

public interface IRealTimeAllocationService
{
    Task AllocateAsync(Guid orderId, Guid actorUserId, CancellationToken cancellationToken);
}

public sealed class RealTimeAllocationService(IRealTimeAllocationStore store, AllocationEngine engine, TimeProvider timeProvider)
    : IRealTimeAllocationService
{
    private static readonly TimeSpan DefaultTransit = TimeSpan.FromHours(24);

    public async Task AllocateAsync(Guid orderId, Guid actorUserId, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await store.ExecuteInTransactionAsync(token => AllocateOnceAsync(orderId, actorUserId, token), cancellationToken);
                store.ClearTracking();
                return;
            }
            catch (AllocationConcurrencyException) when (attempt < 3)
            {
                store.ClearTracking();
            }
        }

        throw new AllocationConcurrencyException("Allocation conflicted three times.");
    }

    private async Task AllocateOnceAsync(Guid orderId, Guid actorUserId, CancellationToken cancellationToken)
    {
        var context = await store.LoadForUpdateAsync(orderId, cancellationToken)
            ?? throw new InvalidOperationException("Sales order was not found for allocation.");
        if (context.Order.Status is OrderStatuses.Quoted or OrderStatuses.ConvertedToPo) return;

        var now = timeProvider.GetUtcNow();
        var run = new AllocationRun
        {
            Id = Guid.NewGuid(), RunCode = $"ALT-{context.Order.OrderCode}-{context.NextAttemptNumber:00}",
            SalesOrderId = context.Order.Id, AttemptNumber = context.NextAttemptNumber,
            Status = AllocationRunStatuses.Processing, AlgorithmSettings = "{\"mode\":\"fefo-weighted-greedy\"}",
            StartedAt = now, Version = 1, CreatedAt = now, UpdatedAt = now
        };
        store.AddRun(run);

        var selected = new List<(SalesOrderLine Line, AllocationDecision Decision)>();
        var remainingByBatch = context.Candidates.ToDictionary(item => item.Batch.Id, item => item.Balance.AvailableQuantity);
        foreach (var line in context.Lines.OrderBy(item => item.LineNumber))
        {
            var productCandidates = context.Candidates.Where(item => item.Product.Id == line.ProductId).ToArray();
            if (productCandidates.Length == 0)
            {
                await MarkOutOfStockAsync(context, run, actorUserId, now,
                    $"Không có tồn kho khả dụng cho {line.SkuSnapshot}", cancellationToken);
                return;
            }

            var candidates = productCandidates.Select(item =>
            {
                var distance = DistanceMeters(context.RetailerLocation, item);
                var eta = distance.HasValue ? now.Add(DefaultTransit) : (DateTimeOffset?)null;
                return new BatchCandidate(item.Batch.Id, item.Batch.BatchCode, item.Warehouse.Id, item.Warehouse.Code,
                    new DateTimeOffset(item.Batch.ExpirationDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                    remainingByBatch[item.Batch.Id], distance ?? 0, eta,
                    eta.HasValue ? DateOnly.FromDateTime(eta.Value.UtcDateTime) : null);
            }).ToArray();
            var result = engine.Allocate(new AllocationRequest(line.SkuSnapshot, line.OrderedQuantity,
                productCandidates[0].Product.ShelfLifeHours,
                line.MlorMinimumLifeRatio, context.Order.RequestedDeliveryDate), candidates);
            if (!result.IsFullyAllocated)
            {
                await MarkOutOfStockAsync(context, run, actorUserId, now,
                    $"Thiếu {result.ShortageQuantity:0.###} thùng cho {line.SkuSnapshot}", cancellationToken);
                return;
            }
            foreach (var decision in result.Allocations) { selected.Add((line, decision)); remainingByBatch[decision.BatchId] -= decision.Quantity; }
        }

        foreach (var group in selected.GroupBy(item => item.Decision.BatchId))
        {
            var candidate = context.Candidates.Single(item => item.Batch.Id == group.Key);
            var quantity = group.Sum(item => item.Decision.Quantity);
            if (candidate.Balance.AvailableQuantity < quantity) throw new AllocationConcurrencyException("Inventory changed during allocation.");
            candidate.Balance.ReservedQuantity += quantity;
            candidate.Balance.AvailableQuantity = candidate.Balance.PhysicalQuantity - candidate.Balance.ReservedQuantity;
            candidate.Balance.Version++; candidate.Balance.UpdatedAt = now;
        }

        foreach (var row in selected)
        {
            var candidate = context.Candidates.Single(item => item.Batch.Id == row.Decision.BatchId);
            var allocation = new Allocation
            {
                Id = Guid.NewGuid(), SalesOrderLineId = row.Line.Id, BatchId = row.Decision.BatchId,
                WarehouseId = row.Decision.WarehouseId, AllocatedQuantity = row.Decision.Quantity,
                AllocationSequence = row.Decision.Sequence, DistanceMeters = row.Decision.DistanceMeters,
                EstimatedArrivalAt = row.Decision.EstimatedArrivalAt,
                RemainingLifeHoursAtArrival = decimal.ToInt32(decimal.Truncate(row.Decision.RemainingShelfLifeHours)),
                WeightedScore = row.Decision.WeightedScore, UnitPrice = row.Line.UnitPrice,
                GrossAmount = row.Decision.Quantity * row.Line.UnitPrice, NetAmount = row.Decision.Quantity * row.Line.UnitPrice,
                Status = AllocationInventoryStatuses.Reserved, CreatedAt = now
            };
            store.AddAllocation(allocation);
            store.AddReservation(new Reservation { Id = Guid.NewGuid(), AllocationId = allocation.Id, BatchId = allocation.BatchId, Quantity = allocation.AllocatedQuantity, Status = ReservationStatuses.Active, ReservedAt = now });
            store.AddTransaction(new InventoryTransaction { Id = Guid.NewGuid(), BatchId = allocation.BatchId, WarehouseId = allocation.WarehouseId, ProductId = row.Line.ProductId, CreatedByUserId = actorUserId, TransactionType = InventoryTransactionTypes.OrderReserve, SourceType = InventoryTransactionSourceTypes.Allocation, SourceId = allocation.Id, ReferenceCode = run.RunCode, ReservedQuantityDelta = allocation.AllocatedQuantity, PhysicalQuantityAfter = candidate.Balance.PhysicalQuantity, ReservedQuantityAfter = candidate.Balance.ReservedQuantity, AvailableQuantityAfter = candidate.Balance.AvailableQuantity, Note = "Đặt trước tồn kho tự động theo SO", OccurredAt = now });
        }

        context.Order.Status = OrderStatuses.Quoted; context.Order.FinalPricePendingAllocation = false;
        context.Order.Version++; context.Order.UpdatedAt = now;
        run.Status = AllocationRunStatuses.Succeeded; run.CompletedAt = now; run.SnapshotCompletedAt = now; run.UpdatedAt = now;
        store.AddQuote(new SalesOrderQuote { Id = Guid.NewGuid(), SalesOrderId = context.Order.Id, AllocationRunId = run.Id, RevisionNumber = 1, Status = SalesOrderQuoteStatuses.Issued, TotalAmount = context.Order.FinalAmount, IssuedAt = now, ExpiresAt = now.AddHours(24), Version = 1, CreatedAt = now, UpdatedAt = now });
        store.AddHistory(History(context.Order.Id, OrderStatuses.Allocating, OrderStatuses.Quoted, actorUserId, "Phân bổ real-time thành công", now));
        await store.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkOutOfStockAsync(RealTimeAllocationContext context, AllocationRun run,
        Guid actorUserId, DateTimeOffset now, string reason, CancellationToken cancellationToken)
    {
        var fromStatus = context.Order.Status;
        run.Status = AllocationRunStatuses.OutOfStock;
        run.FailureReason = reason;
        run.CompletedAt = now;
        run.UpdatedAt = now;
        context.Order.Status = OrderStatuses.OutOfStock;
        context.Order.Version++;
        context.Order.UpdatedAt = now;
        store.AddHistory(History(context.Order.Id, fromStatus, OrderStatuses.OutOfStock, actorUserId, reason, now));
        await store.SaveChangesAsync(cancellationToken);
    }

    private static OrderStatusHistory History(Guid orderId, string from, string to, Guid actor, string? reason, DateTimeOffset now) => new() { Id = Guid.NewGuid(), SalesOrderId = orderId, FromStatus = from, ToStatus = to, ChangedByUserId = actor, Reason = reason, ChangedAt = now };
    private static decimal? DistanceMeters(MapLocation? retailer, RealTimeInventoryCandidate candidate)
    {
        if (retailer is null || !candidate.WarehouseLatitude.HasValue || !candidate.WarehouseLongitude.HasValue) return null;
        const double radius = 6_371_000; static double Radians(decimal value) => (double)value * Math.PI / 180d;
        var lat1 = Radians(retailer.Latitude); var lat2 = Radians(candidate.WarehouseLatitude.Value);
        var dLat = lat2 - lat1; var dLon = Radians(candidate.WarehouseLongitude.Value - retailer.Longitude);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return (decimal)(radius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)));
    }
}

public sealed class AllocationConcurrencyException(string message) : Exception(message);
