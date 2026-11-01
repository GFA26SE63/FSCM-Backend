// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

// One daily allocation run. BusinessDate is the Vietnam calendar day and is unique, so a retry or a
// second scheduler instance joins the existing run instead of starting a competing one.
public sealed class AllocationRun
{
    public Guid Id { get; set; }
    public string RunCode { get; set; } = string.Empty;
    public Guid SalesOrderId { get; set; }
    public int AttemptNumber { get; set; }
    public Guid? RetryOfAllocationRunId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public DateTimeOffset CutoffAt { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>Allocation weights and Dmax frozen at snapshot time, as JSON.</summary>
    public string AlgorithmSettings { get; set; } = "{}";

    public int TransitTableVersion { get; set; }
    public int EtaScheduleVersion { get; set; }
    public int OrderCount { get; set; }
    public string? FailureReason { get; set; }
    public string? TechnicalErrorCode { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? SnapshotCompletedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
