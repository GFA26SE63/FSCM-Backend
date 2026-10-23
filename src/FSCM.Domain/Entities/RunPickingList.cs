// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

// One warehouse's work for one run: everything that warehouse must pick, across every order in the
// run. Issued only after the plan was approved and its stock held.
public sealed class RunPickingList
{
    public Guid Id { get; set; }
    public string PickingCode { get; set; } = string.Empty;
    public Guid AllocationRunId { get; set; }
    public Guid AllocationPlanVersionId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
