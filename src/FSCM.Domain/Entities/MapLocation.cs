// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class MapLocation
{
    public Guid Id { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? RetailerId { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public string? FormattedAddress { get; set; }
    public string? ProviderPlaceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
