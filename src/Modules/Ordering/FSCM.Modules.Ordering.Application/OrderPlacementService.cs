using System.Text.Json;
using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Catalog.Contracts;
using FSCM.Modules.Inventory.Contracts;
using FSCM.Modules.Logistics.Contracts;
using FSCM.Modules.Ordering.Contracts;
using FSCM.Modules.Ordering.Domain.Allocation;
using FSCM.Modules.Ordering.Domain.Orders;
using FSCM.Modules.Partners.Contracts;
using FSCM.Modules.Pricing.Contracts;
using FSCM.SharedKernel.Domain;

namespace FSCM.Modules.Ordering.Application;

/// <summary>
/// Creates orders: online (Sales / Operator) and offline sync. Pipeline: retailer → merge lines → price (promotions) → out-of-area and credit guards →
/// weighted allocation B1–B4 (no partial delivery) → batch discounts → hold stock → save order Pending + OrderSubmitted.
/// </summary>
public sealed class OrderPlacementService(IOrderRepository orders, IOrderingUnitOfWork uow, IPartnersModule partners, ICatalogModule catalog,
    IPricingModule pricing, IInventoryModule inventory, ILogisticsModule logistics, IAllocationSettingsProvider settings,
    IdGenerator ids, ICurrentUser user, IClock clock)
{
    private const int MaxHoldAttempts = 3;
    private static readonly TimeZoneInfo Vn = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<OrderCreatedDto> CreateAsync(CreateOrderInput x, CancellationToken ct)
    {
        var o = await PlaceAsync(x, null, offline: false, ct);
        return new OrderCreatedDto(o.Id, o.Status.ToString(), o.GrossAmount, o.DiscountAmount, o.NetAmount, EstimatedCost(o));
    }

    /// <summary>
    /// Offline sync: processed in the order the orders were created on the device; each order is idempotent by ClientRequestId
    /// (a retry after a lost response returns the same result). Prices and promotions are recalculated, and an order whose stock is gone is stored as Rejected.
    /// </summary>
    public async Task<IReadOnlyList<SyncResultItem>> SyncAsync(IReadOnlyList<OfflineOrderInput> batch, CancellationToken ct)
    {
        var results = new List<SyncResultItem>();
        foreach (var item in batch.OrderBy(i => i.CreatedOnDeviceUtc))
        {
            try
            {
                if (await orders.GetByClientRequestAsync(item.ClientRequestId, ct) is { } existing)
                {
                    results.Add(new SyncResultItem(item.ClientRequestId, item.LocalCode, "Duplicate", existing.Id, existing.Status.ToString(), existing.RejectReason));
                    continue;
                }
                var o = await PlaceAsync(item.Order, item, offline: true, ct);
                results.Add(new SyncResultItem(item.ClientRequestId, item.LocalCode, o.Status == OrderStatus.Rejected ? "Rejected" : "Created", o.Id, o.Status.ToString(), o.RejectReason));
            }
            catch (BusinessRuleException ex)
            {
                uow.ClearTracked();
                results.Add(new SyncResultItem(item.ClientRequestId, item.LocalCode, "Failed", null, null, ex.Message));
            }
        }
        return results;
    }

    private async Task<Order> PlaceAsync(CreateOrderInput x, OfflineOrderInput? off, bool offline, CancellationToken ct)
    {
        var today = clock.Today;
        var retailer = await partners.GetRetailerAsync(x.RetailerCode, ct) ?? throw new BusinessRuleException("retailer.not_found", $"Không có retailer {x.RetailerCode}.");
        Guard.Against(!retailer.Active, "retailer.inactive", $"Retailer {retailer.Code} đã ngừng hoạt động.");
        Guard.Against(!Enum.TryParse<PaymentMethod>(x.Payment, true, out var payment), "order.payment", "Thanh toán: COD, Prepaid hoặc Credit.");
        payment = Enum.Parse<PaymentMethod>(x.Payment, true);

        var wanted = x.Lines.GroupBy(l => l.Sku).Select(g => new QuoteLineInput(g.Key, g.Sum(l => l.Quantity))).ToList();
        var quote = await pricing.QuoteAsync(retailer.Code, wanted, today, ct);               // throws on unknown / unpublished SKU or qty ≤ 0
        var names = new Dictionary<string, ProductDto>();
        foreach (var l in wanted) names[l.Sku] = (await catalog.GetProductAsync(l.Sku, ct))!;

        var salesUserId = user.Role == Role.Sales ? user.UserId : null;
        var header = new NewOrder("", retailer.Code, retailer.Name, salesUserId, salesUserId is null ? null : user.FullName, user.UserId, user.FullName,
            offline ? OrderSource.Offline : OrderSource.Online, off?.ClientRequestId, off?.DeviceCode, off?.LocalCode, off?.CreatedOnDeviceUtc,
            payment, payment == PaymentMethod.Credit ? x.CreditTermDays : 0, x.PromisedDate, clock.UtcNow, x.Note);

        List<OrderLine> NewLines() => quote.Lines.Select(q => new OrderLine(q.LineNo, q.Sku, names[q.Sku].Name, q.Quantity, q.UnitPrice, q.PromotionCode, q.PromotionPercent, q.PromotionAmount)).ToList();

        // ---- guards that reject the order (online: error to the caller; offline: stored as Rejected so the Sales sees the reason) ----
        string? violation = null;
        if (salesUserId is not null && !await partners.IsInSalesAreaAsync(salesUserId, retailer.Code, ct))
            violation = $"Retailer {retailer.Code} nằm ngoài khu vực của nhóm Sales – không thể tạo đơn.";
        else if (payment == PaymentMethod.Credit && (x.CreditTermDays is not (7 or 14 or 30) || retailer.CreditTermDays == 0 || x.CreditTermDays > retailer.CreditTermDays))
            violation = $"Retailer {retailer.Code} không được bán chịu kỳ hạn {x.CreditTermDays} ngày (tối đa {retailer.CreditTermDays} ngày).";
        if (violation is not null) return await RejectAsync(header, NewLines(), violation, "{}", offline, ct);

        // ---- allocation + hold (retry on fresh stock when another Sales took the same batch at the same time) ----
        var orderCode = await ids.OrderAsync(ct);
        header = header with { Code = orderCode };
        var weights = (await settings.GetAsync(ct)) with { DmaxRetailerKm = (await logistics.LimitsAsync(ct)).DmaxRetailerKm };
        var engine = new AllocationEngine(weights);
        var dispatch = await logistics.NextDispatchDateAsync(TimeZoneInfo.ConvertTimeFromUtc(clock.UtcNow, Vn), ct);

        for (var attempt = 1; ; attempt++)
        {
            var results = new List<LineAllocationResult>();
            var transit = new Dictionary<string, (decimal Km, int? Days, decimal Cost)>();
            foreach (var q in quote.Lines)
            {
                var p = names[q.Sku];
                var m = await partners.MlorRatioAsync(retailer.Code, q.Sku, ct) ?? p.DefaultMlorRatio;
                var cands = await CandidatesAsync(q.Sku, retailer.Code, today, transit, ct);
                results.Add(engine.Allocate(new AllocationRequest(q.Sku, q.Quantity, p.ShelfLifeDays, m, today, dispatch, x.PromisedDate), cands));
            }
            var trace = JsonSerializer.Serialize(new { orderCode, dispatchDate = dispatch, promisedDate = x.PromisedDate, weights, lines = results,
                deliveryCost = AllocationEngine.DeliveryCost(results.SelectMany(r => r.Takes)).Select(c => new { warehouse = c.Warehouse, km = c.Km, cost = c.Cost }) }, Json);

            if (results.Any(r => !r.Enough))
            {
                var why = string.Join("; ", results.Where(r => !r.Enough).Select(r => $"{r.Sku}: cấp được {r.Allocated}/{r.Need} thùng (thiếu {r.Short})"));
                return await RejectAsync(header, NewLines(), $"Không đủ hàng đạt MLOR – {why}. Không giao một phần nên cả đơn bị từ chối.", trace, offline, ct, noStockHeld: true);
            }

            var discounts = await pricing.ActiveBatchDiscountsAsync(today, ct);
            var lines = NewLines();
            foreach (var line in lines)
            {
                var r = results.First(x2 => x2.Sku == line.Sku);
                line.Allocate(r.Takes.Select(t => new OrderAllocation(t.BatchCode, t.Warehouse, t.Quantity, t.DistanceKm, t.TransitDays, t.Eta, t.RemainingDays, t.Score, t.TripCost)), discounts);
            }
            var holds = lines.SelectMany(l => l.Allocations).GroupBy(a => a.BatchCode).Select(g => new HoldLine(g.Key, g.Sum(a => a.Quantity))).ToList();
            try { await inventory.HoldAsync(orderCode, holds, ct); }
            catch (BusinessRuleException ex) when (ex.Code is "batch.negative" or "concurrency" && attempt < MaxHoldAttempts) { continue; }
            catch (BusinessRuleException ex) when (ex.Code is "batch.negative" or "concurrency")
            {
                return await RejectAsync(header, NewLines(), "Hàng vừa được giữ cho đơn khác, không còn đủ tồn.", trace, offline, ct, noStockHeld: true);
            }

            var order = Order.Place(header, lines, trace);
            try
            {
                orders.Add(order);
                uow.Enqueue(new OrderSubmitted(order.Id, order.RetailerCode, order.RetailerName, order.SalesUserId, order.NetAmount, offline));
                await uow.SaveChangesAsync(ct);
            }
            catch { uow.ClearTracked(); await inventory.ReleaseAsync(orderCode, ct); throw; }   // never leave stock held for an order that was not saved
            return order;
        }
    }

    private async Task<Order> RejectAsync(NewOrder header, List<OrderLine> lines, string reason, string trace, bool offline, CancellationToken ct, bool noStockHeld = true)
    {
        if (!offline) throw new BusinessRuleException("order.rejected", reason);
        var code = string.IsNullOrEmpty(header.Code) ? await ids.OrderAsync(ct) : header.Code;
        var o = Order.PlaceRejected(header with { Code = code }, lines, reason, trace);
        orders.Add(o);
        uow.Enqueue(new OrderRejected(o.Id, o.RetailerCode, o.SalesUserId, reason, StockWasHeld: !noStockHeld));
        await uow.SaveChangesAsync(ct);
        return o;
    }

    private async Task<List<BatchCandidate>> CandidatesAsync(string sku, string retailerCode, DateOnly today, Dictionary<string, (decimal Km, int? Days, decimal Cost)> cache, CancellationToken ct)
    {
        var list = new List<BatchCandidate>();
        foreach (var b in await inventory.AllocatableBatchesAsync(sku, ct))
        {
            if (!cache.TryGetValue(b.Warehouse, out var t))
            {
                var km = await logistics.DistanceKmAsync(b.Warehouse, retailerCode, ct);
                var q = km is null ? null : await logistics.QuoteAsync(b.Warehouse, retailerCode, Purpose.WarehouseToRetailer, today, ct);
                cache[b.Warehouse] = t = (km ?? 999m, q?.TransitDays, q?.TripCost ?? 0m);
            }
            list.Add(new BatchCandidate(b.BatchCode, b.Warehouse, b.WarehouseName, b.Expiry, b.Available, t.Km, t.Days, t.Cost));
        }
        return list;
    }

    internal static decimal EstimatedCost(Order o) =>
        o.Lines.SelectMany(l => l.Allocations).GroupBy(a => a.WarehouseCode).Sum(g => g.First().TripCost);
}
