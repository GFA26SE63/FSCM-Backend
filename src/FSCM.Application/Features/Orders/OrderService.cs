using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FSCM.Application.Features.AllocationRuns;
using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.Picking;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Orders;

public sealed class OrderService(
    IOrderStore store,
    IOrderPricingService pricingService,
    IInventoryLifecycleService inventoryLifecycleService,
    IPickingPlanService pickingPlanService,
    TimeProvider timeProvider)
{
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
        var plan = await BuildCaptureAsync(
            normalized, retailerContext, salesUserId, orderCode, requestHash, now, cancellationToken);
        try
        {
            await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
            {
                Persist(plan);
                await store.SaveChangesAsync(transactionCancellationToken);
            }, cancellationToken);
        }
        catch (OrderOperationException exception) when (exception.Code == "conflict")
        {
            store.ClearTracking();
            var concurrent = await store.FindByClientOrderKeyAsync(normalized.ClientOrderKey, cancellationToken);
            if (concurrent is null || concurrent.CreatedByUserId != salesUserId || concurrent.RequestHash != requestHash)
                throw;
            return await GetAsync(concurrent.Id, salesUserId, SystemRoles.Sales, cancellationToken);
        }

        store.ClearTracking();
        return await GetAsync(plan.Order.Id, salesUserId, SystemRoles.Sales, cancellationToken);
    }

    /// <summary>
    /// F30: accepts an offline-drafted order a Sales rep submits once back online. Resubmission with
    /// the same client key is idempotent (no duplicate order). Permission, MLOR and base price are
    /// re-checked; a gap between the offline estimate and the server total is flagged for Sales to
    /// confirm. The server-accept time is stamped as the cutoff; momentary stock is not reserved or
    /// used to reject the order.
    /// </summary>
    public async Task<OrderDetailResult> SubmitOfflineAsync(
        SubmitOfflineOrderData data,
        Guid salesUserId,
        CancellationToken cancellationToken)
    {
        if (data.OfflineEstimatedAmount < 0)
            throw OrderOperationException.Validation("Offline estimated amount cannot be negative.");
        var assisted = new CreateAssistedOrderData(
            data.ClientOrderKey, data.RetailerId, data.PaymentMethod, data.RequestedDeliveryDate,
            data.DeliveryAddress, data.Note, data.Lines);
        var normalized = Normalize(assisted);
        var requestHash = ComputeOfflineRequestHash(normalized, data, salesUserId);
        var existing = await store.FindByClientOrderKeyAsync(normalized.ClientOrderKey, cancellationToken);
        if (existing is not null)
        {
            var legacyOfflineHash = ComputeRequestHash(normalized, salesUserId);
            if (existing.CreatedByUserId != salesUserId ||
                existing.RequestHash != requestHash &&
                !(existing.Source == OrderSources.Offline && existing.RequestHash == legacyOfflineHash))
                throw OrderOperationException.Conflict("Client order key was already used for a different request.");
            return await GetAsync(existing.Id, salesUserId, SystemRoles.Sales, cancellationToken);
        }

        var retailerContext = await store.FindRetailerContextAsync(
            salesUserId, normalized.RetailerId, cancellationToken)
            ?? throw OrderOperationException.Forbidden(
                "Sales can only create orders for an active retailer in the assigned territory.");
        if (retailerContext.Retailer.Status != "Active")
            throw OrderOperationException.Conflict("Retailer is not active.");

        ValidatePayment(normalized.PaymentMethod, retailerContext.Retailer.CreditTermDays);
        var now = timeProvider.GetUtcNow();
        var today = GetVietnamDate(now);
        if (normalized.RequestedDeliveryDate < today)
            throw OrderOperationException.Validation("Requested delivery date cannot be in the past.");

        var orderCode = await store.NextOrderCodeAsync(cancellationToken);
        var plan = await BuildCaptureAsync(
            normalized, retailerContext, salesUserId, orderCode, requestHash, now, cancellationToken,
            OrderSources.Offline, data.OfflineEstimatedAmount);
        var expectedConditions = data.ExpectedConditions ?? [];
        if (expectedConditions.Any(item => item.ProductId == Guid.Empty ||
            item.ExpectedBaseUnitPrice < 0 || item.ExpectedMlorVersion < 1 ||
            item.ExpectedMlorRatio is <= 0 or >= 1) ||
            expectedConditions.GroupBy(item => item.ProductId).Any(group => group.Count() != 1))
        {
            throw OrderOperationException.Validation("Expected offline price and MLOR conditions are invalid.");
        }
        var expectedByProduct = expectedConditions.ToDictionary(item => item.ProductId);
        if (expectedByProduct.Count != plan.Lines.Count)
            throw OrderOperationException.Validation(
                "Expected conditions must contain exactly one entry for every submitted product.");
        var differences = plan.Lines.Select(line =>
        {
            if (!expectedByProduct.TryGetValue(line.ProductId, out var expected)) return null;
            var currentBaseUnitPrice = line.OrderedQuantity == 0 ? 0 :
                decimal.Round(line.NetAmount / line.OrderedQuantity, 2, MidpointRounding.AwayFromZero);
            return expected.ExpectedBaseUnitPrice == currentBaseUnitPrice &&
                   expected.ExpectedMlorVersion == line.MlorSourceVersion &&
                   expected.ExpectedMlorRatio == line.MlorMinimumLifeRatio
                ? null
                : new OfflineConditionDifference(line.ProductId, line.SkuSnapshot,
                    expected.ExpectedBaseUnitPrice, currentBaseUnitPrice,
                    expected.ExpectedMlorVersion, line.MlorSourceVersion,
                    expected.ExpectedMlorRatio, line.MlorMinimumLifeRatio);
        }).Where(item => item is not null).Cast<OfflineConditionDifference>().ToArray();
        var totalChanged = data.OfflineEstimatedAmount != plan.Order.FinalAmount;
        if ((totalChanged || differences.Length != 0) && !data.ConfirmConditionChanges)
        {
            throw OrderOperationException.Conflict(
                "Base-price or MLOR conditions changed. Sales must explicitly confirm before the draft is accepted.",
                new OfflineConditionConflictResult(data.ClientOrderKey, data.SnapshotVersion,
                    data.OfflineEstimatedAmount, plan.Order.FinalAmount, differences));
        }
        try
        {
            await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
            {
                Persist(plan);
                await store.SaveChangesAsync(transactionCancellationToken);
            }, cancellationToken);
        }
        catch (OrderOperationException exception) when (exception.Code == "conflict")
        {
            store.ClearTracking();
            var concurrent = await store.FindByClientOrderKeyAsync(normalized.ClientOrderKey, cancellationToken);
            if (concurrent is null || concurrent.CreatedByUserId != salesUserId || concurrent.RequestHash != requestHash)
                throw;
            return await GetAsync(concurrent.Id, salesUserId, SystemRoles.Sales, cancellationToken);
        }

        store.ClearTracking();
        return await GetAsync(plan.Order.Id, salesUserId, SystemRoles.Sales, cancellationToken);
    }

    public async Task<PagedOrderResult> ListAsync(
        Guid actorUserId,
        string actorRole,
        OrderListQuery query,
        CancellationToken cancellationToken)
    {
        ValidateReadRole(actorRole);
        if (!string.IsNullOrWhiteSpace(query.Status) && !OrderStatusSet.Contains(query.Status))
        {
            throw OrderOperationException.Validation("Order status filter is invalid.");
        }
        if (query.Page < 1 || query.PageSize is < 1 or > 100)
            throw OrderOperationException.Validation("Page must be at least 1 and pageSize must be between 1 and 100.");
        if (query.Search?.Length > 200)
            throw OrderOperationException.Validation("Search cannot exceed 200 characters.");

        var result = await store.ListAsync(actorUserId, actorRole, query, cancellationToken);
        return new(result.Items.Select(ToSummary).ToArray(), query.Page, query.PageSize, result.TotalCount);
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

    public async Task<OrderTimelineResult> GetTimelineAsync(
        Guid orderId,
        Guid actorUserId,
        string actorRole,
        CancellationToken cancellationToken)
    {
        var view = await GetViewableAsync(orderId, actorUserId, actorRole, cancellationToken);
        return new(view.Order.Id, view.Order.OrderCode, view.Order.Status, AllocationState(view),
            view.AllocationContext?.RunId, view.AllocationContext?.PlanStatus, view.AllocationContext?.OrderResult,
            ToHistory(view.History), ToShipments(view.Deliveries));
    }

    public async Task EnsurePricingScopeAsync(
        Guid? retailerId,
        Guid actorUserId,
        string actorRole,
        CancellationToken cancellationToken)
    {
        if (actorRole is SystemRoles.Admin or SystemRoles.Operator) return;
        if (actorRole != SystemRoles.Sales)
            throw OrderOperationException.Forbidden("Role cannot request an order price snapshot.");
        if (!retailerId.HasValue) return;
        if (await store.FindRetailerContextAsync(actorUserId, retailerId.Value, cancellationToken) is null)
            throw OrderOperationException.Forbidden(
                "Sales can only request retailer pricing inside the assigned territory.");
    }

    /// <summary>
    /// Reviewing an order parks it for the next run. It holds no stock: the run picks the
    /// batches and only run approval reserves them, so two approved orders can still compete for
    /// the same batch and the earlier acceptance time wins.
    /// </summary>
    public Task<OrderDetailResult> ApproveAsync(
        Guid orderId,
        int expectedVersion,
        Guid reviewerUserId,
        string actorRole,
        CancellationToken cancellationToken) =>
        TransitionAsync(orderId, expectedVersion, reviewerUserId, actorRole,
            OrderStatuses.PendingAllocation, null, cancellationToken);

    public Task<OrderDetailResult> RejectAsync(
        Guid orderId,
        int expectedVersion,
        string reason,
        Guid reviewerUserId,
        string actorRole,
        CancellationToken cancellationToken)
    {
        reason = CleanOptional(reason, 1_000)
            ?? throw OrderOperationException.Validation("Rejection reason is required.");
        return TransitionAsync(orderId, expectedVersion, reviewerUserId, actorRole,
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

            if (view.Order.Status is not (OrderStatuses.PendingApproval or OrderStatuses.PendingAllocation))
            {
                throw OrderOperationException.Conflict(
                    "Only an order that has not entered an allocation run can be cancelled here.");
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
        if (actorRole is not (SystemRoles.Operator or SystemRoles.Admin))
        {
            throw OrderOperationException.Forbidden("Only Admin or Operator can approve or reject orders.");
        }

        await store.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var view = await store.FindAsync(orderId, true, transactionCancellationToken)
                ?? throw OrderOperationException.NotFound("Order was not found.");
            // Re-sending the same decision is a no-op: nothing is reserved or issued here.
            if (view.Order.Status == targetStatus) return;
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

            ApplyTransition(view.Order, targetStatus, actorUserId, reason, now);
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

    private async Task<CapturePlan> BuildCaptureAsync(
        CreateAssistedOrderData data,
        OrderRetailerContext retailerContext,
        Guid salesUserId,
        string orderCode,
        string requestHash,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        string source = OrderSources.Assisted,
        decimal? offlineEstimatedAmount = null)
    {
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
            Source = source,
            Status = OrderStatuses.PendingApproval,
            PaymentMethod = data.PaymentMethod,
            CreditTermDays = data.PaymentMethod == OrderPaymentMethods.Credit
                ? retailerContext.Retailer.CreditTermDays
                : 0,
            DeliveryAddress = deliveryAddress,
            RequestedDeliveryDate = data.RequestedDeliveryDate,
            Note = data.Note,
            FinalPricePendingAllocation = true,
            ServerReceivedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        var lines = new List<SalesOrderLine>();
        var promotions = new List<OrderPromotion>();
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
            var mlor = await store.ResolveMlorAsync(
                data.RetailerId, product.Id, now, cancellationToken)
                ?? throw OrderOperationException.Conflict(
                    $"No effective MLOR configuration exists for SKU '{product.Sku}'.");
            var price = await pricingService.CalculateAsync(
                product.Id, requestedLine.Quantity, data.RetailerId, now, cancellationToken);

            var line = new SalesOrderLine
            {
                Id = Guid.NewGuid(),
                SalesOrderId = order.Id,
                ProductId = product.Id,
                LineNumber = lineNumber,
                SkuSnapshot = product.Sku,
                ProductNameSnapshot = product.Name,
                OrderedQuantity = requestedLine.Quantity,
                UnitPrice = price.UnitPrice,
                ProductPriceId = price.ProductPriceId,
                ProductPriceEffectiveFrom = price.ProductPriceEffectiveFrom,
                ProductPriceEffectiveTo = price.ProductPriceEffectiveTo,
                PricedAt = now,
                GrossAmount = price.GrossAmount,
                DiscountPercent = price.DiscountPercent,
                DiscountAmount = price.DiscountAmount,
                NetAmount = price.NetAmount,
                DiscountSourceType = price.DiscountSourceType,
                DiscountSourceCode = price.DiscountSourceCode,
                FinalPricePendingAllocation = true,
                MlorSourceScope = mlor.SourceScope,
                MlorSourceRuleId = mlor.SourceRuleId,
                MlorSourceVersion = mlor.SourceVersion,
                MlorProductCategoryId = mlor.ProductCategoryId,
                MlorMinimumLifeRatio = mlor.MinimumLifeRatio,
                MlorMinimumLifeHours = mlor.MinimumLifeHours,
                MlorResolvedAt = mlor.ResolvedAt,
                CreatedAt = now
            };
            if (price.PromotionId.HasValue)
            {
                promotions.Add(new OrderPromotion
                {
                    Id = Guid.NewGuid(),
                    SalesOrderLineId = line.Id,
                    PromotionId = price.PromotionId.Value,
                    PromotionCodeSnapshot = price.DiscountSourceCode!,
                    PromotionNameSnapshot = price.PromotionName!,
                    PromotionTypeSnapshot = price.DiscountSourceType!,
                    MinimumQuantitySnapshot = price.PromotionMinimumQuantity!.Value,
                    EffectiveFromSnapshot = price.PromotionEffectiveFrom!.Value,
                    EffectiveToSnapshot = price.PromotionEffectiveTo!.Value,
                    DiscountPercent = price.DiscountPercent,
                    DiscountAmount = price.DiscountAmount,
                    IsServerFinal = false,
                    AppliedAt = now
                });
            }
            lines.Add(line);
        }

        order.SubtotalAmount = lines.Sum(item => item.GrossAmount);
        order.DiscountAmount = lines.Sum(item => item.DiscountAmount);
        order.FinalAmount = lines.Sum(item => item.NetAmount);
        if (source == OrderSources.Offline)
        {
            // F30: keep the offline quote and flag any gap vs the server-repriced indicative total,
            // so Sales confirms the condition change. The indicative amount is pre-allocation; the
            // final HSD-adjusted price is still computed later at allocation, so this is only a hint.
            order.OfflineEstimatedAmount = offlineEstimatedAmount;
            order.HasOfflineDifference =
                offlineEstimatedAmount.HasValue && offlineEstimatedAmount.Value != order.FinalAmount;
        }
        var history = NewHistory(order.Id, null, OrderStatuses.PendingApproval,
            salesUserId, source == OrderSources.Offline ? "Sales gửi đơn nháp offline" : "Sales tạo đơn hỗ trợ", now);
        var audit = new AuditLog
        {
            Id = Guid.NewGuid(),
            UserAccountId = salesUserId,
            EntityName = nameof(SalesOrder),
            EntityId = order.Id,
            Action = "OrderCaptured",
            NewValues = JsonSerializer.Serialize(new
            {
                order.OrderCode,
                order.RetailerId,
                order.PaymentMethod,
                order.RequestedDeliveryDate,
                order.SubtotalAmount,
                order.DiscountAmount,
                IndicativeAmount = order.FinalAmount,
                order.FinalPricePendingAllocation,
                Lines = lines.Select(item => new
                {
                    item.ProductId,
                    item.ProductPriceId,
                    item.PricedAt,
                    item.MlorSourceScope,
                    item.MlorSourceRuleId,
                    item.MlorSourceVersion,
                    item.MlorMinimumLifeRatio
                })
            }, TraceJsonOptions),
            Reason = "Sales tạo đơn trước phân bổ; chưa chọn lô hoặc thay đổi tồn kho.",
            CreatedAt = now
        };
        return new(order, lines, promotions, history, audit);
    }

    private void Persist(CapturePlan plan)
    {
        store.AddOrder(plan.Order);
        foreach (var line in plan.Lines) store.AddLine(line);
        foreach (var promotion in plan.Promotions) store.AddPromotion(promotion);
        store.AddHistory(plan.History);
        store.AddAuditLog(plan.Audit);
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
        if (targetStatus is OrderStatuses.PendingAllocation or OrderStatuses.Rejected)
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
        FinalPricePendingAllocation(view),
        AllocationState(view),
        view.AllocationContext?.RunId,
        view.AllocationContext?.PlanStatus,
        view.AllocationContext?.OrderResult,
        view.AllocationContext?.WarehouseProgress.Count(item => item.ShipmentId.HasValue) ?? view.Deliveries.Count,
        (int)view.Lines.Sum(item => item.Line.OrderedQuantity),
        view.Lines.Count,
        view.Order.Source,
        view.Order.Version,
        view.Order.CreatedAt)
    {
        OfficialAmount = OfficialAmount(view)
    };

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
        FinalPricePendingAllocation(view),
        AllocationState(view),
        view.AllocationContext?.RunId,
        view.AllocationContext?.PlanStatus,
        view.AllocationContext?.OrderResult,
        view.Order.RejectionReason ?? view.AllocationContext?.ShortageReason,
        view.Order.OfflineEstimatedAmount,
        view.Order.HasOfflineDifference,
        view.Order.Version,
        view.Order.ServerReceivedAt,
        view.Order.CreatedAt,
        view.Order.UpdatedAt,
        view.Lines.OrderBy(item => item.Line.LineNumber).Select(item => ToLine(view, item)).ToArray(),
        ToHistory(view.History),
        ToShipments(view.Deliveries))
    {
        OfficialAmount = OfficialAmount(view),
        AllocationRunCode = view.AllocationContext?.RunCode,
        AllocationPlanVersion = view.AllocationContext?.PlanVersionNumber,
        OfficialPricedAt = view.AllocationContext?.PlanStatus == AllocationPlanVersionStatuses.Approved
            ? view.AllocationContext.PricedAt : null,
        WarehouseProgress = view.AllocationContext?.WarehouseProgress.Select(item =>
            new OrderWarehouseProgressResult(
                item.WarehouseId, item.WarehouseCode, item.PickingListId, item.PickingCode,
                item.PickingStatus, item.ShipmentId, item.ShipmentCode, item.ShipmentStatus,
                item.PlannedQuantity, item.PickedQuantity, item.ReceivedQuantity)).ToArray() ?? []
    };

    private static OrderLineResult ToLine(OrderView view, OrderLineView item) => new(
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
            item.Line.ProductPriceId,
            item.Line.ProductPriceEffectiveFrom,
            item.Line.ProductPriceEffectiveTo,
            item.Line.PricedAt,
            new OrderMlorSnapshotResult(
                item.Line.MlorSourceScope,
                item.Line.MlorSourceRuleId,
                item.Line.MlorSourceVersion,
                item.Line.MlorProductCategoryId,
                item.Line.MlorMinimumLifeRatio,
                item.Line.MlorMinimumLifeHours,
                item.Line.MlorResolvedAt),
            item.OrdinaryPromotion is null ? null : new OrderOrdinaryPromotionSnapshotResult(
                item.OrdinaryPromotion.PromotionId,
                item.OrdinaryPromotion.PromotionCodeSnapshot,
                item.OrdinaryPromotion.PromotionNameSnapshot,
                item.OrdinaryPromotion.PromotionTypeSnapshot,
                item.OrdinaryPromotion.MinimumQuantitySnapshot,
                item.OrdinaryPromotion.DiscountPercent,
                item.OrdinaryPromotion.DiscountAmount,
                item.OrdinaryPromotion.EffectiveFromSnapshot,
                item.OrdinaryPromotion.EffectiveToSnapshot,
                item.OrdinaryPromotion.AppliedAt),
            item.Line.EstimatedArrivalAt,
            item.Line.FinalPricePendingAllocation,
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
                    allocation.Status)).ToArray())
    {
        AllocationFragments = view.AllocationContext?.Fragments
                .Where(fragment => fragment.SalesOrderLineId == item.Line.Id)
                .OrderBy(fragment => fragment.Sequence)
                .Select(fragment => new OrderAllocationFragmentResult(
                    fragment.AllocationId, fragment.PlanPriceId, fragment.BatchId, fragment.BatchCode,
                    fragment.BatchExpirationDate, fragment.WarehouseId, fragment.WarehouseCode,
                    fragment.Quantity, fragment.Sequence, fragment.EstimatedArrivalAt,
                    fragment.RemainingLifeHoursAtArrival, fragment.ListUnitPrice,
                    fragment.ListUnitPrice - fragment.BaseUnitPrice, fragment.BaseUnitPrice,
                    fragment.RemainingShelfLifePercent, fragment.DiscountRuleId,
                    fragment.DiscountRuleScope, fragment.DiscountRuleCode, fragment.DiscountRuleVersion,
                    fragment.DiscountType, fragment.DiscountValue, fragment.ShelfLifeDiscountPerUnit,
                    fragment.FinalUnitPrice, fragment.NetAmount, fragment.DiscountReason)).ToArray() ?? [],
        OfficialNetAmount = view.AllocationContext?.PlanStatus == AllocationPlanVersionStatuses.Approved
                ? view.AllocationContext.Fragments.Where(fragment => fragment.SalesOrderLineId == item.Line.Id)
                    .Sum(fragment => fragment.NetAmount)
                : null
    };

    private static IReadOnlyCollection<OrderStatusHistoryResult> ToHistory(
        IReadOnlyCollection<OrderStatusHistory> history) =>
        history.OrderBy(item => item.ChangedAt).ThenBy(item => item.Id).Select(item =>
            new OrderStatusHistoryResult(item.FromStatus, item.ToStatus, item.Reason,
                item.ChangedByUserId, item.ChangedAt)).ToArray();

    private static IReadOnlyCollection<OrderShipmentLinkResult> ToShipments(
        IReadOnlyCollection<Delivery> deliveries) =>
        deliveries.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id).Select(item =>
            new OrderShipmentLinkResult(item.Id, item.Status, item.DispatchedAt, item.DeliveredAt)).ToArray();

    private static string AllocationState(OrderView view) => view.Order.Status switch
    {
        OrderStatuses.Cancelled or OrderStatuses.Rejected => "NotApplicable",
        _ when view.AllocationContext is not null => view.AllocationContext.PlanStatus == AllocationPlanVersionStatuses.Approved
            ? "Allocated" : "Proposed",
        _ when view.Lines.SelectMany(item => item.Allocations).Any() =>
            view.Order.FinalPricePendingAllocation ? "Proposed" : "Allocated",
        _ => "PendingRun"
    };

    private static decimal? OfficialAmount(OrderView view) =>
        view.AllocationContext?.PlanStatus == AllocationPlanVersionStatuses.Approved
            ? view.AllocationContext.Fragments.Sum(item => item.NetAmount)
            : null;

    private static bool FinalPricePendingAllocation(OrderView view) =>
        view.AllocationContext?.PlanStatus != AllocationPlanVersionStatuses.Approved &&
        view.Order.FinalPricePendingAllocation;

    private static string ComputeRequestHash(CreateAssistedOrderData data, Guid salesUserId)
    {
        var lines = data.Lines.OrderBy(item => item.ProductId)
            .Select(item => $"{item.ProductId:N}:{item.Quantity}");
        var canonical = string.Join('|', salesUserId.ToString("N"), data.RetailerId.ToString("N"),
            data.PaymentMethod, data.RequestedDeliveryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            data.DeliveryAddress, data.Note, string.Join(';', lines));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string ComputeOfflineRequestHash(
        CreateAssistedOrderData normalized,
        SubmitOfflineOrderData offline,
        Guid salesUserId)
    {
        var conditions = (offline.ExpectedConditions ?? []).OrderBy(item => item.ProductId)
            .Select(item => string.Join(':', item.ProductId.ToString("N"),
                item.ExpectedBaseUnitPrice.ToString(CultureInfo.InvariantCulture),
                item.ExpectedMlorVersion.ToString(CultureInfo.InvariantCulture),
                item.ExpectedMlorRatio.ToString(CultureInfo.InvariantCulture)));
        var canonical = string.Join('|', ComputeRequestHash(normalized, salesUserId),
            offline.OfflineEstimatedAmount.ToString(CultureInfo.InvariantCulture),
            offline.SnapshotVersion?.Trim(), string.Join(';', conditions));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

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
        OrderStatuses.PendingAllocation,
        OrderStatuses.Approved,
        OrderStatuses.Picking,
        OrderStatuses.Rejected,
        OrderStatuses.Cancelled,
        OrderStatuses.Issued,
        OrderStatuses.Delivering,
        OrderStatuses.Delivered,
        OrderStatuses.Completed
    ];

    private sealed record CapturePlan(
        SalesOrder Order,
        IReadOnlyCollection<SalesOrderLine> Lines,
        IReadOnlyCollection<OrderPromotion> Promotions,
        OrderStatusHistory History,
        AuditLog Audit);
}
