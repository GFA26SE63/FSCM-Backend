namespace FSCM.Domain.Entities;

public sealed class MapDistanceCache
{
    public Guid Id { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid RetailerId { get; set; }
    public Guid OriginLocationId { get; set; }
    public Guid DestinationLocationId { get; set; }
    public int OriginLocationVersion { get; set; }
    public int DestinationLocationVersion { get; set; }
    public int DistanceMeters { get; set; }
    public int DurationSeconds { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ProviderStatus { get; set; } = string.Empty;
    public int Version { get; set; }
    public DateTimeOffset CalculatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool IsActive { get; set; }
}
