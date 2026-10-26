using FSCM.Domain.Entities;

namespace FSCM.Application.Features.AllocationRuns;

/// <summary>
/// Admin's decision on a plan version. Goods and price are approved together: the batches chosen
/// are what set the near-expiry reduction, so approving one without the other would approve a
/// number that no longer belongs to anything.
///
/// Nothing here decides on its own. A deadline passing leaves the version waiting rather than
/// approving it, because an unattended approval would commit stock and money nobody looked at.
/// </summary>
public sealed class AllocationPlanApprovalService(
    IAllocationRunStore store,
    AllocationReservationService reservations,
    TimeProvider timeProvider)
{
    public async Task<AllocationPlanVersionResult> GetAsync(Guid versionId, CancellationToken cancellationToken) =>
        ToResult(await store.FindPlanVersionViewAsync(versionId, cancellationToken)
            ?? throw AllocationRunOperationException.NotFound("Plan version was not found."));

    public async Task<IReadOnlyCollection<AllocationPlanVersionResult>> ListAsync(
        Guid runId, CancellationToken cancellationToken) =>
        (await store.ListPlanVersionsAsync(runId, cancellationToken)).Select(ToResult).ToArray();

    /// <summary>
    /// Approves the plan: its allocations and its prices, as one decision. Only a version still
    /// awaiting a decision can be approved, so an approval sent against a proposal that a
    /// recalculation already replaced is refused rather than applied to stale numbers.
    /// </summary>
    public async Task<AllocationPlanVersionResult> ApproveAsync(
        Guid versionId, int expectedVersion, Guid adminUserId, CancellationToken cancellationToken)
    {
        var view = await store.FindPlanVersionViewAsync(versionId, cancellationToken)
            ?? throw AllocationRunOperationException.NotFound("Plan version was not found.");

        // Hours can pass between building the plan and deciding on it, so the hold happens first:
        // if live stock no longer supports the plan it is sent back to be rebuilt, and the version
        // is never marked approved on numbers that stopped being true.
        try
        {
            await reservations.ReserveAsync(
                versionId, adminUserId, view.RunCode, cancellationToken: cancellationToken);
        }
        catch (AllocationRunOperationException exception) when (exception.Code == "conflict")
        {
            await ReopenForRecalculationAsync(
                versionId, adminUserId, $"Giữ tồn thất bại: {exception.Message}", cancellationToken);
            throw AllocationRunOperationException.Conflict(
                $"{exception.Message} Phương án đã được trả về để tính lại.");
        }

        await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var version = await RequireLiveVersionAsync(versionId, expectedVersion, transactionCancellationToken);
            var now = timeProvider.GetUtcNow();
            version.Status = AllocationPlanVersionStatuses.Approved;
            version.ApprovedByUserId = adminUserId;
            version.ApprovedAt = now;
            version.Version++;
            version.UpdatedAt = now;

            var run = await store.FindRunForUpdateAsync(version.AllocationRunId, transactionCancellationToken)
                ?? throw AllocationRunOperationException.NotFound("Allocation run was not found.");
            run.Status = AllocationRunStatuses.Approved;
            run.Version++;
            run.UpdatedAt = now;
            await store.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);

        store.ClearTracking();
        return await GetAsync(versionId, cancellationToken);
    }

    /// <summary>
    /// Sends the plan back to be rebuilt, with a reason. The version is kept and marked rather than
    /// edited, so what Admin saw and rejected stays readable, and the run returns to a state where
    /// a fresh version can be built and put up for a new decision.
    /// </summary>
    public async Task<AllocationPlanVersionResult> RequestRecalculationAsync(
        Guid versionId, int expectedVersion, string reason, Guid adminUserId, CancellationToken cancellationToken)
    {
        reason = reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            throw AllocationRunOperationException.Validation("A recalculation needs a reason.");
        }

        if (reason.Length > 1_000)
        {
            throw AllocationRunOperationException.Validation("The reason cannot exceed 1000 characters.");
        }

        await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            await RequireLiveVersionAsync(versionId, expectedVersion, transactionCancellationToken);
            await MarkForRecalculationAsync(versionId, adminUserId, reason, transactionCancellationToken);
        }, cancellationToken);

        store.ClearTracking();
        return await GetAsync(versionId, cancellationToken);
    }

    /// <summary>Sends a version back without the expected-version check, for a failure we detected.</summary>
    private Task ReopenForRecalculationAsync(
        Guid versionId, Guid actorUserId, string reason, CancellationToken cancellationToken) =>
        store.ExecuteInTransactionAsync(
            token => MarkForRecalculationAsync(versionId, actorUserId, reason, token), cancellationToken);

    private async Task MarkForRecalculationAsync(
        Guid versionId, Guid actorUserId, string reason, CancellationToken cancellationToken)
    {
        var version = await store.FindPlanVersionForUpdateAsync(versionId, cancellationToken)
            ?? throw AllocationRunOperationException.NotFound("Plan version was not found.");
        if (version.Status != AllocationPlanVersionStatuses.AwaitingApproval) return;

        var now = timeProvider.GetUtcNow();
        version.Status = AllocationPlanVersionStatuses.RecalculationRequested;
        version.RecalculationRequestedByUserId = actorUserId;
        version.RecalculationRequestedAt = now;
        version.RecalculationReason = reason.Length > 1_000 ? reason[..1_000] : reason;
        version.Version++;
        version.UpdatedAt = now;

        var run = await store.FindRunForUpdateAsync(version.AllocationRunId, cancellationToken)
            ?? throw AllocationRunOperationException.NotFound("Allocation run was not found.");
        // Back to the snapshot so planning can run again over the same locked orders and pool.
        run.Status = AllocationRunStatuses.Snapshotted;
        run.CompletedAt = null;
        run.Version++;
        run.UpdatedAt = now;
        await store.SaveChangesAsync(cancellationToken);
    }

    private async Task<AllocationPlanVersion> RequireLiveVersionAsync(
        Guid versionId, int expectedVersion, CancellationToken cancellationToken)
    {
        var version = await store.FindPlanVersionForUpdateAsync(versionId, cancellationToken)
            ?? throw AllocationRunOperationException.NotFound("Plan version was not found.");
        if (version.Status != AllocationPlanVersionStatuses.AwaitingApproval)
        {
            throw AllocationRunOperationException.Conflict(
                $"Plan version {version.VersionNumber} is {version.Status} and can no longer be decided.");
        }

        if (version.Version != expectedVersion)
        {
            throw AllocationRunOperationException.Conflict(
                "The plan version changed since it was read; reload it before deciding.");
        }

        return version;
    }

    private static AllocationPlanVersionResult ToResult(AllocationPlanVersionView view) => new(
        view.PlanVersion.Id,
        view.PlanVersion.AllocationRunId,
        view.RunCode,
        view.PlanVersion.VersionNumber,
        view.PlanVersion.Status,
        view.PlanVersion.PricedAt,
        view.PlanVersion.FulfilledOrderCount,
        view.PlanVersion.ShortOrderCount,
        view.PlanVersion.TotalFinalAmount,
        view.PlanVersion.ApprovedByUserId,
        view.PlanVersion.ApprovedAt,
        view.PlanVersion.RecalculationReason,
        view.PlanVersion.Version,
        view.Prices.Select(price => new AllocationPlanPriceResult(
            price.AllocationRunLineId,
            price.BatchId,
            price.WarehouseId,
            price.Quantity,
            price.BaseNetUnitPrice,
            price.RemainingShelfLifePercent,
            price.ShelfLifeDiscountTierId,
            price.DiscountType,
            price.DiscountValue,
            price.ShelfLifeDiscountPerUnit,
            price.FinalNetUnitPrice,
            price.Amount,
            price.DiscountTableVersion,
            price.Explanation)).ToArray(),
        view.Allocations
            .OrderBy(item => item.Allocation.Sequence)
            .Select(item => new AllocationPlanAllocationResult(
                item.Allocation.AllocationRunLineId,
                item.Allocation.BatchId,
                item.BatchCode,
                item.Allocation.WarehouseId,
                item.WarehouseCode,
                item.Allocation.Quantity,
                item.Allocation.Sequence,
                item.Allocation.EstimatedArrivalAt,
                item.Allocation.RemainingShelfLifeHours,
                item.Allocation.WeightedScore))
            .ToArray());
}
