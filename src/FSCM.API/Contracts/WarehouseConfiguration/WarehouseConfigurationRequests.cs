using System.Text.Json;

namespace FSCM.API.Contracts.WarehouseConfiguration;

public sealed record CreateWarehouseRequest(
    string Code, string Name, string Type, string Address,
    decimal Latitude, decimal Longitude, string? ProviderPlaceId);

public sealed record UpdateWarehouseRequest(
    string Name, string Type, string Address,
    decimal Latitude, decimal Longitude, string? ProviderPlaceId);

public sealed record UpdateWarehouseStatusRequest(bool IsActive);

public sealed record ReplaceTransitTimeRulesRequest(IReadOnlyCollection<TransitTimeRuleRequest> Rules);

public sealed record TransitTimeRuleRequest(
    int MinDistanceMeters, int MaxDistanceMeters, int HandlingMinutes,
    int TransitMinutes, int MaxServingDistanceMeters);

public sealed record SetMlorRequest(
    Guid RetailerId, Guid ProductId, decimal MinimumLifeRatio, int? MinimumLifeHours);

public sealed record UpdateBusinessConfigurationRequest(JsonElement Value);
