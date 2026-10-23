using FSCM.Domain.Entities;

namespace FSCM.Application.Features.AllocationRuns;

public static class RunPickingListStatuses
{
    public const string Issued = "Issued";
    public const string Picking = "Picking";
    public const string Completed = "Completed";
}

public static class RunPickingGatherStatuses
{
    public const string Pending = "Pending";
    public const string Picked = "Picked";
}

/// <summary>One allocation of an approved plan, with everything a picking sheet needs to name it.</summary>
public sealed record PickableAllocation(
    AllocationRunAllocation Allocation,
    AllocationRunLine Line,
    Guid AllocationPlanPriceId,
    Guid SalesOrderId,
    Guid SalesOrderLineId,
    string OrderCode,
    string BatchCode);

public sealed record RunPickingSplitResult(
    Guid Id,
    Guid AllocationRunAllocationId,
    Guid AllocationPlanPriceId,
    Guid SalesOrderId,
    string OrderCode,
    decimal Quantity,
    decimal PickedQuantity);

public sealed record RunPickingGatherResult(
    Guid Id,
    Guid ProductId,
    string Sku,
    Guid BatchId,
    string BatchCode,
    decimal RequiredQuantity,
    decimal PickedQuantity,
    string Status,
    IReadOnlyCollection<RunPickingSplitResult> Splits);

public sealed record RunPickingListResult(
    Guid Id,
    string PickingCode,
    Guid AllocationRunId,
    Guid AllocationPlanVersionId,
    Guid WarehouseId,
    string Status,
    DateTimeOffset IssuedAt,
    int Version,
    IReadOnlyCollection<RunPickingGatherResult> Gathers);

/// <summary>
/// Issues the picking sheets for an approved plan, one per warehouse.
///
/// A sheet is organised the way the work actually happens: gather first, split second. One trip per
/// batch collects everything the run needs from it, and the split rows say which order each unit
/// belongs to. Picking per order would send someone to the same batch once per order.
///
/// Each split keeps the allocation and the approved price record it came from. A line served from
/// two batches can carry two different prices, so repricing at picking would quietly change what
/// the customer was told; nothing here recalculates anything.
///
/// Issuing moves no stock. The plan already holds it (F88); the goods leave on dispatch (F96).
/// </summary>
public sealed class RunPickingIssueService(IAllocationRunStore store, TimeProvider timeProvider)
{
    /// <summary>
    /// Issues every warehouse's sheet for an approved run. Re-running returns what already exists
    /// rather than printing a second set, because a warehouse working from two sheets for the same
    /// run would pick the same stock twice.
    /// </summary>
    public async Task<IReadOnlyCollection<RunPickingListResult>> IssueAsync(
        Guid runId, CancellationToken cancellationToken)
    {
        var run = await store.FindRunForUpdateAsync(runId, cancellationToken)
            ?? throw AllocationRunOperationException.NotFound("Allocation run was not found.");
        if (run.Status != AllocationRunStatuses.Approved)
        {
            throw AllocationRunOperationException.Conflict(
                $"Run {run.RunCode} is {run.Status}; only an approved run issues picking sheets.");
        }

        var existing = await store.ListRunPickingListsAsync(runId, cancellationToken);
        if (existing.Count > 0)
        {
            return existing.Select(ToResult).ToArray();
        }

        var approvedVersion = await store.FindApprovedPlanVersionAsync(runId, cancellationToken)
            ?? throw AllocationRunOperationException.Conflict(
                $"Run {run.RunCode} has no approved plan version.");

        await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            // Re-check inside the transaction: two callers can pass the check above together.
            if ((await store.ListRunPickingListsAsync(runId, transactionCancellationToken)).Count > 0) return;

            var allocations = await store.ListPickableAsync(approvedVersion.Id, transactionCancellationToken);
            if (allocations.Count == 0)
            {
                throw AllocationRunOperationException.Conflict(
                    $"Run {run.RunCode} allocated nothing, so there is nothing to pick.");
            }

            var now = timeProvider.GetUtcNow();
            var lists = new List<RunPickingList>();
            var gathers = new List<RunPickingGather>();
            var splits = new List<RunPickingSplit>();
            var warehouseIndex = 0;

            foreach (var perWarehouse in allocations
                         .GroupBy(item => item.Allocation.WarehouseId)
                         .OrderBy(group => group.Key))
            {
                warehouseIndex++;
                var list = new RunPickingList
                {
                    Id = Guid.NewGuid(),
                    PickingCode = $"PS-{run.RunCode}-{warehouseIndex:00}",
                    AllocationRunId = runId,
                    AllocationPlanVersionId = approvedVersion.Id,
                    WarehouseId = perWarehouse.Key,
                    Status = RunPickingListStatuses.Issued,
                    IssuedAt = now,
                    Version = 0,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                lists.Add(list);

                foreach (var perBatch in perWarehouse
                             .GroupBy(item => item.Allocation.BatchId)
                             .OrderBy(group => group.First().BatchCode, StringComparer.Ordinal))
                {
                    var sample = perBatch.First();
                    var gather = new RunPickingGather
                    {
                        Id = Guid.NewGuid(),
                        RunPickingListId = list.Id,
                        ProductId = sample.Line.ProductId,
                        SkuSnapshot = sample.Line.SkuSnapshot,
                        BatchId = perBatch.Key,
                        BatchCodeSnapshot = sample.BatchCode,
                        RequiredQuantity = perBatch.Sum(item => item.Allocation.Quantity),
                        PickedQuantity = 0m,
                        Status = RunPickingGatherStatuses.Pending,
                        CreatedAt = now
                    };
                    gathers.Add(gather);

                    splits.AddRange(perBatch
                        .OrderBy(item => item.OrderCode, StringComparer.Ordinal)
                        .Select(item => new RunPickingSplit
                        {
                            Id = Guid.NewGuid(),
                            RunPickingGatherId = gather.Id,
                            AllocationRunAllocationId = item.Allocation.Id,
                            AllocationRunLineId = item.Line.Id,
                            // The price approved for this part travels with it; picking never
                            // recalculates, so a line split across batches keeps both prices.
                            AllocationPlanPriceId = item.AllocationPlanPriceId,
                            SalesOrderId = item.SalesOrderId,
                            SalesOrderLineId = item.SalesOrderLineId,
                            OrderCodeSnapshot = item.OrderCode,
                            Quantity = item.Allocation.Quantity,
                            PickedQuantity = 0m,
                            CreatedAt = now
                        }));
                }
            }

            store.AddRunPickingLists(lists);
            store.AddRunPickingGathers(gathers);
            store.AddRunPickingSplits(splits);
            await store.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);

        store.ClearTracking();
        return (await store.ListRunPickingListsAsync(runId, cancellationToken)).Select(ToResult).ToArray();
    }

