using FSCM.Application.Features.AllocationRuns;
using FSCM.Application.Features.DeliveryEta;
using FSCM.Application.Features.Maps;
using FSCM.Application.Features.Orders;
using FSCM.Domain.Entities;
using FSCM.UnitTests.DeliveryEta;

namespace FSCM.UnitTests.AllocationRuns;

public sealed class AllocationPolicyServiceTests
{
    [Fact]
    public async Task Candidates_falls_back_to_stale_distance_when_map_provider_refresh_fails()
    {
        var etaFixture = await DeliveryEtaServiceTests.Fixture.CreateAsync();
        // Make the cached route expired so initial estimate throws StaleDistance
        etaFixture.Store.Distance.ExpiresAt = etaFixture.Store.Distance.ExpiresAt.AddDays(-2);

        var warehouse = new Warehouse
        {
            Id = etaFixture.WarehouseId,
            Code = "WH-TEST",
            Name = "Kho Test",
            IsActive = true
        };
        var batch = new Batch
        {
            Id = Guid.NewGuid(),
            BatchCode = "BATCH-001",
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3))
        };
        var position = new RealTimeInventoryCandidate(
            batch,
            new InventoryBalance { AvailableQuantity = 100, PhysicalQuantity = 100 },
            warehouse,
            new Product { Sku = "SKU001", Name = "Product 1", Unit = "Thùng" },
            10.7m,
            106.6m,
            0m);

        // Map provider that throws MapOperationException (simulating 503 / quota exhausted)
        var throwingMapProvider = new ThrowingMapProvider();
        var mapStore = new FakeMapStore(etaFixture.Store.Distance);
        var mapService = new MapService(mapStore, throwingMapProvider, TimeProvider.System);

        var policy = new AllocationPolicyService(null!, etaFixture.Service, mapService);

        var candidates = await policy.CandidatesAsync(
            etaFixture.RetailerId,
            [position],
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.Single(candidates);
        var candidate = candidates[0];
        Assert.NotNull(candidate.EstimatedArrivalAt);
        Assert.Equal(etaFixture.Store.Distance.DistanceMeters, candidate.DistanceMeters);
    }

    private sealed class ThrowingMapProvider : IMapProvider
    {
        public string Name => "VietMap";
        public Task<GeocodeProviderResult> GeocodeAsync(string address, CancellationToken cancellationToken) =>
            throw MapOperationException.Provider("VietMap quota is exhausted or the requested API is not enabled.");

        public Task<RouteDistanceProviderResult> ComputeDistanceAsync(
            decimal originLatitude, decimal originLongitude,
            decimal destinationLatitude, decimal destinationLongitude,
            CancellationToken cancellationToken) =>
            throw MapOperationException.Provider("VietMap quota is exhausted or the requested API is not enabled.");
    }

    private sealed class FakeMapStore(MapDistanceCache cachedDistance) : IMapStore
    {
        public Task<bool> WarehouseExistsAsync(Guid warehouseId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> RetailerExistsAsync(Guid retailerId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<MapLocation?> FindLocationAsync(string entityType, Guid entityId, bool tracking, CancellationToken cancellationToken) =>
            Task.FromResult<MapLocation?>(new MapLocation
            {
                Id = Guid.NewGuid(),
                WarehouseId = entityType == "warehouse" ? entityId : null,
                RetailerId = entityType == "retailer" ? entityId : null,
                Latitude = 10.7m,
                Longitude = 106.6m,
                Version = 1
            });

        public Task<MapDistanceCache?> FindActiveDistanceAsync(Guid warehouseId, Guid retailerId, bool tracking, CancellationToken cancellationToken) =>
            Task.FromResult<MapDistanceCache?>(cachedDistance);

        public Task<IReadOnlyCollection<MapDistanceCache>> ListDistancesAsync(Guid? warehouseId, Guid? retailerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<MapDistanceCache>>([cachedDistance]);

        public void AddLocation(MapLocation location) { }
        public void AddDistance(MapDistanceCache distance) { }
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
