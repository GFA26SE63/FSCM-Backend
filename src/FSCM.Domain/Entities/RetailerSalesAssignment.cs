// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class RetailerSalesAssignment
{
    public Guid Id { get; set; }
    public Guid RetailerId { get; set; }
    public Guid SalesGroupId { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    public DateTimeOffset? UnassignedAt { get; set; }
    public bool IsActive { get; set; }
}

