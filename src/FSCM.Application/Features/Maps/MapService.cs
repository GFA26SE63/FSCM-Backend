using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Maps;

public sealed class MapService(IMapStore store, IMapProvider provider, TimeProvider timeProvider)
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);

    public async Task<MapLocationResult> GeocodeAsync(GeocodeTarget target, CancellationToken cancellationToken)
    {
        var entityType = NormalizeEntityType(target.EntityType);
        var address = target.Address?.Trim();
        if (string.IsNullOrWhiteSpace(address) || address.Length > 500)
            throw MapOperationException.Validation("Address is required and cannot exceed 500 characters.");
        await RequireEntityAsync(entityType, target.EntityId, cancellationToken);

        GeocodeProviderResult resolved;
        try { resolved = await provider.GeocodeAsync(address, cancellationToken); }
        catch (MapOperationException) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw MapOperationException.Provider("Google Maps geocoding is temporarily unavailable.");
        }

        ValidateCoordinates(resolved.Latitude, resolved.Longitude);
        var now = timeProvider.GetUtcNow();
        var location = await store.FindLocationAsync(entityType, target.EntityId, true, cancellationToken);
        if (location is null)
        {
            location = new MapLocation
            {
                Id = Guid.NewGuid(),
                WarehouseId = entityType == "warehouse" ? target.EntityId : null,
                RetailerId = entityType == "retailer" ? target.EntityId : null,
                Version = 1,
                CreatedAt = now
            };
            store.AddLocation(location);
        }
        else
        {
            location.Version++;
        }

        location.Latitude = resolved.Latitude;
        location.Longitude = resolved.Longitude;
        location.FormattedAddress = resolved.FormattedAddress;
        location.ProviderPlaceId = resolved.PlaceId;
        location.Provider = provider.Name;
        location.NormalizedAt = now;
        location.UpdatedAt = now;
        await store.SaveChangesAsync(cancellationToken);
        return ToLocationResult(location);
    }

    public async Task<MapLocationResult> GetLocationAsync(
        string entityType, Guid entityId, CancellationToken cancellationToken)
    {
        entityType = NormalizeEntityType(entityType);
        await RequireEntityAsync(entityType, entityId, cancellationToken);
        var location = await store.FindLocationAsync(entityType, entityId, false, cancellationToken)
            ?? throw MapOperationException.NotFound("Map location was not found.");
        return ToLocationResult(location);
    }

    public async Task<MapDistanceResult> RefreshDistanceAsync(
        Guid warehouseId, Guid retailerId, CancellationToken cancellationToken)
    {
        var origin = await RequireLocationAsync("warehouse", warehouseId, cancellationToken);
        var destination = await RequireLocationAsync("retailer", retailerId, cancellationToken);
        RouteDistanceProviderResult resolved;
        try
        {
            resolved = await provider.ComputeDistanceAsync(
                origin.Latitude, origin.Longitude, destination.Latitude, destination.Longitude, cancellationToken);
        }
        catch (MapOperationException) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw MapOperationException.Provider("Google Maps route distance is temporarily unavailable.");
        }
        if (resolved.DistanceMeters <= 0 || resolved.DurationSeconds <= 0)
            throw MapOperationException.Provider("Google Maps did not return a usable route; zero distance is not accepted.");

        var now = timeProvider.GetUtcNow();
        var previous = await store.FindActiveDistanceAsync(warehouseId, retailerId, true, cancellationToken);
        var version = (previous?.Version ?? 0) + 1;
        if (previous is not null) previous.IsActive = false;
        var distance = new MapDistanceCache
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouseId,
            RetailerId = retailerId,
            OriginLocationId = origin.Id,
            DestinationLocationId = destination.Id,
            OriginLocationVersion = origin.Version,
            DestinationLocationVersion = destination.Version,
            DistanceMeters = resolved.DistanceMeters,
            DurationSeconds = resolved.DurationSeconds,
            Provider = provider.Name,
            ProviderStatus = resolved.ProviderStatus,
            Version = version,
            CalculatedAt = now,
            ExpiresAt = now.Add(CacheLifetime),
            IsActive = true
        };
        store.AddDistance(distance);
        await store.SaveChangesAsync(cancellationToken);
        return ToDistanceResult(distance, now);
    }

    public async Task<IReadOnlyCollection<MapDistanceResult>> ListDistancesAsync(
        Guid? warehouseId, Guid? retailerId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        return (await store.ListDistancesAsync(warehouseId, retailerId, cancellationToken))
            .Select(item => ToDistanceResult(item, now)).ToArray();
    }

    private async Task<MapLocation> RequireLocationAsync(string entityType, Guid entityId, CancellationToken cancellationToken)
    {
        await RequireEntityAsync(entityType, entityId, cancellationToken);
        return await store.FindLocationAsync(entityType, entityId, false, cancellationToken)
            ?? throw MapOperationException.Conflict($"The {entityType} does not have a resolved map location.");
    }

    private async Task RequireEntityAsync(string entityType, Guid entityId, CancellationToken cancellationToken)
    {
        var exists = entityType == "warehouse"
            ? await store.WarehouseExistsAsync(entityId, cancellationToken)
            : await store.RetailerExistsAsync(entityId, cancellationToken);
        if (!exists) throw MapOperationException.NotFound($"The {entityType} was not found.");
    }

    private static string NormalizeEntityType(string value)
    {
        value = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return value is "warehouse" or "retailer" ? value
            : throw MapOperationException.Validation("Entity type must be warehouse or retailer.");
    }

    private static void ValidateCoordinates(decimal latitude, decimal longitude)
    {
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
            throw MapOperationException.Provider("Google Maps returned invalid coordinates.");
    }

    private static MapLocationResult ToLocationResult(MapLocation item) => new(
        item.Id, item.WarehouseId.HasValue ? "warehouse" : "retailer",
        item.WarehouseId ?? item.RetailerId!.Value, item.Latitude, item.Longitude,
        item.FormattedAddress, item.ProviderPlaceId, item.Provider, item.Version,
        item.NormalizedAt, item.UpdatedAt);

    private static MapDistanceResult ToDistanceResult(MapDistanceCache item, DateTimeOffset now) => new(
        item.Id, item.WarehouseId, item.RetailerId, item.DistanceMeters, item.DurationSeconds,
        item.Provider, item.ProviderStatus, item.Version, item.OriginLocationVersion,
        item.DestinationLocationVersion, item.CalculatedAt, item.ExpiresAt, item.ExpiresAt <= now);
}
