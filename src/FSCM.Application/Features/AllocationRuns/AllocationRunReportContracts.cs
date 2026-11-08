namespace FSCM.Application.Features.AllocationRuns;

/// <summary>What produced an exception, so a report can group them without parsing a reference.</summary>
public static class RunExceptionKinds
{
    public const string PickingSheet = "PickingSheet";
    public const string Shipment = "Shipment";
}

/// <summary>
/// An obligation that outlives the run. Closing a run must not make one unreachable, which is why
/// these are carried in the report rather than only in their own screens.
/// </summary>
public static class RunObligationKinds
{
    public const string Complaint = "Complaint";
    public const string Refund = "Refund";
}

public sealed record RunReportOrderRow(
    Guid SalesOrderId,
    string OrderCode,
    string RetailerName,
    int PriorityRank,

    /// <summary>What the run made of the order: Fulfilled, Short, or still Pending.</summary>
    string RunOrderStatus,

    /// <summary>Where the order itself stands now, which can have moved on long after the run.</summary>
    string OrderStatus,

    string? ShortageReason,
    decimal FinalAmount);

/// <summary>
/// One batch in one warehouse, with the plan's number beside what actually happened to it.
///
/// Held, issued, released and still-held are the four states a reservation can be in, so they sum
/// to what was held. A gap between planned and held means the plan was never fully reserved.
/// </summary>
public sealed record RunReportStockRow(
    Guid WarehouseId,
    string WarehouseCode,
    Guid BatchId,
    string BatchCode,
    string Sku,
    decimal PlannedQuantity,
    decimal HeldQuantity,
    decimal IssuedQuantity,
    decimal ReleasedQuantity,
    decimal StillHeldQuantity);

public sealed record RunReportException(
    string Kind,
    string Reference,
    string Detail,

    /// <summary>
    /// Whether the exception has been written down. An exception nobody recorded is the one thing
    /// that keeps a run open; one with a reason on it has been reconciled or accounted for.
    /// </summary>
    bool IsRecorded);

public sealed record RunReportObligation(
    string Kind,
    string Reference,
    string OrderCode,
    decimal? Amount,
    string Status);

/// <summary>
/// Everything a run did, in one read: which orders it served, what it drew from which batch, what
/// went wrong, and what is still owed to somebody (F95).
/// </summary>
public sealed record AllocationRunReport(
    Guid RunId,
    string RunCode,
    DateOnly BusinessDate,
    DateTimeOffset CutoffAt,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? SnapshotCompletedAt,
    DateTimeOffset? CompletedAt,
    int OrderCount,
    int FulfilledOrderCount,
    int ShortOrderCount,
    decimal PlannedQuantity,
    decimal IssuedQuantity,
    decimal StillHeldQuantity,
    IReadOnlyCollection<RunReportOrderRow> Orders,
    IReadOnlyCollection<RunReportStockRow> Stock,
    IReadOnlyCollection<RunReportException> Exceptions,
    IReadOnlyCollection<RunReportObligation> OpenObligations,

    /// <summary>
    /// Why the run cannot be closed yet, in words meant for the person looking at the report. Empty
    /// means it can be closed.
    /// </summary>
    IReadOnlyCollection<string> Blockers,

    DateTimeOffset? ClosedAt,
    Guid? ClosedByUserId,
    string? ClosureNote,
    int Version)
{
    public bool CanClose => Blockers.Count == 0;
}

public sealed record CloseAllocationRunData(int ExpectedVersion, string? Reason);
