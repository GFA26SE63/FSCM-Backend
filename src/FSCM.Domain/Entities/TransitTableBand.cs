// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

// One distance band of a transit table version. MinDistanceMeters is inclusive and
// MaxDistanceMeters is exclusive, so adjacent bands meet without overlapping.
public sealed class TransitTableBand
{
    public Guid Id { get; set; }
    public Guid TransitTableId { get; set; }
    public int MinDistanceMeters { get; set; }
    public int MaxDistanceMeters { get; set; }
    public int HandlingMinutes { get; set; }
    public int TransitMinutes { get; set; }
    public decimal EstimatedCostAmount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
