using System.Text.Json;
using FSCM.Application.Features.DeliveryEta;
using FSCM.Application.Features.Orders;
using FSCM.Domain.Allocation;
using FSCM.Application.Features.Maps;
using FSCM.Application.Features.TransitTables;

namespace FSCM.Application.Features.AllocationRuns;

/// <summary>The same published rules are used for availability, reservation and delivery rechecks.</summary>
public class AllocationPolicyService(IOrderStore store, DeliveryEtaService eta, MapService? maps = null)
{
    public virtual async Task<AllocationWeights> WeightsAsync(CancellationToken ct)
    {
        var weights = new AllocationWeights();
        if (await store.FindConfigurationAsync("allocation.weights", ct) is { } raw)
        {
            using var json = JsonDocument.Parse(raw);
            weights = weights with { ShelfLife = json.RootElement.GetProperty("expiry").GetDecimal(),
                Distance = json.RootElement.GetProperty("distance").GetDecimal(),
                Coverage = json.RootElement.GetProperty("coverage").GetDecimal() };
        }
        if (await store.FindConfigurationAsync("allocation.service_radius_meters", ct) is { } radius)
            weights = weights with { MaximumDistanceMeters = decimal.Parse(radius, System.Globalization.CultureInfo.InvariantCulture) };
        _ = new AllocationEngine(weights); // Validate published values before using them.
        return weights;
    }

    public virtual async Task<BatchCandidate[]> CandidatesAsync(Guid retailerId,
        IReadOnlyCollection<RealTimeInventoryCandidate> positions, DateTimeOffset at, CancellationToken ct)
    {
        var routes = new Dictionary<Guid, EtaResult?>();
        foreach (var warehouseId in positions.Select(p => p.Warehouse.Id).Distinct())
        {
            try { routes[warehouseId] = await eta.EstimateAsync(new(warehouseId, retailerId, at), ct); }
            catch (DeliveryEtaOperationException e) when (e.Code is "not_found" or "conflict")
            {
                routes[warehouseId] = null;
                if (maps is not null && e.CanRefreshDistance)
                {
                    try
                    {
                        await maps.RefreshDistanceAsync(warehouseId, retailerId, ct);
                        routes[warehouseId] = await eta.EstimateAsync(new(warehouseId, retailerId, at), ct);
                    }
                    catch (MapOperationException) { /* No verified route: never substitute a straight-line estimate. */ }
                    catch (DeliveryEtaOperationException retry) when (retry.Code is "not_found" or "conflict") { }
                    catch (TransitTableOperationException retry) when (retry.Code == "not_found") { }
                }
            }
            catch (TransitTableOperationException e) when (e.Code == "not_found") { routes[warehouseId] = null; }
        }
        return positions.Select(p =>
        {
            var route = routes[p.Warehouse.Id];
            var arrival = route is { IsWithinServingDistance: true } ? route.Breakdown.EstimatedArrivalAt : (DateTimeOffset?)null;
            return new BatchCandidate(p.Batch.Id, p.Batch.BatchCode, p.Warehouse.Id, p.Warehouse.Code,
                new DateTimeOffset(p.Batch.ExpirationDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                p.IssuableQuantity, route?.DistanceMeters ?? 0, arrival,
                arrival.HasValue ? DateOnly.FromDateTime(arrival.Value.ToOffset(TimeSpan.FromHours(7)).DateTime) : null);
        }).ToArray();
    }
}

public sealed class AllocationDeliveryGuard(IOrderStore store, AllocationPolicyService policy, TimeProvider clock)
{
    public async Task ValidateAsync(Guid orderId, CancellationToken ct)
    {
        var view = await store.FindAsync(orderId, false, ct)
            ?? throw OrderOperationException.NotFound("Không tìm thấy đơn để kiểm tra điều kiện giao.");
        var engine = new AllocationEngine(await policy.WeightsAsync(ct));
        foreach (var line in view.Lines)
        {
            if (line.Allocations.Count == 0) continue;
            var product = await store.FindPublishedProductAsync(line.Line.ProductId, ct)
                ?? throw OrderOperationException.Conflict("Sản phẩm không còn được phép giao.");
            var positions = await store.ListCandidatesAsync(product.Id, ct);
            var candidates = await policy.CandidatesAsync(view.Order.RetailerId,
                positions.Where(p => line.Allocations.Any(a => a.BatchId == p.Batch.Id))
                    .Select(p => new RealTimeInventoryCandidate(p.Batch, p.Balance, p.Warehouse, product,
                        p.WarehouseLatitude, p.WarehouseLongitude)).ToArray(), clock.GetUtcNow(), ct);
            foreach (var allocation in line.Allocations)
            {
                var candidate = candidates.SingleOrDefault(c => c.BatchId == allocation.BatchId);
                if (candidate is null || !engine.Allocate(new(line.Line.SkuSnapshot, allocation.AllocatedQuantity,
                        product.ShelfLifeHours, line.Line.MlorMinimumLifeRatio, view.Order.RequestedDeliveryDate),
                        [candidate with { AvailableQuantity = allocation.AllocatedQuantity }]).IsFullyAllocated)
                    throw OrderOperationException.Conflict($"Batch của {line.Line.SkuSnapshot} không còn đạt ETA/MLOR; cần xử lý lại trước khi thanh toán hoặc xuất kho.");
            }
        }
    }
}
