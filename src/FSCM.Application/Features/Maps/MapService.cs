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

    public async Task<EtaEstimateResult> EstimateEtaAsync(EtaEstimateInput input, CancellationToken cancellationToken)
    {
        if (input.BufferMinutes is < 0 or > 1440 || input.ApprovalMinutes is < 0 or > 2880 ||
            input.PreparationMinutes is < 0 or > 2880)
            throw MapOperationException.Validation("ETA duration components are outside their accepted ranges.");
        var now = timeProvider.GetUtcNow();
        var distance = await store.FindActiveDistanceAsync(input.WarehouseId, input.RetailerId, false, cancellationToken)
            ?? throw MapOperationException.Conflict("No cached distance exists for this warehouse and Retailer.");
        if (distance.ExpiresAt <= now)
            throw MapOperationException.Conflict("The cached distance has expired and must be refreshed.");
        var origin = await RequireLocationAsync("warehouse", input.WarehouseId, cancellationToken);
        var destination = await RequireLocationAsync("retailer", input.RetailerId, cancellationToken);
        if (distance.OriginLocationVersion != origin.Version || distance.DestinationLocationVersion != destination.Version)
            throw MapOperationException.Conflict("The cached distance is stale because a location has changed.");
        var rule = await store.FindTransitRuleAsync(input.WarehouseId, distance.DistanceMeters, cancellationToken)
            ?? throw MapOperationException.Conflict("No active transit-time rule covers the cached distance.");
        if (distance.DistanceMeters > rule.MaxServingDistanceMeters)
            throw MapOperationException.Conflict("The Retailer is outside the warehouse serving distance.");

        var requestedAt = input.RequestedAt ?? now;
        if (requestedAt < now.AddMinutes(-5))
            throw MapOperationException.Validation("Requested time cannot be in the past.");
        var allocationRunAt = input.AllocationRunAt ?? requestedAt;
        if (allocationRunAt < requestedAt)
            throw MapOperationException.Validation("Allocation run time cannot be earlier than the requested time.");
        var preparationCompletedAt = allocationRunAt.AddMinutes(
            input.ApprovalMinutes + rule.HandlingMinutes + input.PreparationMinutes);
        var nextDispatchAt = input.NextDispatchAt ?? preparationCompletedAt;
        if (nextDispatchAt < preparationCompletedAt)
            throw MapOperationException.Validation("Next dispatch time cannot be earlier than preparation completion.");
        var estimated = nextDispatchAt.AddMinutes(rule.TransitMinutes + input.BufferMinutes);
        var waitForRunMinutes = WholeMinutes(allocationRunAt - requestedAt);
        var waitForDispatchMinutes = WholeMinutes(nextDispatchAt - preparationCompletedAt);
        return new EtaEstimateResult(input.WarehouseId, input.RetailerId, requestedAt, allocationRunAt,
            preparationCompletedAt, nextDispatchAt, estimated,
            distance.DistanceMeters, distance.DurationSeconds, waitForRunMinutes,
            input.ApprovalMinutes, rule.HandlingMinutes, input.PreparationMinutes,
            waitForDispatchMinutes, rule.TransitMinutes, input.BufferMinutes,
            rule.EstimatedTransportCost, distance.Version, rule.Version, true,
            "ETA chỉ là thời gian ước tính, không phải cam kết thời điểm giao hàng.");
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

    private static int WholeMinutes(TimeSpan duration) => checked((int)Math.Round(duration.TotalMinutes));

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
