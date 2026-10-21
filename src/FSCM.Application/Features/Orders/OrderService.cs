using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.Picking;
using FSCM.Application.Features.WarehouseConfiguration;
using FSCM.Domain.Allocation;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using DomainAllocation = FSCM.Domain.Entities.Allocation;

namespace FSCM.Application.Features.Orders;

public sealed class OrderService(
    IOrderStore store,
    IOrderPricingService pricingService,
    IInventoryLifecycleService inventoryLifecycleService,
    IPickingPlanService pickingPlanService,
    TimeProvider timeProvider)
{
    private const int MaximumPlacementAttempts = 3;
    private const string BufferStockKey = "order.buffer_stock_cartons";
    private const string OrderStepKey = "order.order_step_cartons";
    private static readonly TimeZoneInfo VietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
    private static readonly JsonSerializerOptions TraceJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<OrderDetailResult> CreateAsync(
        CreateAssistedOrderData data,
        Guid salesUserId,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(data);
        var requestHash = ComputeRequestHash(normalized, salesUserId);
        var existing = await store.FindByClientOrderKeyAsync(normalized.ClientOrderKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.CreatedByUserId != salesUserId || existing.RequestHash != requestHash)
            {
                throw OrderOperationException.Conflict("Client order key was already used for a different request.");
            }

            return await GetAsync(existing.Id, salesUserId, SystemRoles.Sales, cancellationToken);
        }

        var retailerContext = await store.FindRetailerContextAsync(
            salesUserId, normalized.RetailerId, cancellationToken)
            ?? throw OrderOperationException.Forbidden(
                "Sales can only create orders for an active retailer in the assigned territory.");
        if (retailerContext.Retailer.Status != "Active")
        {
            throw OrderOperationException.Conflict("Retailer is not active.");
        }

        ValidatePayment(normalized.PaymentMethod, retailerContext.Retailer.CreditTermDays);
        var now = timeProvider.GetUtcNow();
        var today = GetVietnamDate(now);
        if (normalized.RequestedDeliveryDate < today)
        {
            throw OrderOperationException.Validation("Requested delivery date cannot be in the past.");
        }

        var orderCode = await store.NextOrderCodeAsync(cancellationToken);
        for (var attempt = 1; attempt <= MaximumPlacementAttempts; attempt++)
        {
            try
            {
                var plan = await BuildPlanAsync(
                    normalized, retailerContext, salesUserId, orderCode, requestHash, now, today, cancellationToken);
                await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
                {
                    Persist(plan);
                    await store.SaveChangesAsync(transactionCancellationToken);
                }, cancellationToken);

                store.ClearTracking();
                return await GetAsync(plan.Order.Id, salesUserId, SystemRoles.Sales, cancellationToken);
            }
            catch (InventoryOperationException exception) when (
                exception.Code == "conflict" && attempt < MaximumPlacementAttempts)
            {
                store.ClearTracking();
            }
            catch (InventoryOperationException exception) when (exception.Code == "conflict")
            {
                store.ClearTracking();
                throw OrderOperationException.Conflict(
                    "Inventory changed while the order was being allocated. Refresh stock and retry the request.");
            }
        }

        throw OrderOperationException.Conflict("Order placement could not be completed.");
    }

    public async Task<IReadOnlyCollection<OrderSummaryResult>> ListAsync(
        Guid actorUserId,
        string actorRole,
        string? status,
        Guid? retailerId,
        CancellationToken cancellationToken)
    {
        ValidateReadRole(actorRole);
        if (!string.IsNullOrWhiteSpace(status) && !OrderStatusSet.Contains(status))
        {
            throw OrderOperationException.Validation("Order status filter is invalid.");
        }

        var items = await store.ListAsync(actorUserId, actorRole, status, retailerId, cancellationToken);
        return items.Select(ToSummary).ToArray();
    }

    public async Task<OrderDetailResult> GetAsync(
        Guid orderId,
        Guid actorUserId,
        string actorRole,
        CancellationToken cancellationToken)
    {
        ValidateReadRole(actorRole);
        var view = await store.FindAsync(orderId, false, cancellationToken)
            ?? throw OrderOperationException.NotFound("Order was not found.");
        if (!await store.CanViewAsync(view, actorUserId, actorRole, cancellationToken))
        {
            throw OrderOperationException.Forbidden("User cannot access this order.");
        }

        return ToDetail(view);
    }

    public async Task<OrderTraceResult> GetTraceAsync(
        Guid orderId,
        Guid actorUserId,
        string actorRole,
        CancellationToken cancellationToken)
    {
        var order = await GetViewableAsync(orderId, actorUserId, actorRole, cancellationToken);
        return new(order.Order.Id, order.Order.OrderCode, order.Order.AllocationTraceJson);
    }

    public Task<OrderDetailResult> ApproveAsync(
        Guid orderId,
        int expectedVersion,
        Guid operatorUserId,
        CancellationToken cancellationToken) =>
        TransitionAsync(orderId, expectedVersion, operatorUserId, SystemRoles.Operator,
            OrderStatuses.Approved, null, cancellationToken);

    public Task<OrderDetailResult> RejectAsync(
        Guid orderId,
        int expectedVersion,
        string reason,
        Guid operatorUserId,
        CancellationToken cancellationToken)
    {
        reason = CleanOptional(reason, 1_000)
            ?? throw OrderOperationException.Validation("Rejection reason is required.");
        return TransitionAsync(orderId, expectedVersion, operatorUserId, SystemRoles.Operator,
            OrderStatuses.Rejected, reason, cancellationToken);
    }

    public async Task<OrderDetailResult> CancelAsync(
        Guid orderId,
        int expectedVersion,
        Guid actorUserId,
        string actorRole,
        string? reason,
        CancellationToken cancellationToken)
    {
        if (actorRole is not (SystemRoles.Sales or SystemRoles.Operator or SystemRoles.Admin))
        {
            throw OrderOperationException.Forbidden("Role cannot cancel orders.");
        }

        reason = CleanOptional(reason, 1_000);
        await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var view = await store.FindAsync(orderId, true, transactionCancellationToken)
                ?? throw OrderOperationException.NotFound("Order was not found.");
            if (view.Order.Status == OrderStatuses.Cancelled) return;
            if (actorRole == SystemRoles.Sales &&
                (view.Order.CreatedByUserId != actorUserId || view.Order.Status != OrderStatuses.PendingApproval))
            {
                throw OrderOperationException.Forbidden("Sales can only cancel their own pending order.");
            }

            if (view.Order.Status is not (OrderStatuses.PendingApproval or OrderStatuses.Approved))
            {
                throw OrderOperationException.Conflict("Only pending or approved orders can be cancelled.");
            }

            EnsureOrderVersion(view.Order, expectedVersion);
            await pickingPlanService.CancelForOrderAsync(
                view.Order.Id, timeProvider.GetUtcNow(), transactionCancellationToken);
            await ReleaseInventoryAsync(view, actorUserId, transactionCancellationToken);
            var fromStatus = view.Order.Status;
            var now = timeProvider.GetUtcNow();
            ApplyTransition(view.Order, OrderStatuses.Cancelled, actorUserId, reason, now);
            store.AddHistory(NewHistory(view.Order.Id, fromStatus, OrderStatuses.Cancelled,
                actorUserId, reason, now));
            await store.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);

        store.ClearTracking();
        return await GetAsync(orderId, actorUserId, actorRole, cancellationToken);
    }

    private async Task<OrderDetailResult> TransitionAsync(
        Guid orderId,
        int expectedVersion,
        Guid actorUserId,
        string actorRole,
        string targetStatus,
        string? reason,
        CancellationToken cancellationToken)
    {
        if (actorRole != SystemRoles.Operator)
        {
            throw OrderOperationException.Forbidden("Only Operator can approve or reject orders.");
        }

        await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var view = await store.FindAsync(orderId, true, transactionCancellationToken)
                ?? throw OrderOperationException.NotFound("Order was not found.");
            if (view.Order.Status == targetStatus)
            {
                if (targetStatus == OrderStatuses.Approved)
                {
                    await pickingPlanService.CreateForApprovedOrderAsync(
                        view, timeProvider.GetUtcNow(), transactionCancellationToken);
                    await store.SaveChangesAsync(transactionCancellationToken);
                }
                return;
            }
            if (view.Order.Status != OrderStatuses.PendingApproval)
            {
                throw OrderOperationException.Conflict("Only pending orders can be approved or rejected.");
            }

            EnsureOrderVersion(view.Order, expectedVersion);
            var now = timeProvider.GetUtcNow();
            if (targetStatus == OrderStatuses.Rejected)
            {
                await ReleaseInventoryAsync(view, actorUserId, transactionCancellationToken);
            }
            else
            {
                foreach (var allocation in view.Lines.SelectMany(item => item.Allocations)
                             .OrderBy(item => item.AllocationSequence))
                {
                    await inventoryLifecycleService.ReserveAsync(
                        allocation.Id,
                        actorUserId,
                        view.InventoryVersions[allocation.BatchId],
                        transactionCancellationToken);
                }
            }

            ApplyTransition(view.Order, targetStatus, actorUserId, reason, now);
            if (targetStatus == OrderStatuses.Approved)
            {
                await pickingPlanService.CreateForApprovedOrderAsync(view, now, transactionCancellationToken);
            }
            store.AddHistory(NewHistory(view.Order.Id, OrderStatuses.PendingApproval,
                targetStatus, actorUserId, reason, now));
            await store.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);

        store.ClearTracking();
        return await GetAsync(orderId, actorUserId, actorRole, cancellationToken);
    }

    private async Task ReleaseInventoryAsync(
        OrderView view,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        foreach (var allocation in view.Lines.SelectMany(item => item.Allocations)
                     .OrderBy(item => item.AllocationSequence))
        {
            await inventoryLifecycleService.ReleaseAsync(
                allocation.Id,
                actorUserId,
                view.InventoryVersions[allocation.BatchId],
                cancellationToken);
        }
    }

    private async Task<PlacementPlan> BuildPlanAsync(
        CreateAssistedOrderData data,
        OrderRetailerContext retailerContext,
        Guid salesUserId,
        string orderCode,
        string requestHash,
        DateTimeOffset now,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var retailerLocation = await store.FindRetailerLocationAsync(data.RetailerId, cancellationToken)
            ?? throw OrderOperationException.Conflict("Retailer location is required for order allocation.");
        var weights = await GetWeightsAsync(cancellationToken);
        var bufferStock = await GetPositiveConfigurationAsync(BufferStockKey, 0, allowZero: true, cancellationToken);
        var orderStep = await GetPositiveConfigurationAsync(OrderStepKey, 1, allowZero: false, cancellationToken);
        var deliveryAddress = data.DeliveryAddress ?? CleanOptional(retailerContext.Retailer.Address, 1_000)
            ?? throw OrderOperationException.Validation(
                "Delivery address is required when the retailer has no registered address.");
        var order = new SalesOrder
        {
            Id = Guid.NewGuid(),
            OrderCode = orderCode,
            RetailerId = retailerContext.Retailer.Id,
            RetailerCodeSnapshot = retailerContext.Retailer.Code,
            RetailerNameSnapshot = retailerContext.Retailer.Name,
            SalesGroupId = retailerContext.SalesGroupId,
            CreatedByUserId = salesUserId,
            ClientOrderKey = data.ClientOrderKey,
            RequestHash = requestHash,
            Source = OrderSources.Assisted,
            Status = OrderStatuses.PendingApproval,
            PaymentMethod = data.PaymentMethod,
            CreditTermDays = data.PaymentMethod == OrderPaymentMethods.Credit
                ? retailerContext.Retailer.CreditTermDays
                : 0,
            DeliveryAddress = deliveryAddress,
            RequestedDeliveryDate = data.RequestedDeliveryDate,
            Note = data.Note,
            ServerReceivedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        var lines = new List<SalesOrderLine>();
        var allocations = new List<DomainAllocation>();
        var promotions = new List<OrderPromotion>();
        var traces = new List<AllocationResult>();
        var inventoryVersions = new Dictionary<Guid, int>();
        var lineNumber = 0;

        foreach (var requestedLine in data.Lines.OrderBy(item => item.ProductId))
        {
            lineNumber++;
            if (requestedLine.Quantity % orderStep != 0)
            {
                throw OrderOperationException.Validation(
                    $"Order quantity must be a multiple of the configured order step ({orderStep}).");
            }

            var product = await store.FindPublishedProductAsync(requestedLine.ProductId, cancellationToken)
                ?? throw OrderOperationException.NotFound("Published product was not found.");
            var candidates = await store.ListCandidatesAsync(product.Id, cancellationToken);
            var rules = await store.ListTransitRulesAsync(
                candidates.Select(item => item.Warehouse.Id).Distinct().ToArray(), cancellationToken);
            var engineCandidates = candidates.Select(item =>
            {
                var distance = item.WarehouseLatitude.HasValue && item.WarehouseLongitude.HasValue
                    ? CalculateDistanceMeters(item.WarehouseLatitude.Value, item.WarehouseLongitude.Value,
                        retailerLocation.Latitude, retailerLocation.Longitude)
                    : weights.MaximumDistanceMeters + 1;
                var rule = rules.Where(rule => rule.WarehouseId == item.Warehouse.Id && rule.IsActive &&
                        distance >= rule.MinDistanceMeters && distance <= rule.MaxDistanceMeters &&
                        distance <= rule.MaxServingDistanceMeters)
                    .OrderBy(rule => rule.MaxDistanceMeters)
                    .FirstOrDefault();
                var transitDays = rule is null
                    ? (int?)null
                    : Math.Max(0, (int)Math.Ceiling((rule.HandlingMinutes + rule.TransitMinutes) / 1_440m));
                var allocatable = Math.Max(0m,
                    Math.Floor((item.Balance.AvailableQuantity - bufferStock) / orderStep) * orderStep);
                var available = allocatable > int.MaxValue ? int.MaxValue : (int)allocatable;
                inventoryVersions[item.Batch.Id] = item.Balance.Version;
                return new BatchCandidate(
                    item.Batch.Id,
                    item.Batch.BatchCode,
                    item.Warehouse.Id,
                    item.Warehouse.Code,
                    item.Batch.ExpirationDate,
                    available,
                    distance,
                    transitDays);
            }).ToArray();
            var mlorRatio = await store.FindMlorRatioAsync(
                data.RetailerId, product.Id, cancellationToken) ??
                await GetDecimalConfigurationAsync(
                    BusinessConfigurationKeys.DefaultMlorRatio, 0.25m, cancellationToken);
            if (mlorRatio is < 0 or > 1)
            {
                throw OrderOperationException.Conflict("MLOR configuration must be between 0 and 1.");
            }
            var allocationResult = new AllocationEngine(weights).Allocate(
                new AllocationRequest(
                    product.Sku,
                    requestedLine.Quantity,
                    (int)Math.Ceiling(product.ShelfLifeHours / 24m),
                    mlorRatio,
                    today,
                    today,
                    data.RequestedDeliveryDate),
                engineCandidates);
            traces.Add(allocationResult);
            if (!allocationResult.IsFullyAllocated)
            {
                throw OrderOperationException.Conflict(
                    $"Insufficient eligible inventory for {product.Sku}; shortage is {allocationResult.ShortageQuantity} cartons. Partial fulfilment is not allowed.");
            }

            var line = new SalesOrderLine
            {
                Id = Guid.NewGuid(),
                SalesOrderId = order.Id,
                ProductId = product.Id,
                LineNumber = lineNumber,
                SkuSnapshot = product.Sku,
                ProductNameSnapshot = product.Name,
                OrderedQuantity = requestedLine.Quantity,
                CreatedAt = now
            };
            var linePrices = new List<OrderPriceSnapshot>();
            foreach (var decision in allocationResult.Allocations)
            {
                var price = await pricingService.CalculateAsync(
                    product.Id, decision.Quantity, decision.BatchId, data.RetailerId, now, cancellationToken);
                linePrices.Add(price);
                var selectedScore = allocationResult.ScoreTrace.Single(item =>
                    item.Selected && item.BatchId == decision.BatchId && item.Iteration == decision.Sequence);
                var allocation = new DomainAllocation
                {
                    Id = Guid.NewGuid(),
                    SalesOrderLineId = line.Id,
                    BatchId = decision.BatchId,
                    WarehouseId = decision.WarehouseId,
                    AllocatedQuantity = decision.Quantity,
                    AllocationSequence = decision.Sequence,
                    DistanceMeters = decision.DistanceMeters,
                    EstimatedArrivalAt = ToVietnamStartOfDay(decision.EstimatedArrivalDate),
                    RemainingLifeHoursAtArrival = (int)(decision.RemainingShelfLifeDays * 24),
                    ShelfLifeScore = selectedScore.ShelfLifeScore,
                    DistanceScore = selectedScore.DistanceScore,
                    CoverageScore = selectedScore.CoverageScore,
                    WeightedScore = selectedScore.WeightedScore,
                    UnitPrice = price.UnitPrice,
                    GrossAmount = price.GrossAmount,
                    DiscountPercent = price.DiscountPercent,
                    DiscountAmount = price.DiscountAmount,
                    NetAmount = price.NetAmount,
                    PromotionId = price.PromotionId,
                    DiscountSourceType = price.DiscountSourceType,
                    DiscountSourceCode = price.DiscountSourceCode,
                    Status = AllocationInventoryStatuses.Tentative,
                    CreatedAt = now
                };
                allocations.Add(allocation);
                if (price.PromotionId.HasValue)
                {
                    promotions.Add(new OrderPromotion
                    {
                        Id = Guid.NewGuid(),
                        SalesOrderLineId = line.Id,
                        PromotionId = price.PromotionId.Value,
                        DiscountPercent = price.DiscountPercent,
                        DiscountAmount = price.DiscountAmount,
                        IsServerFinal = true,
                        AppliedAt = now
                    });
                }
            }

            line.UnitPrice = linePrices[0].UnitPrice;
            line.GrossAmount = linePrices.Sum(item => item.GrossAmount);
            line.DiscountAmount = linePrices.Sum(item => item.DiscountAmount);
            line.NetAmount = linePrices.Sum(item => item.NetAmount);
            line.DiscountPercent = line.GrossAmount == 0
                ? 0
                : Math.Round(line.DiscountAmount / line.GrossAmount * 100m, 2);
            var sourceCodes = linePrices.Select(item => item.DiscountSourceCode)
                .Distinct(StringComparer.Ordinal).ToArray();
            line.DiscountSourceCode = sourceCodes.Length == 1 ? sourceCodes[0] : "MIXED";
            var sourceTypes = linePrices.Select(item => item.DiscountSourceType)
                .Distinct(StringComparer.Ordinal).ToArray();
            line.DiscountSourceType = sourceTypes.Length == 1 ? sourceTypes[0] : "Mixed";
            line.EstimatedArrivalAt = allocations.Where(item => item.SalesOrderLineId == line.Id)
                .Max(item => item.EstimatedArrivalAt);
            lines.Add(line);
        }

        order.SubtotalAmount = lines.Sum(item => item.GrossAmount);
        order.DiscountAmount = lines.Sum(item => item.DiscountAmount);
        order.FinalAmount = lines.Sum(item => item.NetAmount);
        order.AllocationTraceJson = JsonSerializer.Serialize(new
        {
            orderCode,
            evaluatedAt = now,
            weights,
            bufferStock,
            orderStep,
            lines = traces
        }, TraceJsonOptions);
        var history = NewHistory(order.Id, null, OrderStatuses.PendingApproval,
            salesUserId, "Sales tạo đơn hỗ trợ", now);
        return new(order, lines, allocations, promotions, history, inventoryVersions);
    }

    private void Persist(PlacementPlan plan)
    {
        store.AddOrder(plan.Order);
        foreach (var line in plan.Lines) store.AddLine(line);
        foreach (var allocation in plan.Allocations) store.AddAllocation(allocation);
        foreach (var promotion in plan.Promotions) store.AddPromotion(promotion);
        store.AddHistory(plan.History);
    }

    private async Task<OrderView> GetViewableAsync(
        Guid orderId,
        Guid actorUserId,
        string actorRole,
        CancellationToken cancellationToken)
    {
        ValidateReadRole(actorRole);
        var order = await store.FindAsync(orderId, false, cancellationToken)
            ?? throw OrderOperationException.NotFound("Order was not found.");
        if (!await store.CanViewAsync(order, actorUserId, actorRole, cancellationToken))
        {
            throw OrderOperationException.Forbidden("User cannot access this order.");
        }

        return order;
    }

    private async Task<AllocationWeights> GetWeightsAsync(CancellationToken cancellationToken)
    {
        var maximumDistance = await GetPositiveConfigurationAsync(
            BusinessConfigurationKeys.ServiceRadiusMeters, 20_000, false, cancellationToken);
        var raw = await store.FindConfigurationAsync(
            BusinessConfigurationKeys.AllocationWeights, cancellationToken);
        if (string.IsNullOrWhiteSpace(raw)) return new(MaximumDistanceMeters: maximumDistance);
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            var weights = new AllocationWeights(
                root.GetProperty("expiry").GetDecimal(),
                root.GetProperty("distance").GetDecimal(),
                root.GetProperty("coverage").GetDecimal(),
                maximumDistance);
            _ = new AllocationEngine(weights);
            return weights;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or ArgumentException)
        {
            throw OrderOperationException.Conflict("Allocation weight configuration is invalid.");
        }
    }

    private async Task<int> GetPositiveConfigurationAsync(
        string key,
        int fallback,
        bool allowZero,
        CancellationToken cancellationToken)
    {
        var raw = await store.FindConfigurationAsync(key, cancellationToken);
        if (raw is null) return fallback;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ||
            (allowZero ? value < 0 : value <= 0))
        {
            throw OrderOperationException.Conflict($"Configuration '{key}' is invalid.");
        }

        return value;
    }

    private async Task<decimal> GetDecimalConfigurationAsync(
        string key,
        decimal fallback,
        CancellationToken cancellationToken)
    {
        var raw = await store.FindConfigurationAsync(key, cancellationToken);
        if (raw is null) return fallback;
        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            throw OrderOperationException.Conflict($"Configuration '{key}' is invalid.");
        }

        return value;
    }

    private static CreateAssistedOrderData Normalize(CreateAssistedOrderData data)
    {
        if (data.ClientOrderKey == Guid.Empty || data.RetailerId == Guid.Empty)
            throw OrderOperationException.Validation("Client order key and retailer are required.");
        var paymentMethod = NormalizePaymentMethod(data.PaymentMethod);
        if (paymentMethod is null)
            throw OrderOperationException.Validation("Payment method must be Prepaid, COD, or Credit.");
        if (data.Lines is null || data.Lines.Count is < 1 or > 100)
            throw OrderOperationException.Validation("Order must contain between 1 and 100 lines.");
        if (data.Lines.Any(item => item.ProductId == Guid.Empty || item.Quantity <= 0))
            throw OrderOperationException.Validation("Every order line requires a product and positive carton quantity.");
        var lines = data.Lines.GroupBy(item => item.ProductId)
            .Select(group => new CreateOrderLineData(group.Key, checked(group.Sum(item => item.Quantity))))
            .ToArray();
        var address = CleanOptional(data.DeliveryAddress, 1_000);
        var note = CleanOptional(data.Note, 2_000);
        return data with
        {
            PaymentMethod = paymentMethod,
            DeliveryAddress = address,
            Note = note,
            Lines = lines
        };
    }

    private static string? NormalizePaymentMethod(string? value)
    {
        var candidate = value?.Trim();
        if (string.Equals(candidate, OrderPaymentMethods.Prepaid, StringComparison.OrdinalIgnoreCase))
            return OrderPaymentMethods.Prepaid;
        if (string.Equals(candidate, OrderPaymentMethods.Cod, StringComparison.OrdinalIgnoreCase))
            return OrderPaymentMethods.Cod;
        if (string.Equals(candidate, OrderPaymentMethods.Credit, StringComparison.OrdinalIgnoreCase))
            return OrderPaymentMethods.Credit;
        return null;
    }

    private static void ValidatePayment(string paymentMethod, int retailerCreditTermDays)
    {
        if (paymentMethod == OrderPaymentMethods.Credit && retailerCreditTermDays is not (7 or 14 or 30))
        {
            throw OrderOperationException.Conflict("Retailer is not eligible for an approved credit term.");
        }
    }

    private static void ValidateReadRole(string role)
    {
        if (role is not (SystemRoles.Admin or SystemRoles.Operator or SystemRoles.SalesManager or
            SystemRoles.Sales or SystemRoles.WarehouseKeeper or SystemRoles.Retailer))
        {
            throw OrderOperationException.Forbidden("Role cannot read orders.");
        }
    }

    private static void EnsureOrderVersion(SalesOrder order, int expectedVersion)
    {
        if (order.Version != expectedVersion)
            throw OrderOperationException.Conflict(
                $"Order version is {order.Version}; refresh and retry with the current version.");
    }

    private static void ApplyTransition(
        SalesOrder order,
        string targetStatus,
        Guid actorUserId,
        string? reason,
        DateTimeOffset now)
    {
        order.Status = targetStatus;
        if (targetStatus is OrderStatuses.Approved or OrderStatuses.Rejected)
        {
            order.ReviewedByUserId = actorUserId;
            order.ReviewedAt = now;
        }
        order.RejectionReason = targetStatus == OrderStatuses.Rejected ? reason : order.RejectionReason;
        order.Version++;
        order.UpdatedAt = now;
    }

    private static OrderStatusHistory NewHistory(
        Guid orderId,
        string? fromStatus,
        string toStatus,
        Guid actorUserId,
        string? reason,
        DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            SalesOrderId = orderId,
            ChangedByUserId = actorUserId,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Reason = reason,
            ChangedAt = now
        };

    private static OrderSummaryResult ToSummary(OrderView view) => new(
        view.Order.Id,
        view.Order.OrderCode,
        view.Order.RetailerId,
        view.Order.RetailerCodeSnapshot,
        view.Order.RetailerNameSnapshot,
        view.Order.SalesGroupId,
        view.Order.CreatedByUserId,
        view.Order.Status,
        view.Order.PaymentMethod,
        view.Order.FinalAmount,
        (int)view.Lines.Sum(item => item.Line.OrderedQuantity),
        view.Lines.Count,
        view.Order.Source,
        view.Order.Version,
        view.Order.CreatedAt);

    private static OrderDetailResult ToDetail(OrderView view) => new(
        view.Order.Id,
        view.Order.OrderCode,
        view.Order.RetailerId,
        view.Order.RetailerCodeSnapshot,
        view.Order.RetailerNameSnapshot,
        view.Order.SalesGroupId,
        view.Order.CreatedByUserId,
        view.Order.ReviewedByUserId,
        view.Order.Source,
        view.Order.Status,
        view.Order.PaymentMethod,
        view.Order.CreditTermDays,
        view.Order.DeliveryAddress,
        view.Order.RequestedDeliveryDate,
        view.Order.Note,
        view.Order.SubtotalAmount,
        view.Order.DiscountAmount,
        view.Order.FinalAmount,
        view.Order.RejectionReason,
        view.Order.Version,
        view.Order.ServerReceivedAt,
        view.Order.CreatedAt,
        view.Order.UpdatedAt,
        view.Lines.OrderBy(item => item.Line.LineNumber).Select(item => new OrderLineResult(
            item.Line.Id,
            item.Line.LineNumber,
            item.Line.ProductId,
            item.Line.SkuSnapshot,
            item.Line.ProductNameSnapshot,
            item.Line.OrderedQuantity,
            item.Line.UnitPrice,
            item.Line.GrossAmount,
            item.Line.DiscountPercent,
            item.Line.DiscountAmount,
            item.Line.NetAmount,
            item.Line.DiscountSourceType,
            item.Line.DiscountSourceCode,
            item.Line.EstimatedArrivalAt,
            item.Allocations.OrderBy(allocation => allocation.AllocationSequence).Select(allocation =>
                new OrderAllocationResult(
                    allocation.Id,
                    allocation.BatchId,
                    view.BatchCodes[allocation.BatchId],
                    allocation.WarehouseId,
                    view.WarehouseCodes[allocation.WarehouseId],
                    allocation.AllocatedQuantity,
                    allocation.AllocationSequence,
                    allocation.DistanceMeters,
                    allocation.EstimatedArrivalAt,
                    allocation.RemainingLifeHoursAtArrival,
                    allocation.ShelfLifeScore,
                    allocation.DistanceScore,
                    allocation.CoverageScore,
                    allocation.WeightedScore,
                    allocation.UnitPrice,
                    allocation.GrossAmount,
                    allocation.DiscountPercent,
                    allocation.DiscountAmount,
                    allocation.NetAmount,
                    allocation.DiscountSourceType,
                    allocation.DiscountSourceCode,
                    allocation.Status)).ToArray())).ToArray(),
        view.History.OrderBy(item => item.ChangedAt).Select(item => new OrderStatusHistoryResult(
            item.FromStatus, item.ToStatus, item.Reason, item.ChangedByUserId, item.ChangedAt)).ToArray());

    private static string ComputeRequestHash(CreateAssistedOrderData data, Guid salesUserId)
    {
        var lines = data.Lines.OrderBy(item => item.ProductId)
            .Select(item => $"{item.ProductId:N}:{item.Quantity}");
        var canonical = string.Join('|', salesUserId.ToString("N"), data.RetailerId.ToString("N"),
            data.PaymentMethod, data.RequestedDeliveryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            data.DeliveryAddress, data.Note, string.Join(';', lines));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static decimal CalculateDistanceMeters(
        decimal fromLatitude,
        decimal fromLongitude,
        decimal toLatitude,
        decimal toLongitude)
    {
        const double earthRadiusMeters = 6_371_000;
        static double Radians(decimal degrees) => (double)degrees * Math.PI / 180d;
        var latitudeDelta = Radians(toLatitude - fromLatitude);
        var longitudeDelta = Radians(toLongitude - fromLongitude);
        var firstLatitude = Radians(fromLatitude);
        var secondLatitude = Radians(toLatitude);
        var value = Math.Sin(latitudeDelta / 2) * Math.Sin(latitudeDelta / 2) +
                    Math.Cos(firstLatitude) * Math.Cos(secondLatitude) *
                    Math.Sin(longitudeDelta / 2) * Math.Sin(longitudeDelta / 2);
        return Math.Round((decimal)(earthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(value), Math.Sqrt(1 - value))), 3);
    }

    private static DateTimeOffset ToVietnamStartOfDay(DateOnly date) =>
        new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).ToUniversalTime();

    private static DateOnly GetVietnamDate(DateTimeOffset now) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, VietnamTimeZone).DateTime);

    private static string? CleanOptional(string? value, int maximumLength)
    {
        value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (value?.Length > maximumLength)
            throw OrderOperationException.Validation($"Text cannot exceed {maximumLength} characters.");
        return value;
    }

    private static readonly HashSet<string> OrderStatusSet =
    [
        OrderStatuses.PendingApproval,
        OrderStatuses.Approved,
        OrderStatuses.Picking,
        OrderStatuses.Rejected,
        OrderStatuses.Cancelled,
        OrderStatuses.Issued,
        OrderStatuses.Delivering,
        OrderStatuses.Delivered
    ];

    private sealed record PlacementPlan(
        SalesOrder Order,
        IReadOnlyCollection<SalesOrderLine> Lines,
        IReadOnlyCollection<DomainAllocation> Allocations,
        IReadOnlyCollection<OrderPromotion> Promotions,
        OrderStatusHistory History,
        Dictionary<Guid, int> InventoryVersions);
}
