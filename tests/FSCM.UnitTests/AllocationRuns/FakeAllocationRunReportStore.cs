using FSCM.Application.Features.AllocationRuns;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.AllocationRuns;

/// <summary>
/// In-memory stand-in for one run's report. The view is held whole rather than assembled from
/// pieces, because what the closing check reads is exactly what the report shows - letting a test
/// set one without the other would hide a disagreement between them.
/// </summary>
public sealed class FakeAllocationRunReportStore : IAllocationRunReportStore
{
    public AllocationRun? Run { get; set; }

    public IReadOnlyCollection<RunReportOrderRow> Orders { get; set; } = [];

    public IReadOnlyCollection<RunReportStockRow> Stock { get; set; } = [];

    public IReadOnlyCollection<RunReportException> Exceptions { get; set; } = [];

    public IReadOnlyCollection<RunReportObligation> OpenObligations { get; set; } = [];

    public IReadOnlyCollection<string> UnfinishedPickingSheets { get; set; } = [];

    public IReadOnlyCollection<string> ShipmentsInFlight { get; set; } = [];

    public int StillHeldReservationCount { get; set; }

    public AuditLog? Closure { get; set; }

    public List<AuditLog> Audits { get; } = [];

    public int SaveCount { get; private set; }

    public Task<AllocationRunReportView?> FindReportAsync(Guid runId, CancellationToken cancellationToken) =>
        Task.FromResult(Run is null || Run.Id != runId
            ? null
            : new AllocationRunReportView(
                Run, Orders, Stock, Exceptions, OpenObligations,
                UnfinishedPickingSheets, ShipmentsInFlight, StillHeldReservationCount, Closure));

    public Task<AllocationRun?> FindRunForUpdateAsync(Guid runId, CancellationToken cancellationToken) =>
        Task.FromResult(Run is not null && Run.Id == runId ? Run : null);

    public void AddAuditLog(AuditLog log)
    {
        Audits.Add(log);
        // The closed run reads its own audit row back, the way the real store does.
        Closure = log;
    }

    public Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action, CancellationToken cancellationToken) =>
        action(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    public void ClearTracking()
    {
    }
}