    /// <summary>Reads the sheets already issued for a run. Issues nothing.</summary>
    public async Task<IReadOnlyCollection<RunPickingListResult>> ListForRunAsync(
        Guid runId, CancellationToken cancellationToken) =>
        (await store.ListRunPickingListsAsync(runId, cancellationToken)).Select(ToResult).ToArray();

    public async Task<RunPickingListResult> GetAsync(Guid pickingListId, CancellationToken cancellationToken) =>
        ToResult(await store.FindRunPickingListAsync(pickingListId, cancellationToken)
            ?? throw AllocationRunOperationException.NotFound("Picking sheet was not found."));

    public async Task<IReadOnlyCollection<RunPickingListResult>> ListForWarehouseAsync(
        Guid warehouseId, CancellationToken cancellationToken) =>
        (await store.ListRunPickingListsForWarehouseAsync(warehouseId, cancellationToken))
            .Select(ToResult).ToArray();

    private static RunPickingListResult ToResult(RunPickingListView view) => new(
        view.List.Id,
        view.List.PickingCode,
        view.List.AllocationRunId,
        view.List.AllocationPlanVersionId,
        view.List.WarehouseId,
        view.List.Status,
        view.List.IssuedAt,
        view.List.Version,
        view.Gathers
            .OrderBy(gather => gather.BatchCodeSnapshot, StringComparer.Ordinal)
            .Select(gather => new RunPickingGatherResult(
                gather.Id,
                gather.ProductId,
                gather.SkuSnapshot,
                gather.BatchId,
                gather.BatchCodeSnapshot,
                gather.RequiredQuantity,
                gather.PickedQuantity,
                gather.Status,
                view.Splits
                    .Where(split => split.RunPickingGatherId == gather.Id)
                    .OrderBy(split => split.OrderCodeSnapshot, StringComparer.Ordinal)
                    .Select(split => new RunPickingSplitResult(
                        split.Id,
                        split.AllocationRunAllocationId,
                        split.AllocationPlanPriceId,
                        split.SalesOrderId,
                        split.OrderCodeSnapshot,
                        split.Quantity,
                        split.PickedQuantity))
                    .ToArray()))
            .ToArray());
}
