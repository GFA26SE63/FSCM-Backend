namespace FSCM.Application.Features.DeliveryEta;

/// <summary>
/// The published schedule the ETA is built from. Stored as the versioned business configuration
/// <c>eta.schedule</c>, so an estimate can name the schedule version it used.
/// </summary>
public sealed record EtaSchedule(
    string TimeZone,
    TimeOnly AllocationRunLocalTime,
    TimeOnly PlanApprovalDeadlineLocalTime,
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
