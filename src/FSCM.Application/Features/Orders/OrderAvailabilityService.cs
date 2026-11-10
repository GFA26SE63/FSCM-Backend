using FSCM.Application.Features.AllocationRuns;
using FSCM.Domain.Allocation;

namespace FSCM.Application.Features.Orders;

public sealed record OrderAvailabilityItem(Guid ProductId, string Sku, decimal EligibleQuantity);
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
            var mlor = await store.ResolveMlorAsync(retailerId, productId, now, cancellationToken);
            if (mlor is null)
            {
                items.Add(new(productId, product.Sku, 0));
                continue;
            }
            var positions = await store.ListCandidatesAsync(productId, cancellationToken);
            var candidates = positions.Select(position => {
                var inventory = new RealTimeInventoryCandidate(position.Batch, position.Balance,
                    position.Warehouse, product, position.WarehouseLatitude, position.WarehouseLongitude,
                    position.ActiveLockedQuantity);
                return RealTimeAllocationService.ToBatchCandidate(inventory, location, inventory.IssuableQuantity, now);
            }).ToArray();
            var result = engine.Allocate(new AllocationRequest(product.Sku, 1,
                product.ShelfLifeHours, mlor.MinimumLifeRatio, null), candidates);
            var eligibleIds = result.FilterTrace
                .Where(trace => trace.Stage == AllocationFilterStage.Delivery && trace.Passed)
                .Select(trace => trace.BatchId).ToHashSet();
            items.Add(new(productId, product.Sku,
                candidates.Where(candidate => eligibleIds.Contains(candidate.BatchId))
                    .Sum(candidate => candidate.AvailableQuantity)));
        }
        return new(now, items);
    }
}
