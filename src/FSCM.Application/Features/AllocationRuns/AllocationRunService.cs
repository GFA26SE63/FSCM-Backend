using System.Text.Json;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.AllocationRuns;

/// <summary>
/// Closes the daily allocation run at the cutoff: it locks the orders that may compete, freezes the
/// stock pool and the settings they will be scored against, and leaves the plan itself to batch
/// selection (F33), batch swapping (F86) and pricing (F89).
///
/// Nothing here touches a real inventory balance. The pool is the run's own copy, so a plan can be
/// built, discarded and rebuilt without ever committing stock; only run approval reserves (F88).
/// </summary>
public sealed class AllocationRunService(IAllocationRunStore store, TimeProvider timeProvider)
{
    private static readonly TimeZoneInfo VietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    /// <summary>
    /// Starts the run for <paramref name="cutoffAt"/>'s business day, or returns the existing one.
    /// Re-running the same day is a no-op rather than a second run, so a retry, a restarted
    /// scheduler or a manual trigger cannot split one day's orders across two plans.
    /// </summary>
    public async Task<AllocationRunResult> StartAsync(DateTimeOffset cutoffAt, CancellationToken cancellationToken)
    {
        var businessDate = ToBusinessDate(cutoffAt);
        var existing = await store.FindViewByBusinessDateAsync(businessDate, cancellationToken);
        if (existing is not null)
        {
            return ToResult(existing);
        }

        var now = timeProvider.GetUtcNow();
        await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            // Re-check inside the transaction: two schedulers can reach the check above together.
            if (await store.FindByBusinessDateAsync(businessDate, transactionCancellationToken) is not null)
            {
                return;
            }

            var settings = await store.GetSettingsAsync(transactionCancellationToken);
            var eligible = (await store.ListEligibleOrdersAsync(cutoffAt, transactionCancellationToken))
                // S06: earlier server acceptance wins; the order code breaks a tie so the queue is
                // deterministic when two orders land in the same instant.
                .OrderBy(item => item.Order.ServerReceivedAt)
                .ThenBy(item => item.Order.OrderCode, StringComparer.Ordinal)
                .ToArray();

            var run = new AllocationRun
            {
                Id = Guid.NewGuid(),
                RunCode = await store.NextRunCodeAsync(businessDate, transactionCancellationToken),
                BusinessDate = businessDate,
                CutoffAt = cutoffAt,
                Status = AllocationRunStatuses.Snapshotted,
                AlgorithmSettings = JsonSerializer.Serialize(settings),
                TransitTableVersion = 0,
                EtaScheduleVersion = 0,
                OrderCount = eligible.Length,
                StartedAt = now,
                SnapshotCompletedAt = now,
                Version = 0,
                CreatedAt = now,
                UpdatedAt = now
            };
            store.AddRun(run);

            var mlorPairs = eligible
                .SelectMany(order => order.Lines.Select(line => (order.Order.RetailerId, line.Line.ProductId)))
                .Distinct()
                .ToArray();
            var mlor = await store.ResolveMlorAsync(mlorPairs, transactionCancellationToken);

            var runOrders = new List<AllocationRunOrder>();
            var runLines = new List<AllocationRunLine>();
            for (var index = 0; index < eligible.Length; index++)
            {
                var candidate = eligible[index];
                var runOrder = new AllocationRunOrder
                {
                    Id = Guid.NewGuid(),
                    AllocationRunId = run.Id,
                    SalesOrderId = candidate.Order.Id,
                    OrderCodeSnapshot = candidate.Order.OrderCode,
                    RetailerId = candidate.Order.RetailerId,
                    AcceptedAt = candidate.Order.ServerReceivedAt,
                    DeliveryDeadline = candidate.Order.RequestedDeliveryDate,
                    PriorityRank = index + 1,
                    Status = AllocationRunOrderStatuses.Pending,
                    CreatedAt = now
                };
                runOrders.Add(runOrder);

                foreach (var line in candidate.Lines.OrderBy(item => item.Line.LineNumber))
                {
                    var key = (candidate.Order.RetailerId, line.Line.ProductId);
                    var resolved = mlor.TryGetValue(key, out var value)
                        ? value
                        : throw AllocationRunOperationException.Conflict(
                            $"No MLOR could be resolved for order {candidate.Order.OrderCode} line {line.Line.LineNumber}.");

                    runLines.Add(new AllocationRunLine
                    {
                        Id = Guid.NewGuid(),
                        AllocationRunOrderId = runOrder.Id,
                        SalesOrderLineId = line.Line.Id,
                        ProductId = line.Line.ProductId,
                        SkuSnapshot = line.Line.SkuSnapshot,
                        LineNumber = line.Line.LineNumber,
                        OrderedQuantity = line.Line.OrderedQuantity,
                        TotalShelfLifeHours = line.Product.ShelfLifeHours,
                        EffectiveMlorRatio = resolved.Ratio,
                        MlorSource = resolved.Source,
                        // Base price after ordinary promotion; the near-expiry reduction is applied
                        // after batch selection (F89), never here.
                        BaseNetUnitPrice = line.Line.OrderedQuantity == 0
                            ? 0m
                            : line.Line.NetAmount / line.Line.OrderedQuantity,
                        AllocatedQuantity = 0m,
                        CreatedAt = now
                    });
                }
            }

            store.AddOrders(runOrders);
            store.AddLines(runLines);

            // Demand is gathered by SKU only to read candidates once; every line keeps its own MLOR,
            // deadline and rank, so grouping never merges two orders' conditions.
            var productIds = runLines.Select(item => item.ProductId).Distinct().ToArray();
            var available = productIds.Length == 0
                ? []
                : await store.ListAvailableStockAsync(productIds, businessDate, transactionCancellationToken);

            store.AddPool(available.Select(item => new AllocationRunStock
            {
                Id = Guid.NewGuid(),
                AllocationRunId = run.Id,
                BatchId = item.Batch.Id,
                WarehouseId = item.WarehouseId,
                ProductId = item.Batch.ProductId,
                ExpirationDate = item.Batch.ExpirationDate,
                SnapshotQuantity = item.AvailableQuantity,
                RemainingQuantity = item.AvailableQuantity,
                InventoryVersion = item.InventoryVersion,
                CreatedAt = now
            }));

            await store.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);

