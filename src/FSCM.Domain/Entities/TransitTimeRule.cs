// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class TransitTimeRule
{
    public Guid Id { get; set; }
    public Guid WarehouseId { get; set; }
    public int MinDistanceMeters { get; set; }
    public int MaxDistanceMeters { get; set; }
    public int HandlingMinutes { get; set; }
    public int TransitMinutes { get; set; }
    public int MaxServingDistanceMeters { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

