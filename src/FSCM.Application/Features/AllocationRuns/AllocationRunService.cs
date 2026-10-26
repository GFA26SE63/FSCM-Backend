using System.Text.Json;
using FSCM.Application.Features.DeliveryEta;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.AllocationRuns;

/// <summary>
/// Drives the daily allocation run: it locks the orders that may compete at the cutoff, freezes the
/// stock pool and the settings they will be scored against, then builds the plan by choosing
/// batches (F33) and pricing the orders that come out whole (F89).
///
/// Nothing here touches a real inventory balance. The pool is the run's own copy, so a plan can be
/// built, discarded and rebuilt without ever committing stock; only run approval reserves (F88).
/// </summary>
public sealed class AllocationRunService(
    IAllocationRunStore store,
    AllocationPlanner planner,
    AllocationPricingService pricing,
    DeliveryEtaService deliveryEta,
    TimeProvider timeProvider)
{
    private static readonly TimeZoneInfo VietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    /// <summary>
    /// Starts the run for <paramref name="cutoffAt"/>'s business day, or returns the existing one.
    /// Re-running the same day is a no-op rather than a second run, so a retry, a restarted
    /// scheduler or a manual trigger cannot split one day's orders across two plans.
    /// </summary>
    public async Task<AllocationRunResult> StartAsync(DateTimeOffset cutoffAt, CancellationToken cancellationToken)
    {
        // Npgsql only binds a DateTimeOffset whose offset is zero, and both callers build the
        // cutoff in Vietnam time: the job from the local clock, the manual run from the published
        // schedule. Normalising here keeps the same instant and spares every query downstream from
        // caring, instead of failing on the first one that happens to take the cutoff as a
        // parameter. The business date is still read in Vietnam, where the day is defined.
        cutoffAt = cutoffAt.ToUniversalTime();
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
                    // The ratio the order was accepted under wins. Re-resolving here would let a
                    // configuration change made after acceptance silently restate an old order,
                    // which is exactly what F22 stores the snapshot to prevent. Resolution is a
                    // fallback for lines captured before that snapshot existed.
                    var key = (candidate.Order.RetailerId, line.Line.ProductId);
                    var resolved = line.Line.MlorMinimumLifeRatio > 0
                        ? (Ratio: line.Line.MlorMinimumLifeRatio, Source: line.Line.MlorSourceScope)
                        : mlor.TryGetValue(key, out var value)
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
                        ListUnitPrice = line.Line.UnitPrice,
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

    /// <summary>
    /// Builds the plan for a snapshotted run: batches are chosen, the orders that come out whole
    /// are priced, and the run moves to AwaitingApproval. Nothing is reserved here; the plan stays
    /// a proposal until Admin approves it (F87, F88).
    ///
    /// Planning an already planned run is refused rather than silently rebuilding, so a retry
    /// cannot replace a plan an Admin is in the middle of reviewing.
    /// </summary>
    /// <summary>
    /// Runs today's allocation by hand: close the run at its cutoff, then plan it.
    ///
    /// The scheduled job is the normal path. It is also the only one, which means a deployment that
    /// was down at the cutoff has no way to produce the day's plan at all - the orders simply sit
    /// there until tomorrow, when they queue behind a day of newer ones. This is the recovery.
    ///
    /// The cutoff is not a parameter by default: it comes from the published schedule, so pressing
    /// the button cannot quietly change which orders belong to the run. An Admin may pass a later
    /// moment on the same business date, for the case where the run was down and orders kept
    /// arriving, and whatever is used is stored on the run itself - the boundary that was applied
    /// stays readable afterwards rather than being inferred from when someone pressed it.
    ///
    /// Closing is idempotent on the business date, so pressing twice joins the run that exists
    /// instead of splitting the day across two plans.
    /// </summary>
    public async Task<AllocationRunResult> RunNowAsync(
        DateTimeOffset? cutoffAt, CancellationToken cancellationToken)
    {
        var schedule = await deliveryEta.GetScheduleAsync(null, cancellationToken);
        var scheduled = schedule.Milestones.Single(item => item.Key == "allocation_run").At;
        var now = timeProvider.GetUtcNow();
        var cutoff = cutoffAt ?? scheduled;

        // The business date goes first, and the order is the whole point. The published time this is
        // measured against is today's, so a cutoff on any other day fails one of the two checks
        // below as a side effect - yesterday's is earlier than it, tomorrow's is later than now -
        // and the caller is told their time is out of range when the real answer is that they named
        // the wrong day.
        if (ToBusinessDate(cutoff) != ToBusinessDate(now))
        {
            throw AllocationRunOperationException.Validation(
                $"The cutoff must fall on today's business date, {ToBusinessDate(now):yyyy-MM-dd}: " +
                "a day that has closed cannot be re-run, and a later one has not begun.");
        }

        if (cutoff > now)
        {
            throw AllocationRunOperationException.Conflict(
                $"The cutoff {InVietnam(cutoff):HH:mm} has not passed yet; a run cannot close before it.");
        }

        if (cutoff < scheduled)
        {
            throw AllocationRunOperationException.Validation(
                $"The cutoff cannot be earlier than the published {InVietnam(scheduled):HH:mm}: moving " +
                "it back would drop orders that belong to this run.");
        }

        var run = await StartAsync(cutoff, cancellationToken);
        if (run.Status != AllocationRunStatuses.Snapshotted) return run;

        await PlanAsync(run.Id, cancellationToken);
        return await GetAsync(run.Id, cancellationToken);
    }

    public async Task<AllocationPricing> PlanAsync(Guid runId, CancellationToken cancellationToken)
    {
        var view = await store.FindViewAsync(runId, cancellationToken)
            ?? throw AllocationRunOperationException.NotFound("Allocation run was not found.");
        if (view.Run.Status != AllocationRunStatuses.Snapshotted)
        {
            throw AllocationRunOperationException.Conflict(
                $"Run {view.Run.RunCode} is {view.Run.Status}; only a snapshotted run can be planned.");
        }

        var now = timeProvider.GetUtcNow();
        var batches = await store.ListPlannerBatchesAsync(runId, cancellationToken);
        var linesByOrder = view.Lines.GroupBy(item => item.AllocationRunOrderId)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.LineNumber).ToArray());

        var plannerOrders = view.Orders.OrderBy(item => item.PriorityRank).Select(order => new PlannerOrder(
            order.Id,
            order.OrderCodeSnapshot,
            order.RetailerId,
            order.PriorityRank,
            order.DeliveryDeadline,
            (linesByOrder.TryGetValue(order.Id, out var lines) ? lines : [])
                .Select(line => new PlannerLine(
                    line.Id, line.ProductId, line.SkuSnapshot, line.LineNumber,
                    line.OrderedQuantity, line.TotalShelfLifeHours, line.EffectiveMlorRatio))
                .ToArray())).ToArray();

        var routes = await BuildRoutesAsync(batches, plannerOrders, cancellationToken);
        var plan = planner.Plan(
            plannerOrders,
            batches,
            (warehouseId, retailerId) => routes.GetValueOrDefault((warehouseId, retailerId)));

        var priced = await PriceAsync(plan, view, now, cancellationToken);
        await PersistAsync(view.Run.Id, plan, priced, now, cancellationToken);
        store.ClearTracking();
        return priced;
    }

    /// <summary>
    /// ETA per warehouse-retailer pair, read once for the whole run. A pair with no cached distance
    /// or no estimable ETA is left out, and the planner treats that warehouse as unreachable for
    /// that order rather than guessing a leg.
    /// </summary>
    private async Task<Dictionary<(Guid, Guid), PlannerRoute>> BuildRoutesAsync(
        IReadOnlyCollection<PlannerBatch> batches,
        IReadOnlyCollection<PlannerOrder> orders,
        CancellationToken cancellationToken)
    {
        var warehouseIds = batches.Select(item => item.WarehouseId).Distinct().ToArray();
        var retailerIds = orders.Select(item => item.RetailerId).Distinct().ToArray();
        var distances = await store.ListDistancesAsync(warehouseIds, retailerIds, cancellationToken);
        var routes = new Dictionary<(Guid, Guid), PlannerRoute>();

        foreach (var warehouseId in warehouseIds)
        {
            foreach (var retailerId in retailerIds)
            {
                if (!distances.TryGetValue((warehouseId, retailerId), out var distanceMeters)) continue;
                try
                {
                    var eta = await deliveryEta.EstimateAsync(
                        new EtaRequest(warehouseId, retailerId, null), cancellationToken);
                    routes[(warehouseId, retailerId)] = new PlannerRoute(
                        distanceMeters,
                        eta.EstimatedArrivalAt,
                        DateOnly.FromDateTime(
                            TimeZoneInfo.ConvertTime(eta.EstimatedArrivalAt, VietnamTimeZone).DateTime));
                }
                catch (DeliveryEtaOperationException)
                {
                    // Unreachable is a legitimate outcome for one pair, not a failure of the run.
                }
            }
        }

        return routes;
    }

    private async Task<AllocationPricing> PriceAsync(
        AllocationPlan plan,
        AllocationRunView view,
        DateTimeOffset pricedAt,
        CancellationToken cancellationToken)
    {
        var fulfilled = plan.Fulfilled.ToArray();
        if (fulfilled.Length == 0) return new AllocationPricing(pricedAt, []);

        var lineById = view.Lines.ToDictionary(item => item.Id);
        var batchIds = fulfilled.SelectMany(order => order.Allocations)
            .Select(item => item.BatchId).Distinct().ToArray();
        var expiry = await store.ListBatchExpiryAsync(batchIds, cancellationToken);
        var categories = await store.ListProductCategoriesAsync(
            view.Lines.Select(item => item.ProductId).Distinct().ToArray(), cancellationToken);

        var orders = fulfilled.Select(order => new PricingOrder(
            order.RunOrderId,
            order.OrderCode,
            order.Allocations.Select(allocation =>
            {
                var line = lineById[allocation.RunLineId];
                return new PricingPart(
                    allocation.RunLineId,
                    allocation.BatchId,
                    allocation.WarehouseId,
                    line.ProductId,
                    categories.GetValueOrDefault(line.ProductId),
                    line.SkuSnapshot,
                    allocation.Quantity,
                    expiry.GetValueOrDefault(allocation.BatchId, pricedAt),
                    line.TotalShelfLifeHours,
                    line.ListUnitPrice,
                    line.BaseNetUnitPrice);
            }).ToArray())).ToArray();

        return await pricing.PriceAsync(pricedAt, orders, cancellationToken);
    }

    /// <summary>
    /// Keeps the engine's working for every line of the version: the weights and the MLOR it was
    /// measured against, every batch each hard filter looked at and what stopped it, and every
    /// ranking of every greedy turn.
    ///
    /// Rejections and losing scores are the point. "Why not that batch" cannot be answered from the
    /// chosen ones, and a score means nothing without the scores it beat.
    ///
    /// Nothing here is a price. Batches are settled first and priced after (F89), so the reason a
    /// batch was chosen stays separate from the reason it was later reduced.
    /// </summary>
    private void PersistTrace(AllocationPlan plan, Guid planVersionId, DateTimeOffset now)
    {
        var traces = new List<AllocationLineTrace>();
        var candidates = new List<AllocationTraceCandidate>();
        var scores = new List<AllocationTraceScore>();

        foreach (var planned in plan.Orders)
        {
            foreach (var line in planned.LineResults)
            {
                var result = line.Result;
                // A short order hands back every draw it made, so the lines that succeeded before
                // the shortfall are Released rather than Allocated. Lines after it were never
                // reached and have no result to record.
                var outcome = result.IsFullyAllocated
                    ? planned.IsFullyAllocated
                        ? AllocationLineTraceOutcomes.Allocated
                        : AllocationLineTraceOutcomes.Released
                    : AllocationLineTraceOutcomes.Short;
                var note = outcome switch
                {
                    AllocationLineTraceOutcomes.Short => planned.ShortageReason,
                    AllocationLineTraceOutcomes.Released =>
                        $"Trả {result.AllocatedQuantity:0.###} về quỹ chung vì đơn {planned.OrderCode} thiếu ở dòng khác.",
                    _ => null
                };

                var trace = new AllocationLineTrace
                {
                    Id = Guid.NewGuid(),
                    AllocationPlanVersionId = planVersionId,
                    AllocationRunLineId = line.RunLineId,
                    Outcome = outcome,
                    OrderedQuantity = result.RequiredQuantity,
                    DrawnQuantity = result.AllocatedQuantity,
                    ShortageQuantity = result.ShortageQuantity,
                    RequiredShelfLifeHours = Round3(result.RequiredShelfLifeHours),
                    ShelfLifeWeight = result.Weights.ShelfLife,
                    DistanceWeight = result.Weights.Distance,
                    CoverageWeight = result.Weights.Coverage,
                    MaximumDistanceMeters = result.Weights.MaximumDistanceMeters,
                    Note = note,
                    CreatedAt = now
                };
                traces.Add(trace);

                candidates.AddRange(result.FilterTrace.Select(item => new AllocationTraceCandidate
                {
                    Id = Guid.NewGuid(),
                    AllocationLineTraceId = trace.Id,
                    BatchId = item.BatchId,
                    BatchCodeSnapshot = item.BatchCode,
                    WarehouseId = item.WarehouseId,
                    WarehouseCodeSnapshot = item.WarehouseCode,
                    Stage = item.Stage.ToString(),
                    Passed = item.Passed,
                    Reason = item.Reason.ToString(),
                    EstimatedArrivalAt = item.EstimatedArrivalAt,
                    RemainingShelfLifeHours = item.RemainingShelfLifeHours is { } remaining ? Round3(remaining) : null,
                    RequiredShelfLifeHours = item.RequiredShelfLifeHours is { } required ? Round3(required) : null,
                    CreatedAt = now
                }));

                scores.AddRange(result.ScoreTrace.Select(item => new AllocationTraceScore
                {
                    Id = Guid.NewGuid(),
                    AllocationLineTraceId = trace.Id,
                    Iteration = item.Iteration,
                    Rank = item.Rank,
                    BatchId = item.BatchId,
                    BatchCodeSnapshot = item.BatchCode,
                    WarehouseId = item.WarehouseId,
                    WarehouseCodeSnapshot = item.WarehouseCode,
                    RemainingDemand = item.RemainingDemand,
                    ShelfLifeScore = item.ShelfLifeScore,
                    DistanceScore = item.DistanceScore,
                    CoverageScore = item.CoverageScore,
                    WeightedScore = item.WeightedScore,
                    Selected = item.Selected,
                    SelectedQuantity = item.SelectedQuantity,
                    CreatedAt = now
                }));
            }
        }

        store.AddLineTraces(traces);
        store.AddTraceCandidates(candidates);
        store.AddTraceScores(scores);
    }

    private static decimal Round3(decimal value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);

    private async Task PersistAsync(
        Guid runId,
        AllocationPlan plan,
        AllocationPricing priced,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var tracked = await store.FindViewForUpdateAsync(runId, transactionCancellationToken)
                ?? throw AllocationRunOperationException.NotFound("Allocation run disappeared while planning.");
            var orderById = tracked.Orders.ToDictionary(item => item.Id);
            var lineById = tracked.Lines.ToDictionary(item => item.Id);
            var allocations = new List<AllocationRunAllocation>();

            // A recalculation supersedes the proposal it replaces instead of editing it, so what an
            // Admin saw stays readable even after a new version is built.
            if (await store.FindLivePlanVersionAsync(runId, transactionCancellationToken) is { } live)
            {
                live.Status = AllocationPlanVersionStatuses.Superseded;
                live.Version++;
                live.UpdatedAt = now;
            }

            var planVersion = new AllocationPlanVersion
            {
                Id = Guid.NewGuid(),
                AllocationRunId = runId,
                VersionNumber = await store.MaxPlanVersionNumberAsync(runId, transactionCancellationToken) + 1,
                Status = AllocationPlanVersionStatuses.AwaitingApproval,
                PricedAt = priced.PricedAt,
                FulfilledOrderCount = plan.Fulfilled.Count(),
                ShortOrderCount = plan.Short.Count(),
                TotalFinalAmount = priced.Orders.Sum(item => item.FinalAmount),
                Version = 0,
                CreatedAt = now,
                UpdatedAt = now
            };
            store.AddPlanVersion(planVersion);
            store.AddPlanPrices(priced.Orders.SelectMany(order => order.Parts).Select(part => new AllocationPlanPrice
            {
                Id = Guid.NewGuid(),
                AllocationPlanVersionId = planVersion.Id,
                AllocationRunLineId = part.RunLineId,
                BatchId = part.BatchId,
                WarehouseId = part.WarehouseId,
                Quantity = part.Quantity,
                ListUnitPrice = part.ListUnitPrice,
                BaseNetUnitPrice = part.BaseNetUnitPrice,
                PricedAt = priced.PricedAt,
                BatchExpiresAt = part.BatchExpiresAt,
                TotalShelfLifeHours = part.TotalShelfLifeHours,
                RemainingShelfLifePercent = part.RemainingPercent,
                ShelfLifeDiscountTierId = part.TierId,
                DiscountType = part.DiscountType,
                DiscountValue = part.DiscountValue,
                ShelfLifeDiscountPerUnit = part.ShelfLifeDiscountPerUnit,
                FinalNetUnitPrice = part.FinalNetUnitPrice,
                Amount = part.Amount,
                DiscountTableVersion = part.DiscountTableVersion,
                Explanation = part.Explanation,
                CreatedAt = now
            }));

            foreach (var planned in plan.Orders)
            {
                var runOrder = orderById[planned.RunOrderId];
                runOrder.Status = planned.IsFullyAllocated
                    ? AllocationRunOrderStatuses.Fulfilled
                    : AllocationRunOrderStatuses.Short;
                runOrder.ShortageReason = planned.ShortageReason;

                foreach (var group in planned.Allocations.GroupBy(item => item.RunLineId))
                {
                    lineById[group.Key].AllocatedQuantity = group.Sum(item => item.Quantity);
                    allocations.AddRange(group.Select(item => new AllocationRunAllocation
                    {
                        Id = Guid.NewGuid(),
                        AllocationRunId = runId,
                        AllocationPlanVersionId = planVersion.Id,
                        AllocationRunLineId = item.RunLineId,
                        BatchId = item.BatchId,
                        WarehouseId = item.WarehouseId,
                        Quantity = item.Quantity,
                        Sequence = item.Sequence,
                        EstimatedArrivalAt = item.EstimatedArrivalAt,
                        RemainingShelfLifeHours = item.RemainingShelfLifeHours,
                        WeightedScore = item.WeightedScore,
                        CreatedAt = now
                    }));
                }
            }

            foreach (var row in tracked.Pool)
            {
                row.RemainingQuantity = plan.RemainingPool.GetValueOrDefault(row.BatchId, row.RemainingQuantity);
            }

            store.AddAllocations(allocations);
            PersistTrace(plan, planVersion.Id, now);
            tracked.Run.Status = AllocationRunStatuses.AwaitingApproval;
            tracked.Run.CompletedAt = now;
            tracked.Run.Version++;
            tracked.Run.UpdatedAt = now;
            await store.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);

    public async Task<AllocationRunResult> GetAsync(Guid runId, CancellationToken cancellationToken) =>
        ToResult(await store.FindViewAsync(runId, cancellationToken)
            ?? throw AllocationRunOperationException.NotFound("Allocation run was not found."));

    public async Task<AllocationRunResult> GetByBusinessDateAsync(
        DateOnly businessDate, CancellationToken cancellationToken) =>
        ToResult(await store.FindViewByBusinessDateAsync(businessDate, cancellationToken)
            ?? throw AllocationRunOperationException.NotFound(
                $"No allocation run exists for {businessDate:yyyy-MM-dd}."));

    /// <summary>
    /// The same instant in Vietnam, for a message someone reads. A cutoff arrives in whatever offset
    /// its caller built it in - the schedule in local time, an API client quite possibly in UTC - and
    /// a refusal that says 05:00 about a 12:00 cutoff is worse than no message at all.
    /// </summary>
    private static DateTimeOffset InVietnam(DateTimeOffset moment) =>
        TimeZoneInfo.ConvertTime(moment, VietnamTimeZone);

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
