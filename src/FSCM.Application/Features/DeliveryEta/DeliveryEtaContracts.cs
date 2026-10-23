namespace FSCM.Application.Features.DeliveryEta;

/// <summary>
/// The published schedule the ETA is built from. Stored as the versioned business configuration
/// <c>eta.schedule</c>, so an estimate can name the schedule version it used.
/// </summary>
public sealed record EtaSchedule(
    string TimeZone,

    /// <summary>Advice to Sales, not a gate. An order accepted after it and before the cutoff is
    /// still in the run; treating it as a deadline would drop valid orders.</summary>
    TimeOnly SalesSubmissionAdviceLocalTime,

    /// <summary>Reminder that orders still waiting for review will miss the cutoff.</summary>
    TimeOnly OrderReviewReminderLocalTime,

    /// <summary>The cutoff itself: the run takes the orders accepted and reviewed before it.</summary>
    TimeOnly AllocationRunLocalTime,

    /// <summary>Reminder that a proposal is still undecided.</summary>
    TimeOnly PlanApprovalReminderLocalTime,

    /// <summary>When a decision is expected. Nothing approves itself after it; a late decision has
    /// to be re-checked against the ETA, because the plan was estimated from the run moment.</summary>
    TimeOnly PlanApprovalDeadlineLocalTime,

    int PreparationMinutes,
    int BufferMinutes,
    IReadOnlyList<TimeOnly> DispatchSlotsLocalTime);

/// <summary>One point in the run day, resolved to an instant for a given business date.</summary>
public sealed record ScheduleMilestone(
    string Key,
    string Label,
    TimeOnly LocalTime,
    DateTimeOffset At,
    bool IsAdvisory);

/// <summary>
/// The published run-day timetable, with each milestone resolved for one business date so a portal
/// does not have to re-derive the time zone.
/// </summary>
public sealed record AllocationScheduleResult(
    DateOnly BusinessDate,
    string TimeZone,
    int ScheduleVersion,
    IReadOnlyList<ScheduleMilestone> Milestones,
    int PreparationMinutes,
    int BufferMinutes,
    IReadOnlyList<TimeOnly> DispatchSlotsLocalTime);

public sealed record EtaRequest(
    Guid WarehouseId,
    Guid RetailerId,
    DateTimeOffset? ReferenceAt);

/// <summary>
/// Every stage between accepting an order and the customer receiving it. Kept separate so an
/// allocation trace can show where the time goes instead of a single opaque timestamp.
/// </summary>
public sealed record EtaBreakdown(
    DateTimeOffset ReferenceAt,
    DateTimeOffset AllocationRunAt,
    DateTimeOffset PlanApprovedAt,
    DateTimeOffset PickingDoneAt,
    DateTimeOffset ReadyToLoadAt,
    DateTimeOffset DepartureAt,
    DateTimeOffset EstimatedArrivalAt,
    int WaitForRunMinutes,
    int ApprovalMinutes,
    int PreparationMinutes,
    int HandlingMinutes,
    int WaitForDispatchSlotMinutes,
    int TransitMinutes,
    int BufferMinutes);

public sealed record EtaResult(
    Guid WarehouseId,
    Guid RetailerId,
    int DistanceMeters,
    bool IsWithinServingDistance,
    decimal EstimatedCostAmount,
    EtaBreakdown Breakdown,
    int ScheduleVersion,
    int TransitTableVersion,
    int DistanceVersion,
    string Disclaimer)
{
    public DateTimeOffset EstimatedArrivalAt => Breakdown.EstimatedArrivalAt;
}
