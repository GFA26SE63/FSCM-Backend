using System.Text.Json;

namespace FSCM.API.Contracts.WarehouseConfiguration;

public sealed record CreateWarehouseRequest(
    string Code, string Name, Guid RegionId, string Address,
    decimal Latitude, decimal Longitude, string? ProviderPlaceId,
    int MinimumStockThresholdCartons);

public sealed record UpdateWarehouseRequest(
    string Name, Guid RegionId, string Address,
    decimal Latitude, decimal Longitude, string? ProviderPlaceId,
    int MinimumStockThresholdCartons);

public sealed record UpdateWarehouseStatusRequest(bool IsActive);

public sealed record SetMlorRequest(
    Guid RetailerId, Guid? ProductCategoryId, Guid? ProductId,
    decimal MinimumLifeRatio, int? MinimumLifeHours);

public sealed record UpdateBusinessConfigurationRequest(JsonElement Value);
