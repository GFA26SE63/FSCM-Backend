namespace FSCM.Application.Features.Maps;

public sealed record GeocodeProviderResult(
    decimal Latitude,
    decimal Longitude,
    string FormattedAddress,
    string PlaceId,
    string ProviderStatus);

public sealed record RouteDistanceProviderResult(
    int DistanceMeters,
    int DurationSeconds,
    string ProviderStatus);

public sealed record MapLocationResult(
    Guid Id,
    string EntityType,
    Guid EntityId,
    decimal Latitude,
    decimal Longitude,
    string? FormattedAddress,
    string? ProviderPlaceId,
    string Provider,
    int Version,
    DateTimeOffset? NormalizedAt,
    DateTimeOffset UpdatedAt);

public sealed record MapDistanceResult(
    Guid Id,
    Guid WarehouseId,
    Guid RetailerId,
    int DistanceMeters,
    int DurationSeconds,
    string Provider,
    string ProviderStatus,
    int Version,
    int OriginLocationVersion,
    int DestinationLocationVersion,
    DateTimeOffset CalculatedAt,
    DateTimeOffset ExpiresAt,
    bool IsExpired);

public sealed record EtaEstimateResult(
    Guid WarehouseId,
    Guid RetailerId,
    DateTimeOffset DepartureAt,
    DateTimeOffset EstimatedReceiptAt,
    int DistanceMeters,
    int RouteDurationSeconds,
    int HandlingMinutes,
    int TransitMinutes,
    int BufferMinutes,
    int DistanceVersion,
    int TransitRuleVersion,
    bool IsEstimate,
    string Disclaimer);

public sealed record GeocodeTarget(string EntityType, Guid EntityId, string Address);
public sealed record EtaEstimateInput(Guid WarehouseId, Guid RetailerId, DateTimeOffset? DepartureAt, int BufferMinutes);
