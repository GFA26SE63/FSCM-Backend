using FSCM.Application.Features.Maps;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Maps;

public sealed class MapServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Refresh_distance_versions_cache_and_never_uses_zero_fallback()
    {
        var fixture = new Fixture();
        fixture.Provider.Distance = new RouteDistanceProviderResult(12_500, 1_800, "ROUTE_EXISTS");

        var result = await fixture.Service.RefreshDistanceAsync(
            fixture.WarehouseId, fixture.RetailerId, CancellationToken.None);

        Assert.Equal(12_500, result.DistanceMeters);
        Assert.Equal(1, result.Version);
        Assert.Equal(Now.AddHours(24), result.ExpiresAt);

        fixture.Provider.Distance = new RouteDistanceProviderResult(0, 0, "NO_ROUTE");
        var exception = await Assert.ThrowsAsync<MapOperationException>(() => fixture.Service.RefreshDistanceAsync(
            fixture.WarehouseId, fixture.RetailerId, CancellationToken.None));
        Assert.Equal("map_provider_error", exception.Code);
    }

    private sealed class Fixture
    {
        public Guid WarehouseId { get; } = Guid.NewGuid();
        public Guid RetailerId { get; } = Guid.NewGuid();
        public FakeStore Store { get; }
        public FakeProvider Provider { get; } = new();
        public MapService Service { get; }

        public Fixture()
        {
            Store = new FakeStore(WarehouseId, RetailerId);
            Service = new MapService(Store, Provider, new FixedTimeProvider(Now));
        }
    }

    private sealed class FakeProvider : IMapProvider
    {
        public string Name => "GoogleMaps";
        public RouteDistanceProviderResult Distance { get; set; } = new(1_000, 300, "ROUTE_EXISTS");
        public Task<GeocodeProviderResult> GeocodeAsync(string address, CancellationToken cancellationToken) =>
            Task.FromResult(new GeocodeProviderResult(10.77m, 106.70m, address, "place-1", "OK"));
        public Task<RouteDistanceProviderResult> ComputeDistanceAsync(
            decimal originLatitude, decimal originLongitude, decimal destinationLatitude,
            decimal destinationLongitude, CancellationToken cancellationToken) => Task.FromResult(Distance);
    }

    private sealed class FakeStore : IMapStore
    {
        private readonly Guid _warehouseId;
        private readonly Guid _retailerId;
        public List<MapLocation> Locations { get; } = [];
        public List<MapDistanceCache> Distances { get; } = [];

        public FakeStore(Guid warehouseId, Guid retailerId)
        {
            _warehouseId = warehouseId;
            _retailerId = retailerId;
            Locations.Add(new MapLocation { Id = Guid.NewGuid(), WarehouseId = warehouseId, Latitude = 10.7m, Longitude = 106.6m, Version = 1 });
            Locations.Add(new MapLocation { Id = Guid.NewGuid(), RetailerId = retailerId, Latitude = 10.8m, Longitude = 106.7m, Version = 1 });
        }

        public Task<bool> WarehouseExistsAsync(Guid warehouseId, CancellationToken cancellationToken) => Task.FromResult(warehouseId == _warehouseId);
        public Task<bool> RetailerExistsAsync(Guid retailerId, CancellationToken cancellationToken) => Task.FromResult(retailerId == _retailerId);
        public Task<MapLocation?> FindLocationAsync(string entityType, Guid entityId, bool tracking, CancellationToken cancellationToken) =>
            Task.FromResult(Locations.SingleOrDefault(item => entityType == "warehouse" ? item.WarehouseId == entityId : item.RetailerId == entityId));
        public Task<MapDistanceCache?> FindActiveDistanceAsync(Guid warehouseId, Guid retailerId, bool tracking, CancellationToken cancellationToken) =>
            Task.FromResult(Distances.SingleOrDefault(item => item.WarehouseId == warehouseId && item.RetailerId == retailerId && item.IsActive));
        public Task<IReadOnlyCollection<MapDistanceCache>> ListDistancesAsync(Guid? warehouseId, Guid? retailerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<MapDistanceCache>>(Distances.Where(item => item.IsActive).ToArray());
        public void AddLocation(MapLocation location) => Locations.Add(location);
        public void AddDistance(MapDistanceCache distance) => Distances.Add(distance);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
