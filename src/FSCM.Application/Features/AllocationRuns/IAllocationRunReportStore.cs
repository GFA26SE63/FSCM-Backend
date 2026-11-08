using FSCM.Domain.Entities;

namespace FSCM.Application.Features.AllocationRuns;

/// <summary>Everything the run still has in flight, read in one pass so the report and the closing check agree.</summary>
public sealed record AllocationRunReportView(
    AllocationRun Run,
    IReadOnlyCollection<RunReportOrderRow> Orders,
    IReadOnlyCollection<RunReportStockRow> Stock,
    IReadOnlyCollection<RunReportException> Exceptions,
    IReadOnlyCollection<RunReportObligation> OpenObligations,

    /// <summary>Picking sheets whose work is not finished, by picking code.</summary>
    IReadOnlyCollection<string> UnfinishedPickingSheets,

    /// <summary>Shipments whose goods have not come to rest, by shipment code.</summary>
    IReadOnlyCollection<string> ShipmentsInFlight,

    /// <summary>How many of the run's reservations are still holding stock.</summary>
    int StillHeldReservationCount,

    AuditLog? Closure);

public interface IAllocationRunReportStore
{
    Task<AllocationRunReportView?> FindReportAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>The run itself, tracked, for the closing write.</summary>
    Task<AllocationRun?> FindRunForUpdateAsync(Guid runId, CancellationToken cancellationToken);

    void AddAuditLog(AuditLog log);

    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    void ClearTracking();
}
