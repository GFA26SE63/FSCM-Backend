// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class SalesGroupMember
{
    public Guid Id { get; set; }
    public Guid SalesGroupId { get; set; }
    public Guid UserAccountId { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    public DateTimeOffset? UnassignedAt { get; set; }
    public bool IsActive { get; set; }
}