        store.ClearTracking();
        return await GetByBusinessDateAsync(businessDate, cancellationToken);
    }

    public async Task<AllocationRunResult> GetAsync(Guid runId, CancellationToken cancellationToken) =>
        ToResult(await store.FindViewAsync(runId, cancellationToken)
            ?? throw AllocationRunOperationException.NotFound("Allocation run was not found."));

    public async Task<AllocationRunResult> GetByBusinessDateAsync(
        DateOnly businessDate, CancellationToken cancellationToken) =>
        ToResult(await store.FindViewByBusinessDateAsync(businessDate, cancellationToken)
            ?? throw AllocationRunOperationException.NotFound(
                $"No allocation run exists for {businessDate:yyyy-MM-dd}."));

    /// <summary>The Vietnam calendar day a cutoff belongs to, so a run is one per local day.</summary>
    public static DateOnly ToBusinessDate(DateTimeOffset moment) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, VietnamTimeZone).DateTime);

    private static AllocationRunResult ToResult(AllocationRunView view)
    {
        var linesByOrder = view.Lines.GroupBy(item => item.AllocationRunOrderId)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.LineNumber).ToArray());

        return new AllocationRunResult(
            view.Run.Id,
            view.Run.RunCode,
            view.Run.BusinessDate,
            view.Run.CutoffAt,
            view.Run.Status,
            JsonSerializer.Deserialize<AllocationSettings>(view.Run.AlgorithmSettings)
                ?? throw AllocationRunOperationException.Conflict(
                    $"Run {view.Run.RunCode} has unreadable algorithm settings."),
            view.Run.TransitTableVersion,
            view.Run.EtaScheduleVersion,
            view.Run.OrderCount,
            view.Run.FailureReason,
            view.Run.StartedAt,
            view.Run.SnapshotCompletedAt,
            view.Run.CompletedAt,
            view.Run.Version,
            view.Orders.OrderBy(item => item.PriorityRank).Select(order => new AllocationRunOrderResult(
                order.Id,
                order.SalesOrderId,
                order.OrderCodeSnapshot,
                order.RetailerId,
                order.AcceptedAt,
                order.DeliveryDeadline,
                order.PriorityRank,
                order.Status,
                order.ShortageReason,
                (linesByOrder.TryGetValue(order.Id, out var lines) ? lines : [])
                    .Select(line => new AllocationRunLineResult(
                        line.Id,
                        line.SalesOrderLineId,
                        line.ProductId,
                        line.SkuSnapshot,
                        line.LineNumber,
                        line.OrderedQuantity,
                        line.TotalShelfLifeHours,
                        line.EffectiveMlorRatio,
                        line.MlorSource,
                        line.BaseNetUnitPrice,
                        line.AllocatedQuantity))
                    .ToArray())).ToArray(),
            view.Pool.OrderBy(item => item.ExpirationDate).Select(item => new AllocationRunStockResult(
                item.BatchId,
                item.WarehouseId,
                item.ProductId,
                item.ExpirationDate,
                item.SnapshotQuantity,
                item.RemainingQuantity,
                item.InventoryVersion)).ToArray());
    }
}
