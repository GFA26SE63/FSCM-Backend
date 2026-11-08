using System.Globalization;
using System.Text.Json;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.AllocationRuns;

/// <summary>
/// The run's own account of itself, and the decision to close it (F95).
///
/// A run closes when nothing about its <em>goods</em> can still move: every sheet is finished, every
/// shipment has come to rest, and no stock is still held in the run's name. That is the line,
/// because those are the numbers the report states - closing while any of them can still change
/// would publish a total that is not final.
///
/// Money is deliberately on the other side of that line. An open complaint or an unpaid refund
/// changes what is owed, not what the run moved, and both outlive the run by design. They are
/// carried in the report so closing cannot make one unreachable, and they never block.
///
/// Closing writes no history away. The status moves and an audit row records who closed it and why;
/// every order, allocation, price, reservation and exception row stays exactly as it was.
/// </summary>
public sealed class AllocationRunClosureService(IAllocationRunReportStore store, TimeProvider timeProvider)
{
    /// <summary>
    /// The action name the first closing shipped with. Kept so rows already written stay readable
    /// and the Portal keeps finding them.
    /// </summary>
    public const string ClosureAction = "AllocationRunClosed";
    public const string ClosureEntityName = nameof(AllocationRun);

    public async Task<AllocationRunReport> GetReportAsync(Guid runId, CancellationToken cancellationToken) =>
        Build(await store.FindReportAsync(runId, cancellationToken)
              ?? throw AllocationRunOperationException.NotFound("Allocation run was not found."));

    /// <summary>
    /// Closes the run, once its report has nothing left blocking it.
    ///
    /// Re-sending the same decision returns the closed run rather than refusing: a double click on
    /// a report that took a moment to load should not read as an error.
    /// </summary>
    public async Task<AllocationRunReport> CloseAsync(
        Guid runId,
        CloseAllocationRunData data,
        Guid actorUserId,
        string actorRole,
        CancellationToken cancellationToken)
    {
        if (actorRole is not (SystemRoles.Admin or SystemRoles.Operator))
        {
            throw AllocationRunOperationException.Forbidden(
                "Only Admin or Operator can close an allocation run.");
        }

        var note = data.Reason?.Trim();
        if (note?.Length > 1_000)
        {
            throw AllocationRunOperationException.Validation("The closing note cannot exceed 1000 characters.");
        }

        var report = await GetReportAsync(runId, cancellationToken);
        if (report.Status == AllocationRunStatuses.Closed) return report;
        if (!report.CanClose)
        {
            throw AllocationRunOperationException.Conflict(
                $"Đợt {report.RunCode} chưa đóng được: {string.Join(" ", report.Blockers)}");
        }

        await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var run = await store.FindRunForUpdateAsync(runId, transactionCancellationToken)
                ?? throw AllocationRunOperationException.NotFound("Allocation run was not found.");
            if (run.Status == AllocationRunStatuses.Closed) return;
            if (run.Status != AllocationRunStatuses.Approved)
            {
                throw AllocationRunOperationException.Conflict(
                    $"Đợt {run.RunCode} đang {run.Status}; chỉ đợt đã duyệt mới đóng được.");
            }

            if (run.Version != data.ExpectedVersion)
            {
                throw AllocationRunOperationException.Conflict(
                    "The run changed since the report was read; reload it before closing.");
            }

            var now = timeProvider.GetUtcNow();
            run.Status = AllocationRunStatuses.Closed;
            run.Version++;
            run.UpdatedAt = now;

            // The closing is recorded rather than stored on the run: who closed it and why is the
            // same kind of fact as every other decision in this system, and it needs no column.
            store.AddAuditLog(new AuditLog
            {
                Id = Guid.NewGuid(),
                UserAccountId = actorUserId,
                EntityName = ClosureEntityName,
                EntityId = run.Id,
                Action = ClosureAction,
                OldValues = JsonSerializer.Serialize(new { Status = AllocationRunStatuses.Approved }),
                NewValues = JsonSerializer.Serialize(new { Status = AllocationRunStatuses.Closed }),
                Reason = note,
                CreatedAt = now
            });

            await store.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);

        store.ClearTracking();
        return await GetReportAsync(runId, cancellationToken);
    }

    private static AllocationRunReport Build(AllocationRunReportView view)
    {
        var run = view.Run;
        return new AllocationRunReport(
            run.Id,
            run.RunCode,
            run.BusinessDate,
            run.CutoffAt,
            run.Status,
            run.StartedAt,
            run.SnapshotCompletedAt,
            run.CompletedAt,
            run.OrderCount,
            view.Orders.Count(row => row.RunOrderStatus == AllocationRunOrderStatuses.Fulfilled),
            view.Orders.Count(row => row.RunOrderStatus == AllocationRunOrderStatuses.Short),
            view.Stock.Sum(row => row.PlannedQuantity),
            view.Stock.Sum(row => row.IssuedQuantity),
            view.Stock.Sum(row => row.StillHeldQuantity),
            view.Orders,
            view.Stock,
            view.Exceptions,
            view.OpenObligations,
            Blockers(view),
            view.Closure?.CreatedAt,
            view.Closure?.UserAccountId,
            view.Closure?.Reason,
            run.Version);
    }

    /// <summary>
    /// Why the run is not finished, in the order someone would want to read it.
    ///
    /// Each line names what to go and look at, because "the run cannot be closed" with no reason is
    /// the kind of message that gets a report ignored.
    /// </summary>
    private static IReadOnlyCollection<string> Blockers(AllocationRunReportView view)
    {
        var blockers = new List<string>();
        var run = view.Run;
        if (run.Status == AllocationRunStatuses.Closed) return blockers;
        if (run.Status != AllocationRunStatuses.Approved)
        {
            blockers.Add($"Đợt đang {run.Status}; chỉ đợt đã duyệt mới đóng được.");
            // The rest would be noise on a run that has not even been decided yet.
            return blockers;
        }

        if (view.UnfinishedPickingSheets.Count > 0)
        {
            blockers.Add($"Còn {view.UnfinishedPickingSheets.Count} phiếu soạn chưa xong " +
                         $"({Names(view.UnfinishedPickingSheets)}).");
        }

        if (view.ShipmentsInFlight.Count > 0)
        {
            blockers.Add($"Còn {view.ShipmentsInFlight.Count} phần giao chưa về đích " +
                         $"({Names(view.ShipmentsInFlight)}).");
        }

        if (view.StillHeldReservationCount > 0)
        {
            var quantity = view.Stock.Sum(row => row.StillHeldQuantity);
            blockers.Add($"Còn {quantity:0.###} đang giữ tồn trên " +
                         $"{view.StillHeldReservationCount} dòng phân bổ: hàng phải xuất đi hoặc " +
                         "được nhả lại trước khi đóng đợt.");
        }

        var unrecorded = view.Exceptions.Where(item => !item.IsRecorded).ToArray();
        if (unrecorded.Length > 0)
        {
            blockers.Add($"Còn {unrecorded.Length} ngoại lệ chưa ghi nhận xử lý " +
                         $"({Names(unrecorded.Select(item => item.Reference).ToArray())}).");
        }

        return blockers;
    }

    /// <summary>Lists a few names and says how many more there are, so a message stays readable.</summary>
    private static string Names(IReadOnlyCollection<string> references)
    {
        const int shown = 3;
        var head = string.Join(", ", references.Take(shown));
        var rest = references.Count - shown;
        return rest > 0
            ? $"{head} và {rest.ToString(CultureInfo.InvariantCulture)} cái nữa"
            : head;
    }
}
