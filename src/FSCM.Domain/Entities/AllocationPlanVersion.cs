// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

// One proposal put to Admin: a complete set of allocations and their prices. A recalculation never
// edits a version in place, it supersedes it, so what an Admin approved stays exactly as approved.
public sealed class AllocationPlanVersion
{
    public Guid Id { get; set; }
    public Guid AllocationRunId { get; set; }
    public int VersionNumber { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>The shared moment every part of this version was priced from.</summary>
    public DateTimeOffset PricedAt { get; set; }

    public int FulfilledOrderCount { get; set; }
    public int ShortOrderCount { get; set; }
    public decimal TotalFinalAmount { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public Guid? RecalculationRequestedByUserId { get; set; }
    public DateTimeOffset? RecalculationRequestedAt { get; set; }
    public string? RecalculationReason { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
