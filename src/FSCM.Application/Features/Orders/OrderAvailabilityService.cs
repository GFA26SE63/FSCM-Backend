using FSCM.Application.Features.AllocationRuns;
using FSCM.Domain.Allocation;
using FSCM.Domain.Authorization;

namespace FSCM.Application.Features.Orders;

public sealed record OrderWarehouseAvailability(
    Guid WarehouseId, string WarehouseCode, string WarehouseName,
    decimal EligibleQuantity, decimal? DistanceMeters, string? UnavailableReason);
public sealed record OrderAvailabilityItem(Guid ProductId, string Sku, decimal EligibleQuantity)
{
    public IReadOnlyCollection<OrderWarehouseAvailability> Warehouses { get; init; } = [];
}
public sealed record OrderAvailabilityResult(DateTimeOffset CheckedAt, IReadOnlyCollection<OrderAvailabilityItem> Items);

public sealed class OrderAvailabilityService(
    IOrderStore store, OrderService orders, AllocationEngine engine, TimeProvider timeProvider)
{
    public async Task<OrderAvailabilityResult> CheckAsync(
        Guid retailerId, IReadOnlyCollection<Guid> productIds,
        Guid actorId, string role, CancellationToken cancellationToken)
    {
        if (retailerId == Guid.Empty || productIds is null || productIds.Count is < 1 or > 100 ||
            productIds.Any(id => id == Guid.Empty))
            throw OrderOperationException.Validation("Availability requires a store and 1 to 100 product IDs.");
        await orders.EnsurePricingScopeAsync(retailerId, actorId, role, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var location = await store.FindRetailerLocationAsync(retailerId, cancellationToken);
        var items = new List<OrderAvailabilityItem>();
        foreach (var productId in productIds.Distinct())
        {
            var product = await store.FindPublishedProductAsync(productId, cancellationToken)
                ?? throw OrderOperationException.NotFound("Published product was not found.");
            var positions = await store.ListCandidatesAsync(productId, cancellationToken);
            var candidates = positions.Select(position => {
                var inventory = new RealTimeInventoryCandidate(position.Batch, position.Balance,
                    position.Warehouse, product, position.WarehouseLatitude, position.WarehouseLongitude,
                    position.ActiveLockedQuantity);
                return RealTimeAllocationService.ToBatchCandidate(inventory, location, inventory.IssuableQuantity, now);
            }).ToArray();
            var mlor = await store.ResolveMlorAsync(retailerId, productId, now, cancellationToken);
            if (mlor is null)
            {
                items.Add(new(productId, product.Sku, 0) { Warehouses = positions.GroupBy(p => p.Warehouse.Id)
                    .Select(g => new OrderWarehouseAvailability(g.Key, g.First().Warehouse.Code,
                        g.First().Warehouse.Name, 0,
                        candidates.FirstOrDefault(c => c.WarehouseId == g.Key && c.EstimatedArrivalAt.HasValue)?.DistanceMeters,
                        candidates.Where(c => c.WarehouseId == g.Key).All(engine.IsOutsideServiceRadius)
                            ? "OutsideServiceRadius" : "MissingMlor")).ToArray() });
                continue;
            }
            var result = engine.Allocate(new AllocationRequest(product.Sku, 1,
                product.ShelfLifeHours, mlor.MinimumLifeRatio, null), candidates);
            var eligibleIds = result.FilterTrace
                .Where(trace => trace.Stage == AllocationFilterStage.Delivery && trace.Passed)
                .Select(trace => trace.BatchId).ToHashSet();
            var warehouses = positions.GroupBy(p => p.Warehouse.Id).Select(group =>
            {
                var warehouse = group.First().Warehouse;
                var rows = candidates.Where(c => c.WarehouseId == group.Key).ToArray();
                var quantity = rows.Where(c => eligibleIds.Contains(c.BatchId)).Sum(c => c.AvailableQuantity);
                var failures = result.FilterTrace.Where(t => t.WarehouseId == group.Key && !t.Passed)
                    .Select(t => t.Reason).ToHashSet();
                var reason = quantity > 0 ? null
                    : rows.All(engine.IsOutsideServiceRadius) ? "OutsideServiceRadius"
                    : rows.All(c => c.AvailableQuantity <= 0) ? "NoIssuableStock"
                    : failures.Contains(AllocationFilterReason.OutsideServiceRadius) ? "OutsideServiceRadius"
                    : failures.Contains(AllocationFilterReason.RouteUnavailable) ? "RouteUnavailable"
                    : failures.Contains(AllocationFilterReason.BelowMlor) ? "BelowMlor" : "NoEligibleStock";
                return new OrderWarehouseAvailability(warehouse.Id, warehouse.Code, warehouse.Name,
                    quantity, rows.FirstOrDefault(c => c.EstimatedArrivalAt.HasValue)?.DistanceMeters, reason);
            }).OrderBy(w => w.EligibleQuantity > 0 ? 0 : 1).ThenBy(w => w.DistanceMeters ?? decimal.MaxValue)
                .ThenBy(w => w.WarehouseCode, StringComparer.Ordinal).ToArray();
            // Backward-compatible total for quantity validation; the UI receives each warehouse separately.
            items.Add(new(productId, product.Sku, warehouses.Sum(w => w.EligibleQuantity)) { Warehouses = warehouses });
        }
        return new(now, role == SystemRoles.Sales ? items :
            items.Select(item => item with { Warehouses = [] }).ToArray());
    }
}
