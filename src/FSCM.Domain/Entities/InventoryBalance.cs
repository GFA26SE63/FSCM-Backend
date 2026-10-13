// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class InventoryBalance
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public decimal PhysicalQuantity { get; set; }
    public decimal HeldQuantity { get; set; }
    public decimal ReservedQuantity { get; set; }
    public decimal AvailableQuantity { get; set; }
    public int Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
