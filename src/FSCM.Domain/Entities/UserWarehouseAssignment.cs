// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class UserWarehouseAssignment
{
    public Guid Id { get; set; }
    public Guid UserAccountId { get; set; }
    public Guid WarehouseId { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    public bool IsActive { get; set; }
}
