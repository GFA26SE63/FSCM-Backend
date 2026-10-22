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

    [Fact]
    public async Task Eta_uses_distance_and_transit_versions_and_is_explicitly_an_estimate()
    {
        var fixture = new Fixture();
        fixture.Store.Distances.Add(new MapDistanceCache
        {
            Id = Guid.NewGuid(),
            WarehouseId = fixture.WarehouseId,
            RetailerId = fixture.RetailerId,
            OriginLocationId = fixture.Store.Locations[0].Id,
            DestinationLocationId = fixture.Store.Locations[1].Id,
            OriginLocationVersion = 1,
            DestinationLocationVersion = 1,
            DistanceMeters = 12_500,
            DurationSeconds = 1_800,
            Provider = "GoogleMaps",
            ProviderStatus = "ROUTE_EXISTS",
            Version = 3,
            CalculatedAt = Now.AddHours(-1),
            ExpiresAt = Now.AddHours(23),
            IsActive = true
        });
        fixture.Store.TransitRules.Add(new TransitTimeRule
        {
            Id = Guid.NewGuid(),
            WarehouseId = fixture.WarehouseId,
            MinDistanceMeters = 0,
            MaxDistanceMeters = 20_000,
            MaxServingDistanceMeters = 20_000,
            HandlingMinutes = 60,
            TransitMinutes = 120,
            EstimatedTransportCost = 180_000m,
            Version = 4,
            IsActive = true
        });

        var result = await fixture.Service.EstimateEtaAsync(
            new EtaEstimateInput(fixture.WarehouseId, fixture.RetailerId, Now,
                Now.AddHours(4), 45, 30, Now.AddHours(7), 30), CancellationToken.None);

        Assert.True(result.IsEstimate);
        Assert.Equal(3, result.DistanceVersion);
        Assert.Equal(4, result.TransitRuleVersion);
        Assert.Equal(240, result.WaitForRunMinutes);
        Assert.Equal(45, result.ApprovalMinutes);
        Assert.Equal(45, result.WaitForDispatchMinutes);
        Assert.Equal(180_000m, result.EstimatedTransportCost);
        Assert.Equal(Now.AddHours(9.5), result.EstimatedReceiptAt);
        Assert.Contains("không phải cam kết", result.Disclaimer);
    }

    [Fact]
    public async Task Eta_rejects_cache_after_location_version_changes()
    {
        var fixture = new Fixture();
        fixture.Store.Distances.Add(new MapDistanceCache
        {
            WarehouseId = fixture.WarehouseId,
            RetailerId = fixture.RetailerId,
            OriginLocationVersion = 1,
            DestinationLocationVersion = 1,
            DistanceMeters = 1_000,
            DurationSeconds = 300,
            Version = 1,
            ExpiresAt = Now.AddHours(1),
            IsActive = true
        });
        fixture.Store.Locations[1].Version = 2;

        var exception = await Assert.ThrowsAsync<MapOperationException>(() => fixture.Service.EstimateEtaAsync(
            new EtaEstimateInput(fixture.WarehouseId, fixture.RetailerId, Now,
                Now, 0, 0, Now, 0), CancellationToken.None));

        Assert.Equal("conflict", exception.Code);
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
        public List<TransitTimeRule> TransitRules { get; } = [];

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
        public Task<TransitTimeRule?> FindTransitRuleAsync(Guid warehouseId, int distanceMeters, CancellationToken cancellationToken) =>
            Task.FromResult(TransitRules.SingleOrDefault(item => item.WarehouseId == warehouseId && item.IsActive &&
                item.MinDistanceMeters <= distanceMeters && distanceMeters <= item.MaxDistanceMeters));
        public void AddLocation(MapLocation location) => Locations.Add(location);
        public void AddDistance(MapDistanceCache distance) => Distances.Add(distance);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
